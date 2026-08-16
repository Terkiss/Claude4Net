import subprocess
import sys
import json
import unittest
from pathlib import Path

HOOK_PATH = str(Path(__file__).parent.parent.parent / ".agents" / "hooks" / "post_tool_use_review.py")

class TestPostToolHook(unittest.TestCase):
    def run_post_hook(self, payload_dict):
        res = subprocess.run(
            [sys.executable, HOOK_PATH],
            input=json.dumps(payload_dict),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )
        try:
            decision = json.loads(res.stdout.strip())
        except Exception as e:
            raise ValueError(f"Stdout was not valid JSON: {res.stdout}. Stderr: {res.stderr}") from e
        return decision, res.stderr

    def test_post_hook_always_returns_empty_dict(self):
        payload = {
            "toolCall": {
                "name": "write_to_file",
                "args": {
                    "TargetFile": "src/main.py",
                    "CodeContent": "print('hello')"
                }
            }
        }
        decision, stderr = self.run_post_hook(payload)
        self.assertEqual(decision, {})

    def test_post_hook_risky_path_warning(self):
        payload = {
            "toolCall": {
                "name": "write_to_file",
                "args": {
                    "TargetFile": "firebase.json",
                    "CodeContent": "{}"
                }
            }
        }
        decision, stderr = self.run_post_hook(payload)
        self.assertEqual(decision, {})
        self.assertTrue("firebase.json" in stderr or "sensitive" in stderr)

if __name__ == "__main__":
    unittest.main()
