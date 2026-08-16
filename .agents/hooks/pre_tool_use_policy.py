#!/usr/bin/env python3
import sys
import re
import shlex
import os
from pathlib import Path
from common import load_input, respond_json, log_stderr, normalize_path, find_repo_root, check_secret_in_content

# Absolute Deny Command list
DENY_CMD_REGEX = [
    (r"\brm\s+-[^;\n]*r[^;\n]*f\s+/\b", "destructive root deletion (rm -rf /)"),
    (r"\bterraform\s+destroy\b", "destructive infrastructure destroy (terraform destroy)"),
    (r"(?i)\bFormat-Volume\b", "destructive disk formatting (Format-Volume)"),
    (r"\bdotnet\s+ef\s+database\s+drop\b", "destructive database drop (dotnet ef database drop)"),
    (r"(?i)\bDrop-Database\b", "destructive database drop (Drop-Database)"),
]

# Force Ask Command list
FORCE_ASK_CMD_REGEX = [
    (r"\bterraform\s+apply\b", "Terraform infrastructure mutation"),
    (r"\bkubectl\s+(apply|delete|rollout|scale)\b", "Kubernetes production-impacting operation"),
    (r"\bfirebase\s+deploy\b", "Firebase production deploy"),
    (r"\bgcloud\s+app\s+deploy\b", "Google Cloud production deploy"),
    (r"\bfastlane\s+(deliver|supply|pilot|deploy)\b", "Fastlane mobile release action"),
    (r"\b(npm|yarn|pnpm)\s+install\b", "package dependency installation"),
    (r"\bpip\s+install\b", "python package installation"),
    (r"\bflutter\s+pub\s+(add|get)\b", "Flutter package dependency action"),
]

def check_command_line(cmd: str, repo_root_str: str) -> tuple[str, str]:
    for pattern, description in DENY_CMD_REGEX:
        if re.search(pattern, cmd):
            return "deny", f"Destructive command detected: {description}"
            
    try:
        tokens = shlex.split(cmd)
    except Exception:
        tokens = cmd.split()
        
    if not tokens:
        return "allow", ""
        
    main_cmd = os.path.basename(tokens[0]).lower()
    
    if main_cmd == "git":
        git_args = [t.lower() for t in tokens[1:]]
        if "reset" in git_args and "--hard" in git_args:
            return "deny", "Destructive git operation: git reset --hard is strictly denied."
        if "clean" in git_args:
            if "-n" in git_args or "--dry-run" in git_args:
                return "allow", ""
            if "-f" in git_args or "-fd" in git_args or "-fdx" in git_args or "-fx" in git_args:
                return "deny", "Destructive git operation: git clean with force (-f) is strictly denied."
        if "checkout" in git_args and "--" in git_args:
            return "deny", "Destructive git operation: git checkout -- can discard uncommitted user changes and is denied."
        if "commit" in git_args:
            return "force_ask", "Commit operation requires user review and approval."
            
    if main_cmd == "rm" or main_cmd == "remove-item":
        args_lower = [t.lower() for t in tokens[1:]]
        has_recursive = any(x in args_lower for x in ["-r", "-rf", "-fr", "-recurse"])
        has_force = any(x in args_lower for x in ["-f", "-rf", "-fr", "-force"])
        
        for token in tokens[1:]:
            if token.startswith("-"):
                continue
            normalized_target = normalize_path(token)
            if normalized_target in ["/", "c:/", "d:/"]:
                return "deny", "Attempted root filesystem deletion."
            if normalized_target == normalize_path(repo_root_str):
                return "deny", "Attempted deletion of the repository root directory."
            if ".git" in normalized_target.split("/"):
                return "deny", "Attempted deletion or modification of the .git system directory."

    if "supabase" in tokens and "db" in tokens and any(x in tokens for x in ["reset", "push"]):
        return "force_ask", "Database mutation (supabase db reset/push) requires user approval."
        
    for pattern, description in FORCE_ASK_CMD_REGEX:
        if re.search(pattern, cmd):
            return "force_ask", f"Production-impacting command: {description} requires explicit confirmation."
            
    return "allow", ""

def check_file_write(tool_name: str, args: dict, repo_root_str: str, allowed_prefixes: list) -> tuple[str, str]:
    target_file = args.get("TargetFile") or args.get("AbsolutePath")
    if not target_file:
        return "allow", ""
        
    target_path = Path(target_file)
    if not target_path.is_absolute():
        target_path = Path(repo_root_str) / target_path
    normalized_target = normalize_path(str(target_path))
    
    if ".git/" in normalized_target or "/.git" in normalized_target:
        return "deny", "Direct modification of .git internals is strictly denied."
        
    file_name = os.path.basename(normalized_target).lower()
    if file_name in [".env", "secrets", "credentials"]:
        content = args.get("CodeContent") or args.get("ReplacementContent") or ""
        if check_secret_in_content(content):
            return "deny", f"Writing literal secrets directly to {file_name} is strictly denied."
        return "force_ask", f"Writing/modifying sensitive file {file_name} requires explicit user approval."

    is_allowed_path = False
    for prefix in allowed_prefixes:
        if normalized_target.startswith(prefix):
            is_allowed_path = True
            break
    if not is_allowed_path:
        return "deny", f"Workspace escape blocked. Path {target_file} is outside authorized directories."

    if not normalized_target.endswith(".md"):
        content = args.get("CodeContent") or args.get("ReplacementContent") or ""
        if "ReplacementChunks" in args:
            for chunk in args["ReplacementChunks"]:
                content += chunk.get("ReplacementContent", "")
                
        reason = check_secret_in_content(content)
        if reason:
            return "deny", f"Literal secret detected in code content: {reason}."

    return "allow", ""

def main() -> int:
    try:
        payload = load_input()
    except Exception as e:
        respond_json({"decision": "force_ask", "reason": f"Harness internal parse error: {e}"})
        return 0

    tool_call = payload.get("toolCall", {})
    tool_name = tool_call.get("name")
    args = tool_call.get("args", {})
    
    repo_root = find_repo_root()
    repo_root_str = normalize_path(str(repo_root))
    
    allowed_prefixes = [repo_root_str]
    workspace_paths = payload.get("workspacePaths", [])
    for path in workspace_paths:
        allowed_prefixes.append(normalize_path(path))
        
    conversation_id = payload.get("conversationId")
    if conversation_id:
        app_data_path = normalize_path(f"C:/Users/dl200/.gemini/antigravity-cli/brain/{conversation_id}")
        allowed_prefixes.append(app_data_path)
        
    log_stderr(f"Evaluating tool call '{tool_name}'")

    try:
        if tool_name == "run_command":
            cmd = args.get("CommandLine", "")
            decision, reason = check_command_line(cmd, repo_root_str)
            respond_json({"decision": decision, "reason": reason})
        elif tool_name in ["write_to_file", "replace_file_content", "multi_replace_file_content"]:
            decision, reason = check_file_write(tool_name, args, repo_root_str, allowed_prefixes)
            respond_json({"decision": decision, "reason": reason})
        else:
            respond_json({"decision": "allow", "reason": ""})
    except Exception as exc:
        log_stderr(f"Error evaluating hook: {exc}")
        if tool_name in ["run_command", "write_to_file", "replace_file_content", "multi_replace_file_content"]:
            respond_json({"decision": "deny", "reason": f"Harness check exception: {exc}."})
        else:
            respond_json({"decision": "force_ask", "reason": f"Harness check exception: {exc}."})
            
    return 0

if __name__ == "__main__":
    main()
