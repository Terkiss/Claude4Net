#!/usr/bin/env python3
import os
import sys
import re

EXPECTED_AGENTS = [
    "ralph-orchestrator",
    "terukirdo-plan",
    "agy-worker",
    "first-reviewer",
    "tech-expert",
    "universal-final-controller",
    "final-approach-control"
]

def parse_frontmatter(content):
    if not content.startswith("---"):
        return None, "File does not start with YAML frontmatter delimiter '---'"
    
    parts = content.split("---", 2)
    if len(parts) < 3:
        return None, "File does not contain closing YAML frontmatter delimiter '---'"
    
    frontmatter_text = parts[1]
    body_text = parts[2]
    
    metadata = {}
    lines = frontmatter_text.splitlines()
    current_key = None
    folded_mode = False
    folded_lines = []
    
    for line in lines:
        line_stripped = line.strip()
        if not line_stripped or line_stripped.startswith("#"):
            continue
        
        if folded_mode:
            if ":" in line and not line.startswith(" ") and not line.startswith("\t"):
                metadata[current_key] = "\n".join(folded_lines).strip()
                folded_mode = False
                folded_lines = []
            else:
                folded_lines.append(line)
                continue
        
        if ":" in line:
            key, val = line.split(":", 1)
            key = key.strip()
            val = val.strip()
            
            if val == ">" or val == "|":
                folded_mode = True
                current_key = key
                folded_lines = []
            else:
                if (val.startswith('"') and val.endswith('"')) or (val.startswith("'") and val.endswith("'")):
                    val = val[1:-1]
                metadata[key] = val
                
    if folded_mode and current_key:
        metadata[current_key] = "\n".join(folded_lines).strip()
        
    return metadata, body_text

def validate_harness(strict=False):
    base_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    agents_dir = os.path.join(base_dir, ".agents", "agents")
    old_agents_dir = os.path.join(base_dir, "agents")
    errors = []
    
    if not os.path.isdir(agents_dir):
        errors.append(f"Directory not found: {agents_dir}")
        return errors

    found_agents = []
    for agent in EXPECTED_AGENTS:
        agent_path = os.path.join(agents_dir, agent, "agent.md")
        if not os.path.isfile(agent_path):
            errors.append(f"Missing agent file: .agents/agents/{agent}/agent.md")
            continue
        
        try:
            with open(agent_path, "r", encoding="utf-8") as f:
                content = f.read()
        except Exception as e:
            errors.append(f"Failed to read {agent_path}: {e}")
            continue
            
        metadata, _ = parse_frontmatter(content)
        if not isinstance(metadata, dict):
            errors.append(f"Invalid frontmatter in .agents/agents/{agent}/agent.md: {metadata}")
            continue
            
        name = metadata.get("name")
        if name != agent:
            errors.append(f"Name mismatch in .agents/agents/{agent}/agent.md: frontmatter name '{name}' != folder name '{agent}'")
            
        description = metadata.get("description")
        if not description:
            errors.append(f"Missing description in .agents/agents/{agent}/agent.md frontmatter")
            
        found_agents.append(agent)

    for folder in os.listdir(agents_dir):
        folder_path = os.path.join(agents_dir, folder)
        if os.path.isdir(folder_path) and folder not in EXPECTED_AGENTS:
            errors.append(f"Unexpected custom agent folder found: .agents/agents/{folder}")

    if os.path.isdir(old_agents_dir):
        for f in os.listdir(old_agents_dir):
            if f not in ["README.md", ".gitkeep"] and f.endswith(".md"):
                errors.append(f"Stale duplicate agent file found in root agents/ directory: agents/{f}")

    return errors

if __name__ == "__main__":
    strict = "--strict" in sys.argv
    errors = validate_harness(strict=strict)
    
    if errors:
        print("Harness Validation FAILED with following errors:", file=sys.stderr)
        for err in errors:
            print(f"- {err}", file=sys.stderr)
        sys.exit(1)
    else:
        print("Harness Validation PASSED.")
        sys.exit(0)
