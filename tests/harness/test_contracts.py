import json
import unittest
import re
from pathlib import Path

class TestContracts(unittest.TestCase):
    def setUp(self):
        self.base_dir = Path(__file__).parent.parent.parent
        self.schemas_dir = self.base_dir / ".agents" / "schemas"

    def load_schema(self, filename):
        path = self.schemas_dir / filename
        self.assertTrue(path.is_file(), f"Schema file not found: {filename}")
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)

    def test_schemas_are_valid_json(self):
        schema_files = [
            "execution-card.schema.json",
            "evidence-bundle.schema.json",
            "finding.schema.json",
            "review-report.schema.json",
            "final-control-report.schema.json"
        ]
        for s in schema_files:
            data = self.load_schema(s)
            self.assertIn("$schema", data)
            self.assertEqual(data["type"], "object")

    def test_finding_pattern(self):
        schema = self.load_schema("finding.schema.json")
        pattern_str = schema["properties"]["id"]["pattern"]
        pattern = re.compile(pattern_str)

        self.assertTrue(pattern.match("P1-SEC-001"))
        self.assertTrue(pattern.match("P2-ARCH-999"))
        self.assertTrue(pattern.match("P3-CODE-102"))
        
        self.assertFalse(pattern.match("P4-SEC-001"))
        self.assertFalse(pattern.match("P1-S-001"))
        self.assertFalse(pattern.match("P1-SECTOR-001"))
        self.assertFalse(pattern.match("P1-SEC-99"))

    def test_final_control_report_verdict(self):
        schema = self.load_schema("final-control-report.schema.json")
        allowed_verdicts = schema["properties"]["verdict"]["enum"]
        
        self.assertNotIn("APPROVED", allowed_verdicts)
        self.assertIn("VERIFIED FOR FINAL CONTROL", allowed_verdicts)
        self.assertIn("APPROVED FOR COMMIT ONLY", allowed_verdicts)

    def test_evidence_bundle_commands_enum(self):
        schema = self.load_schema("evidence-bundle.schema.json")
        status_enum = schema["properties"]["commands"]["items"]["properties"]["status"]["enum"]
        
        self.assertIn("PASS", status_enum)
        self.assertIn("FAIL", status_enum)
        self.assertIn("SKIPPED_NO_COMMAND", status_enum)
        self.assertIn("NOT_RUN", status_enum)

if __name__ == "__main__":
    unittest.main()
