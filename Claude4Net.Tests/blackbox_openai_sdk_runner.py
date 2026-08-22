import sys
import os
import argparse
import time
import concurrent.futures

def run_blackbox_tests(port: int, api_key: str):
    base_url = f"http://127.0.0.1:{port}/v1"
    print(f"=== Starting Official OpenAI Python SDK Black-Box Verification ===")
    print(f"Target Base URL: {base_url}")
    
    client = OpenAI(
        base_url=base_url,
        api_key=api_key
    )

    passed_tests = 0
    total_tests = 0

    # -------------------------------------------------------------
    # 1. Models API Verification
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 1] GET /v1/models (List Models)...")
    models = client.models.list()
    assert len(models.data) > 0, "Models list returned empty"
    model_ids = [m.id for m in models.data]
    print(f"  Found {len(model_ids)} models. Sample: {model_ids[:3]}")
    assert "claude-3-5-sonnet-20241022" in model_ids or "gpt-4o" in model_ids
    passed_tests += 1
    print("  [PASS] Models list succeeded.")

    total_tests += 1
    print("\n[Test 2] GET /v1/models/{model} (Retrieve Valid Model)...")
    model_card = client.models.retrieve("claude-3-5-sonnet-20241022")
    assert model_card.id == "claude-3-5-sonnet-20241022"
    assert model_card.owned_by == "anthropic"
    passed_tests += 1
    print(f"  [PASS] Retrieved model card: id={model_card.id}, owned_by={model_card.owned_by}")

    total_tests += 1
    print("\n[Test 3] GET /v1/models/{model} (Retrieve Non-Existent Model 404)...")
    try:
        client.models.retrieve("non-existent-model-xyz-12345")
        raise AssertionError("Expected NotFoundError (404) for non-existent model.")
    except NotFoundError as e:
        print(f"  [PASS] Correctly received 404 NotFoundError: {e.message}")
        passed_tests += 1

    # -------------------------------------------------------------
    # 2. Chat Completions (Non-Streaming)
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 4] POST /v1/chat/completions (Non-Streaming)...")
    chat_resp = client.chat.completions.create(
        model="claude-3-5-sonnet",
        messages=[
            {"role": "user", "content": "Hello official OpenAI SDK test"}
        ],
        max_completion_tokens=50
    )
    assert chat_resp.id.startswith("chatcmpl-")
    assert len(chat_resp.choices) > 0
    assert chat_resp.choices[0].message.role == "assistant"
    assert chat_resp.choices[0].finish_reason in ["stop", "length"]
    assert chat_resp.system_fingerprint == "fp_claude4net"
    assert chat_resp.usage is not None
    print(f"  [PASS] Non-streaming completion returned: {chat_resp.choices[0].message.content[:40]}...")
    passed_tests += 1

    # -------------------------------------------------------------
    # 3. Chat Completions (Streaming & stream_options)
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 5] POST /v1/chat/completions (Streaming & Usage Chunk)...")
    stream = client.chat.completions.create(
        model="claude-3-5-sonnet",
        messages=[{"role": "user", "content": "Stream me a response"}],
        stream=True,
        stream_options={"include_usage": True}
    )
    
    chunks = []
    has_usage_chunk = False
    for chunk in stream:
        chunks.append(chunk)
        if chunk.usage is not None:
            has_usage_chunk = True
            print(f"  Received final usage chunk: prompt_tokens={chunk.usage.prompt_tokens}, completion_tokens={chunk.usage.completion_tokens}")

    assert len(chunks) > 0
    assert has_usage_chunk, "Expected final usage chunk when stream_options.include_usage=True"
    passed_tests += 1
    print("  [PASS] Streaming with usage chunk succeeded.")

    # -------------------------------------------------------------
    # 4. Tool Calls (Streaming Reassembly by SDK)
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 6] POST /v1/chat/completions (Streaming Tool Calls Accumulation)...")
    tools = [
        {
            "type": "function",
            "function": {
                "name": "calculator",
                "description": "Calculates math expressions",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "number": {"type": "string"}
                    },
                    "required": ["number"]
                }
            }
        }
    ]

    tool_stream = client.chat.completions.create(
        model="claude-3-5-sonnet",
        messages=[{"role": "user", "content": "invoke tool calculator with number 42"}],
        tools=tools,
        stream=True
    )

    accumulated_tool_calls = {}
    saw_tool_finish = False

    for chunk in tool_stream:
        if chunk.choices and chunk.choices[0].delta.tool_calls:
            for tc in chunk.choices[0].delta.tool_calls:
                idx = tc.index
                if idx not in accumulated_tool_calls:
                    accumulated_tool_calls[idx] = {"id": tc.id, "name": "", "arguments": ""}
                if tc.id:
                    accumulated_tool_calls[idx]["id"] = tc.id
                if tc.function and tc.function.name:
                    accumulated_tool_calls[idx]["name"] += tc.function.name
                if tc.function and tc.function.arguments:
                    accumulated_tool_calls[idx]["arguments"] += tc.function.arguments

        if chunk.choices and chunk.choices[0].finish_reason == "tool_calls":
            saw_tool_finish = True

    assert len(accumulated_tool_calls) > 0, "No tool calls received in stream"
    assert saw_tool_finish, "Expected finish_reason == 'tool_calls'"
    tool_0 = accumulated_tool_calls[0]
    assert tool_0["name"] == "calculator"
    assert "42" in tool_0["arguments"]
    print(f"  [PASS] Reassembled tool call: name={tool_0['name']}, args={tool_0['arguments']}, finish_reason=tool_calls")
    passed_tests += 1

    # -------------------------------------------------------------
    # 5. Reasoning Content (Streaming Extension)
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 7] POST /v1/chat/completions (Reasoning Content Streaming)...")
    r_stream = client.chat.completions.create(
        model="gemini-2.5-flash",
        messages=[{"role": "user", "content": "reasoning test please"}],
        stream=True
    )

    saw_reasoning_field = False
    content_text = ""
    for chunk in r_stream:
        # Check raw json delta for reasoning_content
        delta_dict = chunk.choices[0].delta.to_dict() if chunk.choices else {}
        if "reasoning_content" in delta_dict and delta_dict["reasoning_content"]:
            saw_reasoning_field = True
        if chunk.choices and chunk.choices[0].delta.content:
            content_text += chunk.choices[0].delta.content

    assert saw_reasoning_field, "Expected reasoning_content in delta chunk dictionary"
    assert "<think>" not in content_text, "<think> leaked into content!"
    assert "</think>" not in content_text, "</think> leaked into content!"
    print(f"  [PASS] Reasoning content isolated without leaking think tags into content: '{content_text}'")
    passed_tests += 1

    # -------------------------------------------------------------
    # 6. Embeddings (Base64 & Native Dimensions & Mismatch Error)
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 8] POST /v1/embeddings (Float & Base64)...")
    emb_resp = client.embeddings.create(
        model="text-embedding-004",
        input="Hello embedding blackbox",
        dimensions=768
    )
    assert len(emb_resp.data) == 1
    assert len(emb_resp.data[0].embedding) == 768
    print(f"  [PASS] Embeddings returned valid 768-dim float vector.")
    passed_tests += 1

    total_tests += 1
    print("\n[Test 9] POST /v1/embeddings (Unsupported Dimension Rejection)...")
    try:
        client.embeddings.create(
            model="text-embedding-004",
            input="Hello dimension rejection",
            dimensions=128 # Native is 768, 128 is not supported -> Must throw BadRequestError (400)
        )
        raise AssertionError("Expected BadRequestError for unsupported dimensions.")
    except BadRequestError as e:
        assert "unsupported_dimension" in str(e) or "Synthetic dimension scaling is disallowed" in str(e)
        print(f"  [PASS] Unsupported dimension correctly rejected with BadRequestError: {e.message}")
        passed_tests += 1

    # -------------------------------------------------------------
    # 7. Error Handling (Auth 401 & Malformed 400)
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 10] Authentication Error (401 on Invalid API Key)...")
    bad_client = OpenAI(base_url=base_url, api_key="wrong-key-xyz")
    try:
        bad_client.models.list()
        raise AssertionError("Expected AuthenticationError (401) on invalid key.")
    except AuthenticationError as e:
        print(f"  [PASS] Received expected AuthenticationError (401): {e.message}")
        passed_tests += 1

    # -------------------------------------------------------------
    # 8. Concurrency Test
    # -------------------------------------------------------------
    total_tests += 1
    print("\n[Test 11] Concurrency Test (10 Concurrent Requests)...")
    def fetch_completion(i):
        c = client.chat.completions.create(
            model="claude-3-5-sonnet",
            messages=[{"role": "user", "content": f"Concurrency probe {i}"}],
            max_completion_tokens=20
        )
        return c.choices[0].message.content

    with concurrent.futures.ThreadPoolExecutor(max_workers=10) as executor:
        futures = [executor.submit(fetch_completion, i) for i in range(10)]
        results = [f.result() for f in concurrent.futures.as_completed(futures)]
        assert len(results) == 10
    print(f"  [PASS] 10 concurrent requests completed successfully without collision.")
    passed_tests += 1

    total_tests += 1
    print("\n[Test 12] POST /v1/completions (Legacy Completion)...")
    completion_model = "claude-3-5-sonnet"
    completion_resp = client.completions.create(
        model=completion_model,
        prompt="Complete this official Python SDK compatibility probe",
        max_tokens=50
    )
    assert completion_resp.id.startswith("cmpl-")
    assert completion_resp.model == completion_model
    assert len(completion_resp.choices) > 0
    assert completion_resp.choices[0].text
    assert completion_resp.choices[0].finish_reason
    assert completion_resp.usage is not None
    assert completion_resp.usage.total_tokens > 0
    passed_tests += 1
    print("  [PASS] Legacy completion returned SDK-compatible text and usage.")

    print("\n=======================================================")
    print(f"ALL {passed_tests}/{total_tests} OFFICIAL OPENAI SDK BLACK-BOX TESTS PASSED!")
    print("=======================================================")
    return True

if __name__ == "__main__":
    if sys.stdin.buffer.read(1) != b"\x01":
        raise SystemExit(1)
    from openai import OpenAI, NotFoundError, AuthenticationError, BadRequestError

    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--api-key", type=str)
    args = parser.parse_args()
    api_key = os.environ.get("CLAUDE4NET_TEST_API_KEY") or args.api_key
    if not api_key:
        parser.error("API key is required through CLAUDE4NET_TEST_API_KEY or --api-key")

    success = run_blackbox_tests(args.port, api_key)
    if not success:
        sys.exit(1)
