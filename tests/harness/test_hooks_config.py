import json
import os
import unittest
from pathlib import Path

class TestHooksConfig(unittest.TestCase):
    def test_hooks_json_validity(self):
        base_dir = Path(__file__).parent.parent.parent
        hooks_json_path = base_dir / ".agents" / "hooks.json"
        
        self.assertTrue(hooks_json_path.is_file(), "hooks.json file not found")
        
        with open(hooks_json_path, "r", encoding="utf-8") as f:
            data = json.load(f)
            
        self.assertTrue(isinstance(data, dict), "hooks.json root must be a dict")
        self.assertNotIn("hooks", data, "hooks.json should not contain 'hooks' top-level wrapper")
        
        self.assertIn("terukirdo-safety-policy", data)
        self.assertIn("terukirdo-post-review", data)
        self.assertIn("terukirdo-stop-gate", data)
        
        safety = data["terukirdo-safety-policy"]
        self.assertEqual(safety.get("enabled"), True)
        self.assertIn("PreToolUse", safety)
        pre_hooks = safety["PreToolUse"]
        self.assertGreater(len(pre_hooks), 0)
        self.assertIn("matcher", pre_hooks[0])
        self.assertIn("hooks", pre_hooks[0])

if __name__ == "__main__":
    unittest.main()
