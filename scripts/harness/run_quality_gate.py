#!/usr/bin/env python3
import os
import sys
import json
import subprocess
import argparse
import re
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent.parent
PROJECT_JSON = BASE_DIR / ".agents" / "project.json"
COMMANDS_MD = BASE_DIR / "docs" / "development" / "commands.md"

def load_project_json():
    if PROJECT_JSON.is_file():
        try:
            return json.loads(PROJECT_JSON.read_text(encoding="utf-8"))
        except Exception as e:
            print(f"[quality-gate] Warn: failed to parse project.json: {e}", file=sys.stderr)
    return {}

def parse_commands_md():
    commands = {}
    if COMMANDS_MD.is_file():
        try:
            content = COMMANDS_MD.read_text(encoding="utf-8")
            match = re.search(r'```json\s*\n(.*?)\n```', content, re.DOTALL)
            if match:
                data = json.loads(match.group(1))
                if isinstance(data, dict) and "commands" in data:
                    commands = data["commands"]
        except Exception:
            pass
    return commands

def resolve_commands():
    md_cmds = parse_commands_md()
    proj_json = load_project_json()
    proj_cmds = proj_json.get("commands", {})
    
    cmds = {}
    for k in ["formatCheck", "lint", "build", "targetedTest", "fullTest", "releaseGate"]:
        cmds[k] = proj_cmds.get(k) or md_cmds.get(k) or []
        
    if not any(cmds[k] for k in cmds):
        if (BASE_DIR / "package.json").is_file():
            cmds["build"] = ["npm run build"]
            cmds["fullTest"] = ["npm test"]
        elif (BASE_DIR / "pubspec.yaml").is_file():
            cmds["build"] = ["flutter build apk --analyze-size"]
            cmds["fullTest"] = ["flutter test"]
        elif (BASE_DIR / "pyproject.toml").is_file() or (BASE_DIR / "requirements.txt").is_file():
            cmds["fullTest"] = ["python -m unittest discover tests"]
            
    return cmds

def run_command(cmd_str):
    print(f"[quality-gate] Running command: {cmd_str}")
    try:
        res = subprocess.run(
            cmd_str,
            shell=True,
            cwd=str(BASE_DIR),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=180
        )
        return {
            "command": cmd_str,
            "exit_code": res.returncode,
            "stdout": res.stdout,
            "stderr": res.stderr
        }
    except subprocess.TimeoutExpired as te:
        return {
            "command": cmd_str,
            "exit_code": -1,
            "stdout": te.stdout.decode() if te.stdout else "",
            "stderr": "TIMEOUT EXPIRED (180s)"
        }
    except Exception as e:
        return {
            "command": cmd_str,
            "exit_code": -2,
            "stdout": "",
            "stderr": str(e)
        }

def run_gate(skill_name):
    print(f"[quality-gate] Starting gate for skill: {skill_name}")
    
    dev_docs = ['docs/development/commands.md', 'docs/development/conventions.md', 'docs/development/testing.md']
    preflight_missing = []
    for doc in dev_docs:
        if not (BASE_DIR / doc).is_file():
            preflight_missing.append(doc)
            
    if preflight_missing:
        print(f"[quality-gate] HARNESS_PREFLIGHT_FAIL: Missing required dev docs: {preflight_missing}", file=sys.stderr)
        return "FAIL", f"Missing docs: {preflight_missing}"
        
    print("[quality-gate] HARNESS_PREFLIGHT_PASS")

    resolved = resolve_commands()
    
    cmds_to_run = []
    if skill_name == "implement-feature":
        cmds_to_run = resolved.get("build", []) + resolved.get("lint", [])
    elif skill_name in ["verify-change", "test"]:
        cmds_to_run = resolved.get("targetedTest", []) + resolved.get("fullTest", [])
    elif skill_name == "prepare-release":
        cmds_to_run = resolved.get("releaseGate", [])
        
    if not cmds_to_run:
        print("[quality-gate] PROJECT_VERIFICATION_SKIPPED_NO_COMMAND")
        return "SKIPPED_NO_COMMAND", "No commands resolved to run."

    all_success = True
    results = []
    for cmd in cmds_to_run:
        res = run_command(cmd)
        results.append(res)
        if res["exit_code"] != 0:
            all_success = False
            print(f"[quality-gate] Command failed: {cmd}. Exit code: {res['exit_code']}", file=sys.stderr)
            print(res["stderr"], file=sys.stderr)
            
    if all_success:
        print("[quality-gate] PROJECT_VERIFICATION_PASS")
        return "PASS", "All commands completed successfully."
    else:
        print("[quality-gate] PROJECT_VERIFICATION_FAIL", file=sys.stderr)
        return "FAIL", "One or more verification commands failed."

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Terukirdo Quality Gate Verification Engine")
    parser.add_argument('--skill', type=str, required=True, help='The skill context being verified')
    args = parser.parse_args()
    
    status, msg = run_gate(args.skill)
    if status == "FAIL":
        sys.exit(1)
    sys.exit(0)
