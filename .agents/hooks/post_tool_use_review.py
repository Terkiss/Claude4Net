#!/usr/bin/env python3
import sys
import subprocess
import json
from pathlib import Path
from common import load_input, respond_json, log_stderr, normalize_path, find_repo_root

HIGH_RISK_PATH_HINTS = [
    "firebase.json",
    ".firebaserc",
    "google-services.json",
    "GoogleService-Info.plist",
    "Info.plist",
    "build.gradle",
    "build.gradle.kts",
    "pubspec.yaml",
    "package.json",
    "pyproject.toml",
    ".env",
    "secrets",
    "auth",
    "permission",
    "migration",
    "release",
    "rollback",
]

def check_python_file_syntax(filepath: Path) -> str:
    if filepath.suffix != ".py":
        return ""
    try:
        import ast
        content = filepath.read_text(encoding="utf-8")
        ast.parse(content, filename=filepath.name)
        return ""
    except SyntaxError as e:
        return f"Python syntax error in {filepath.name}: {e}"
    except Exception as e:
        return f"Failed to check syntax for {filepath.name}: {e}"

def get_git_status_porcelain(root: Path) -> list[str]:
    try:
        output = subprocess.check_output(
            ["git", "status", "--porcelain"],
            cwd=root,
            stderr=subprocess.DEVNULL,
            text=True,
        )
        paths = []
        for line in output.splitlines():
            if not line.strip():
                continue
            path = line[3:].strip()
            if " -> " in path:
                path = path.split(" -> ", 1)[1]
            paths.append(path)
        return paths
    except Exception:
        return []

def main() -> int:
    try:
        payload = load_input()
    except Exception as e:
        log_stderr(f"Post Hook payload error: {e}")
        respond_json({})
        return 0

    root = find_repo_root()
    tool_call = payload.get("toolCall", {})
    args = tool_call.get("args", {})
    target_path_str = args.get("TargetFile") or args.get("AbsolutePath") or ""
    
    analyzed_paths = []
    if target_path_str:
        analyzed_paths.append(normalize_path(target_path_str))
    else:
        changed = get_git_status_porcelain(root)
        analyzed_paths = [normalize_path(str(root / p)) for p in changed]

    warnings = []
    for path_str in analyzed_paths:
        path = Path(path_str)
        for hint in HIGH_RISK_PATH_HINTS:
            if hint.lower() in path.name.lower():
                warnings.append(f"Modification to sensitive/config path: {path.name}")
                break
        if path.is_file():
            syntax_err = check_python_file_syntax(path)
            if syntax_err:
                warnings.append(syntax_err)

    if warnings:
        log_stderr("[post-tool-review] WARNINGS:")
        for w in warnings:
            log_stderr(f"  - {w}")
            
    respond_json({})
    return 0

if __name__ == "__main__":
    main()
