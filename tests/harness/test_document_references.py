import unittest
import os
from pathlib import Path

class TestDocumentReferences(unittest.TestCase):
    def setUp(self):
        self.base_dir = Path(__file__).parent.parent.parent

    def test_no_dangling_old_agent_paths(self):
        files_to_check = [
            self.base_dir / "AGENTS.md",
            self.base_dir / "README.md"
        ]
        
        old_patterns = [
            "agents/ralph-orchestrator.md",
            "agents/agy-worker.md",
            "agents/first-reviewer.md",
            "agents/tech-expert.md",
            "agents/universal-final-controller.md",
            "agents/Final_Approach_Control.md",
            "agents/terukirdo_plan.md"
        ]
        
        for f in files_to_check:
            if not f.is_file():
                continue
            content = f.read_text(encoding="utf-8")
            for pat in old_patterns:
                self.assertNotIn(pat, content, f"Stale reference '{pat}' found in {f.name}")

    def test_no_dangling_old_plan_paths(self):
        files_to_check = [
            self.base_dir / "AGENTS.md",
            self.base_dir / "README.md"
        ]
        
        old_plan_paths = [
            "Documents/Implementation_Plan.md",
            "IMPLEMENTATION_PROGRESS.md"
        ]
        
        for f in files_to_check:
            if not f.is_file():
                continue
            content = f.read_text(encoding="utf-8")
            for pat in old_plan_paths:
                self.assertNotIn(pat, content, f"Stale plan reference '{pat}' found in {f.name}")

if __name__ == "__main__":
    unittest.main()
