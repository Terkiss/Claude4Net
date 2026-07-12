import os
import re
from datetime import datetime
from typing import List
from skillopt_sleep.types import SessionDigest

def parse_markdown_section(filepath: str, section_regex: str) -> str:
    if not os.path.exists(filepath):
        return ""
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    match = re.search(section_regex, content, re.IGNORECASE | re.MULTILINE)
    if not match:
        return ""
    
    start_idx = match.end()
    header_level = len(re.match(r'(#+)', match.group(0)).group(1))
    next_header_pattern = r'^#{1,' + str(header_level) + r'}\s'
    next_match = re.search(next_header_pattern, content[start_idx:], re.MULTILINE)
    if next_match:
        end_idx = start_idx + next_match.start()
        return content[start_idx:end_idx].strip()
    return content[start_idx:].strip()

def harvest_docs(workspace_dir: str) -> List[SessionDigest]:
    digests = []
    
    memory_path = os.path.join(workspace_dir, "MEMORY.md")
    key_learnings = parse_markdown_section(memory_path, r'^(#+)\s*Key\s*Learnings')
    open_questions = parse_markdown_section(memory_path, r'^(#+)\s*Open\s*Questions')
    
    next_actions_path = os.path.join(workspace_dir, "docs", "handoff", "next-actions.md")
    next_actions = ""
    if os.path.exists(next_actions_path):
        with open(next_actions_path, 'r', encoding='utf-8') as f:
            next_actions = f.read().strip()

    combined_text = ""
    if key_learnings:
        combined_text += f"Key Learnings:\n{key_learnings}\n\n"
    if open_questions:
        combined_text += f"Open Questions:\n{open_questions}\n\n"
    if next_actions:
        combined_text += f"Next Actions:\n{next_actions}\n\n"

    combined_text = combined_text.strip()
    if len(combined_text) > 50:
        digest = SessionDigest(
            session_id="doc_driven_evolution",
            project=workspace_dir,
            git_branch="",
            started_at=datetime.now().isoformat(),
            ended_at=datetime.now().isoformat(),
            user_prompts=[combined_text],
            assistant_finals=["Understood. I will optimize the skills based on these actionable insights."],
            tools_used=[],
            files_touched=[],
            feedback_signals=["pos:lgtm"],
            n_user_turns=1,
            n_assistant_turns=1,
            raw_path="docs"
        )
        digests.append(digest)
        
    return digests
