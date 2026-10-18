import urllib.request
import json
import time

def test_models():
    models = [
        'gemini-3.8-flash-high',
        'gemini-3.8-flash',
        'gemini-2.5-flash',
        'claude-sonnet-4-6',
        'claude-opus-4-6-thinking',
        'gpt-oss-120b-medium',
        'gpt-4o'
    ]
    print(f"Testing {len(models)} models against http://127.0.0.1:5055/v1 ...\n")
    for m in models:
        t0 = time.time()
        payload = {
            "model": m,
            "messages": [
                {"role": "user", "content": "Respond with one sentence about what you are."}
            ],
            "temperature": 0.2
        }
        req = urllib.request.Request(
            'http://127.0.0.1:5055/v1/chat/completions',
            data=json.dumps(payload).encode('utf-8'),
            headers={'Content-Type': 'application/json'}
        )
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                elapsed = time.time() - t0
                data = json.loads(resp.read().decode('utf-8'))
                content = data["choices"][0]["message"]["content"].strip()
                reasoning = data.get("usage", {}).get("completion_tokens_details", {}).get("reasoning_tokens", 0)
                print(f"[OK] {m:25} ({elapsed:4.2f}s) -> {content[:60]}... (reasoning: {reasoning})")
        except urllib.error.HTTPError as e:
            err_body = e.read().decode('utf-8')
            print(f"[FAIL] {m:25} -> HTTP {e.code}: {err_body[:100]}")
        except Exception as e:
            print(f"[FAIL] {m:25} -> {e}")

if __name__ == "__main__":
    test_models()
