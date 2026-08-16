import unittest
import json
import os
import subprocess
import sys
from pathlib import Path

class TestSkillIndexer(unittest.TestCase):
    def setUp(self):
        self.base_dir = Path(__file__).parent.parent.parent
        self.indexer_script = self.base_dir / ".agents" / "skills" / "self-evolution" / "scripts" / "skill_indexer.py"
        self.meta_file = self.base_dir / ".agents" / "skills" / "self-evolution" / "scripts" / "index" / "skills_meta.json"

    def test_indexer_build_and_count(self):
        res = subprocess.run(
            [sys.executable, str(self.indexer_script), "--build"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )
        self.assertEqual(res.returncode, 0, f"Indexer build failed: {res.stderr}")
        
        self.assertTrue(self.meta_file.is_file(), "Metadata file not generated")
        
        with open(self.meta_file, "r", encoding="utf-8") as f:
            data = json.load(f)
            
        self.assertEqual(len(data), 15, f"Expected 15 skills, but found {len(data)}")
        
        for item in data:
            path = item["filepath"]
            self.assertFalse(path.startswith("/"), f"Path must be relative: {path}")
            self.assertFalse(path.startswith("C:"), f"Path must be relative: {path}")
            self.assertFalse(path.startswith("D:"), f"Path must be relative: {path}")
            self.assertTrue(path.endswith("SKILL.md"), f"Must only index SKILL.md: {path}")

    def test_lexical_search_fallback(self):
        res = subprocess.run(
            [sys.executable, str(self.indexer_script), "--search", "UI design", "--k", "3"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )
        self.assertEqual(res.returncode, 0, f"Search failed: {res.stderr}")
        
        results = json.loads(res.stdout.strip())
        self.assertTrue(isinstance(results, list))
        self.assertLessEqual(len(results), 3)
        
        names = [r["name"] for r in results]
        self.assertTrue(any("design-ui" in name for name in names), f"Expected design-ui in results: {names}")

if __name__ == "__main__":
    unittest.main()
