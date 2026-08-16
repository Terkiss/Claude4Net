import subprocess
import sys
import json
import unittest
from pathlib import Path

HOOK_PATH = str(Path(__file__).parent.parent.parent / ".agents" / "hooks" / "stop_quality_gate.py")
STATE_FILE_PATH = Path(__file__).parent.parent.parent / ".agents" / "state" / "stop_gate_state.json"

class TestStopHook(unittest.TestCase):
    def run_stop_hook(self, payload_dict):
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

    def test_stop_hook_tier0_passed(self):
        if STATE_FILE_PATH.is_file():
            STATE_FILE_PATH.unlink()
            
        payload = {
            "conversationId": "test-conv-123"
        }
        decision, stderr = self.run_stop_hook(payload)
        self.assertEqual(decision["status"], "passed")

    def test_stop_hook_retry_limit(self):
        dummy_file = Path(__file__).parent.parent.parent / "tests" / "harness" / "dummy_code.py"
        dummy_file.write_text("a = 1", encoding="utf-8")
        
        try:
            if STATE_FILE_PATH.is_file():
                STATE_FILE_PATH.unlink()
                
            payload = {
                "conversationId": "test-conv-123"
            }
            
            decision1, stderr1 = self.run_stop_hook(payload)
            self.assertIn(decision1["status"], ["continue", "passed"])
            
            if decision1["status"] == "continue":
                decision2, stderr2 = self.run_stop_hook(payload)
                self.assertEqual(decision2["status"], "blocked")
        finally:
            if dummy_file.is_file():
                dummy_file.unlink()
            if STATE_FILE_PATH.is_file():
                STATE_FILE_PATH.unlink()

if __name__ == "__main__":
    unittest.main()
