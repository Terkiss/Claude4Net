import sys
import json
import os
import re
from pathlib import Path
from typing import Any, Dict

SECRET_PATTERNS = [
    (r"(?i)(api[_-]?key|secret_key|auth_token|access_token|password)\s*[:=]\s*['\"]?[A-Za-z0-9_\-\.]{10,}", "possible secret literal"),
    (r"AIza[0-9A-Za-z\-_]{10,}", "possible Google API key"),
    (r"sk-[A-Za-z0-9_\-]{10,}", "possible OpenAI/API key"),
    (r"ghp_[A-Za-z0-9]{10,}", "possible GitHub personal access token"),
    (r"xoxb-[A-Za-z0-9\-]{10,}", "possible Slack bot token"),
]

def log_stderr(message: str) -> None:
    print(f"[harness-debug] {message}", file=sys.stderr)
    sys.stderr.flush()

def load_input() -> Dict[str, Any]:
    try:
        raw = sys.stdin.read()
        if not raw.strip():
            log_stderr("Empty input payload received.")
            return {}
        data = json.loads(raw)
        if not isinstance(data, dict):
            log_stderr(f"Input is not a JSON object: {type(data)}")
            return {"raw": data}
        return data
    except Exception as e:
        log_stderr(f"Error parsing input JSON: {e}")
        return {"error": str(e)}

def respond_json(decision_data: Dict[str, Any]) -> None:
    output = json.dumps(decision_data, ensure_ascii=False)
    print(output)
    sys.stdout.flush()
    sys.exit(0)

def normalize_path(path_str: str) -> str:
    if not path_str:
        return ""
    norm = os.path.normpath(path_str)
    return norm.replace('\\', '/')

def find_repo_root() -> Path:
    import subprocess
    try:
        out = subprocess.check_output(
            ["git", "rev-parse", "--show-toplevel"],
            stderr=subprocess.DEVNULL,
            text=True,
        ).strip()
        if out:
            return Path(out)
    except Exception:
        pass
    return Path.cwd()

def check_secret_in_content(content: str) -> str:
    for pattern, reason in SECRET_PATTERNS:
        if re.search(pattern, content):
            return reason
    return ""
