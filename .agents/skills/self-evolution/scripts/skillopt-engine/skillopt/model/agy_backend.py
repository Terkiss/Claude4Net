"""Antigravity CLI (agy) backend for ReflACT."""
from __future__ import annotations

import os
import subprocess
import tempfile
from typing import Any

from skillopt.model.common import tracker


AGY_BIN = os.environ.get("AGY_CLI_BIN", "agy")


def chat_target(
    system: str,
    user: str,
    max_completion_tokens: int = 16384,
    retries: int = 5,
    stage: str = "target",
    reasoning_effort: str | None = None,
    timeout: int | None = None,
) -> tuple[str, dict]:
    prompt_file = tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.txt', encoding='utf-8')
    prompt_file.write(f"System:\n{system}\n\nUser:\n{user}")
    prompt_file.close()
    
    try:
        # Simplistic wrapper assuming agy can accept a file as prompt
        cmd = [AGY_BIN, "ask", "--file", prompt_file.name]
        result = subprocess.run(cmd, capture_output=True, text=True, check=True)
        out_text = result.stdout.strip()
    except subprocess.CalledProcessError as e:
        out_text = f"AGY execution failed (return code {e.returncode}):\n{e.stderr.strip() or e.stdout.strip()}"
    except Exception as e:
        out_text = f"AGY execution failed: {e}"
    finally:
        os.unlink(prompt_file.name)
        
    usage = {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0}
    tracker.add(stage, usage)
    return out_text, usage


def chat_target_messages(
    messages: list[dict[str, Any]],
    max_completion_tokens: int = 16384,
    retries: int = 5,
    stage: str = "target",
    reasoning_effort: str | None = None,
    *,
    tools: list[dict[str, Any]] | None = None,
    tool_choice: str | dict[str, Any] | None = None,
    return_message: bool = False,
    timeout: int | None = None,
) -> tuple[Any, dict]:
    text = ""
    for m in messages:
        role = m.get("role", "")
        content = m.get("content", "")
        if isinstance(content, list):
            content = " ".join([str(c.get("text", "")) for c in content if isinstance(c, dict)])
        text += f"{role.capitalize()}: {content}\n\n"
    
    out_text, usage = chat_target(system="", user=text, stage=stage)
    if return_message:
        return {"role": "assistant", "content": out_text}, usage
    return out_text, usage


def get_token_summary() -> dict:
    return tracker.summary()


def reset_token_tracker() -> None:
    tracker.reset()


def set_reasoning_effort(effort: str | None) -> None:
    pass


def set_target_deployment(deployment: str) -> None:
    pass


def set_optimizer_deployment(deployment: str) -> None:
    pass
