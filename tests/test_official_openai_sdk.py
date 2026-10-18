from openai import OpenAI
import json

def test_official_sdk():
    print("Testing with official OpenAI Python SDK (openai.OpenAI)...")
    client = OpenAI(
        base_url="http://127.0.0.1:5055/v1",
        api_key="any-key"  # Not enforced unless GEMINI_API_KEY is configured
    )

    # 1. Models list
    print("\n1. client.models.list():")
    models = client.models.list()
    model_ids = [m.id for m in models.data]
    print(f"   Found {len(model_ids)} models. Sample: {model_ids[:5]}")
    assert len(model_ids) > 0

    # 2. Non-streaming completion
    print("\n2. client.chat.completions.create(stream=False):")
    response = client.chat.completions.create(
        model="gemini-3.8-flash",
        messages=[
            {"role": "system", "content": "You are a helpful assistant."},
            {"role": "user", "content": "What is the capital of France? Reply in one word."}
        ]
    )
    ans = response.choices[0].message.content.strip()
    print("   Answer:", ans)
    assert "Paris" in ans
    print("   Usage:", response.usage)

    # 3. Streaming completion
    print("\n3. client.chat.completions.create(stream=True):")
    stream = client.chat.completions.create(
        model="gemini-3.8-flash",
        messages=[
            {"role": "user", "content": "Count from 1 to 3 separated by commas."}
        ],
        stream=True
    )
    stream_text = ""
    for chunk in stream:
        if chunk.choices and chunk.choices[0].delta.content:
            stream_text += chunk.choices[0].delta.content
    print("   Streamed Text:", stream_text.strip())
    assert "1" in stream_text and "2" in stream_text and "3" in stream_text

    # 4. Multi-turn Agent Tool Calling
    print("\n4. Tool Calling via SDK:")
    tools = [
        {
            "type": "function",
            "function": {
                "name": "calculate",
                "description": "Calculate expression",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "expr": {"type": "string"}
                    },
                    "required": ["expr"]
                }
            }
        }
    ]

    # Turn 1: User asks, model calls tool
    resp1 = client.chat.completions.create(
        model="gemini-3.8-flash",
        messages=[{"role": "user", "content": "Calculate 123 * 456"}],
        tools=tools
    )
    msg1 = resp1.choices[0].message
    assert msg1.tool_calls is not None
    call = msg1.tool_calls[0]
    print(f"   Model requested tool call: {call.function.name}({call.function.arguments})")
    assert call.function.name == "calculate"

    # Turn 2: Provide tool result back to model
    resp2 = client.chat.completions.create(
        model="gemini-3.8-flash",
        messages=[
            {"role": "user", "content": "Calculate 123 * 456"},
            msg1,
            {"role": "tool", "tool_call_id": call.id, "content": "56088"}
        ],
        tools=tools
    )
    final_ans = resp2.choices[0].message.content.strip()
    print("   Agent final answer after tool return:", final_ans)
    assert "56088" in final_ans or "56,088" in final_ans

    print("\n=================================================================")
    print(" OFFICIAL OPENAI SDK TEST PASSED WITH 100% SUCCESS!")
    print("=================================================================")

if __name__ == "__main__":
    test_official_sdk()
