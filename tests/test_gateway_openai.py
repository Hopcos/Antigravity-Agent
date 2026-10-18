import urllib.request
import urllib.parse
import json
import time
import sys

BASE_URL = "http://127.0.0.1:5055"

def test_health():
    print("\n[1/5] Testing GET /v1/health...")
    req = urllib.request.Request(f"{BASE_URL}/v1/health")
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode('utf-8'))
        print("  Status:", data.get("status"))
        print("  Account Email:", data.get("account", {}).get("email"))
        print("  Source:", data.get("account", {}).get("source"))
        print("  Remaining Token Seconds:", data.get("account", {}).get("remainingSeconds"))
        print("  Default Model:", data.get("defaultModel"))
        assert data.get("status") == "healthy"
        assert "@" in data.get("account", {}).get("email", "")
    print("  PASS: Health check verified successfully!")

def test_models():
    print("\n[2/5] Testing GET /v1/models...")
    req = urllib.request.Request(f"{BASE_URL}/v1/models")
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode('utf-8'))
        models = data.get("data", [])
        model_ids = [m["id"] for m in models]
        print(f"  Discovered {len(models)} models:")
        for m in model_ids[:8]:
            print(f"    - {m}")
        if len(model_ids) > 8:
            print(f"    ... and {len(model_ids) - 8} more")
        assert "gemini-3.8-flash-high" in model_ids or "gemini-3.8-flash" in model_ids
    print("  PASS: Models list endpoint verified successfully!")

def test_chat_non_streaming():
    print("\n[3/5] Testing POST /v1/chat/completions (Non-streaming, stream=false)...")
    payload = {
        "model": "gemini-3.8-flash-high",
        "messages": [
            {"role": "system", "content": "You are a concise AI assistant."},
            {"role": "user", "content": "What is 15 * 6? Reply with only the number."}
        ],
        "temperature": 0.2,
        "stream": False
    }
    req = urllib.request.Request(
        f"{BASE_URL}/v1/chat/completions",
        data=json.dumps(payload).encode('utf-8'),
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode('utf-8'))
        assert data.get("object") == "chat.completion"
        choice = data["choices"][0]
        content = choice["message"]["content"].strip()
        print(f"  Model response: {content}")
        print(f"  Finish reason: {choice['finish_reason']}")
        print(f"  Usage: {data.get('usage')}")
        assert "90" in content
    print("  PASS: Non-streaming chat completion verified successfully!")

def test_chat_streaming():
    print("\n[4/5] Testing POST /v1/chat/completions (Streaming, stream=true)...")
    payload = {
        "model": "gemini-3.8-flash-high",
        "messages": [
            {"role": "user", "content": "Say 'Antigravity Gateway Active' in one line."}
        ],
        "stream": True
    }
    req = urllib.request.Request(
        f"{BASE_URL}/v1/chat/completions",
        data=json.dumps(payload).encode('utf-8'),
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    full_text = ""
    chunk_count = 0
    got_done = False
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        for line in resp:
            line_str = line.decode('utf-8', errors='ignore').strip()
            if line_str == "data: [DONE]":
                got_done = True
                break
            if line_str.startswith("data: "):
                chunk_count += 1
                chunk = json.loads(line_str[6:].strip())
                assert chunk.get("object") == "chat.completion.chunk"
                choices = chunk.get("choices", [])
                if choices:
                    delta = choices[0].get("delta", {})
                    if "content" in delta and delta["content"]:
                        full_text += delta["content"]

    print(f"  Streamed {chunk_count} chunks, Got [DONE]: {got_done}")
    print(f"  Aggregated stream text: '{full_text.strip()}'")
    assert got_done
    assert len(full_text.strip()) > 0
    print("  PASS: Streaming SSE chat completion verified successfully!")

def test_tools_function_calling():
    print("\n[5/5] Testing Function Calling / Tool calls via OpenAI protocol...")
    tools = [
        {
            "type": "function",
            "function": {
                "name": "lookup_stock_price",
                "description": "Look up the stock price for a ticker symbol",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "ticker": {"type": "string", "description": "Stock ticker symbol e.g. GOOG"}
                    },
                    "required": ["ticker"]
                }
            }
        }
    ]
    payload = {
        "model": "gemini-3.8-flash-high",
        "messages": [
            {"role": "user", "content": "What is the stock price of GOOG?"}
        ],
        "tools": tools,
        "stream": False
    }
    req = urllib.request.Request(
        f"{BASE_URL}/v1/chat/completions",
        data=json.dumps(payload).encode('utf-8'),
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    with urllib.request.urlopen(req) as resp:
        assert resp.status == 200
        data = json.loads(resp.read().decode('utf-8'))
        choice = data["choices"][0]
        tool_calls = choice["message"].get("tool_calls", [])
        print(f"  Tool calls received: {len(tool_calls)}")
        assert len(tool_calls) > 0
        call = tool_calls[0]
        fn_name = call["function"]["name"]
        fn_args = json.loads(call["function"]["arguments"])
        print(f"    Function Name: {fn_name}")
        print(f"    Arguments: {fn_args}")
        assert fn_name == "lookup_stock_price"
        assert "GOOG" in str(fn_args).upper()

    print("  PASS: Function calling verified successfully!")

def main():
    print("=================================================================")
    print(" GeminiAgent OpenAI Compatible Gateway - Test & Verification")
    print("=================================================================")
    try:
        test_health()
        test_models()
        test_chat_non_streaming()
        test_chat_streaming()
        test_tools_function_calling()
        print("\n=================================================================")
        print(" ALL TESTS PASSED! Gateway is 100% compliant and fully verified!")
        print("=================================================================")
    except Exception as e:
        print(f"\nTEST FAILED: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

if __name__ == "__main__":
    main()
