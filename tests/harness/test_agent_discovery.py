import subprocess
import sys
import unittest
from pathlib import Path

class TestAgentDiscovery(unittest.TestCase):
    def test_validate_harness_script(self):
        base_dir = Path(__file__).parent.parent.parent
        script_path = base_dir / "scripts" / "harness" / "validate_harness.py"
        
        self.assertTrue(script_path.is_file(), "validate_harness.py script not found")
        
        res = subprocess.run(
            [sys.executable, str(script_path), "--strict"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )
        
        self.assertEqual(res.returncode, 0, f"validate_harness.py script failed with code {res.returncode}. Stderr: {res.stderr}")

if __name__ == "__main__":
    unittest.main()
