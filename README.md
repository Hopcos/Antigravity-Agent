# GeminiAgent 🚀

> **基于 Antigravity 账号的 OpenAI 兼容代理网关与双 Token 永续系统**

GeminiAgent 是一个专为 AI Agent（Cursor、Dify、LangChain、AutoGPT、Continue 等）打造的高性能网关。它通过深度逆向与适配 Google 官方 Antigravity 运行环境，无缝提取当前宿主机上已登录的账号凭据，将其转化为完全符合 **OpenAI 官方规范** 的 API 接口（`/v1/chat/completions`、`/v1/models`、`/v1/health`）。

---

## 🌟 核心特性与技术突破

### 1. 🔄 双 Token 机制与自动永续保活 (Perpetual Token Lifecycle)
- **无感凭据读取**：通过 Windows 底层 Win32 API（P/Invoke `advapi32.dll` 的 `CredReadW`）直接安全读取 Windows 凭据管理器中的 `gemini:antigravity`，完全无需用户重新登录或手动抓包。
- **OAuth 2.0 双 Token 自动续期**：提取凭据中的 `refresh_token`，配合 Antigravity 官方客户端凭据，定期向 `https://oauth2.googleapis.com/token` 刷新获取最新的 `access_token`（具备 `https://www.googleapis.com/auth/aicode` 核心权限）。
- **后台主动轮询保活 (`TokenRefresherBackgroundService`)**：每 60 秒定期巡检，当 Token 剩余有效时长少于 5 分钟或总时长的 10% 时，后台自动提前无缝刷新。
- **401 自愈重试**：当上游返回鉴权失败时，网关原子化使当前 Token 失效并强制重新刷新重试，实现真正意义上的**7x24小时永续运行**。

### 2. ⚡ Antigravity 专属通道与后端优化
- **专用内部端点**：直连 Google 内部通道 `https://daily-cloudcode-pa.googleapis.com/v1internal`，指定 `aicode-consumers` 专属工程与 `antigravity/2.24.0` 客户端标识，避开常规 Cloud Code 订阅限制与 403 阻断。
- **全双工流式与聚合**：由于上游限制仅开放 `:streamGenerateContent?alt=sse`，GeminiAgent 内部实现了高吞吐 SSE 解析器。当客户端请求 `stream=false` 时，网关在内存中以纳秒级高效聚合分块并输出标准 OpenAI 响应；当客户端请求 `stream=true` 时，零延迟透传 SSE 数据。

### 3. 🧠 Gemini 3.x 深度思考与多轮工具调用 (Thought Signature) 破局
- **思考标记 (Thought Signature) 完美适配**：Gemini 3.x（如 `gemini-3.8-flash-high`）在 Tool Calling 中引入了思维链签名校验机制，下游在回传函数结果时若缺少该签名上游将直接报 400 错误。
- **无状态 ID 编码创新**：GeminiAgent 将 `(tool_name, thoughtSignature)` 进行 Base64Url 打包编码进标准的 OpenAI `tool_call_id`（`call_<encoded>`）。标准 Agent 无需做任何魔改即可直接原样传回，网关无状态解码还原，彻底解决多轮函数调用的断流痛点。
- **思考 Token 统计**：将 Gemini 的 `thoughtsTokenCount` 转化为 OpenAI 规范的 `usage.completion_tokens_details.reasoning_tokens`，精准支持思考过程审计。

### 4. 🔀 丰富模型生态与智能别名路由
自动探查并支持账户名下的 24 个前沿模型，并内置常用别名映射：
- `gemini-3.8-flash-high`（默认推荐，性能与智能平衡巅峰）
- `gemini-3.8-flash`、`gemini-3.8-flash-medium`、`gemini-3.8-flash-low`
- `gemini-2.5-pro`、`gemini-2.5-flash`、`gemini-2.5-flash-thinking`
- `claude-sonnet-4-6`、`claude-opus-4-6-thinking`
- 常用别名映射：`gpt-4o` 自动路由至 `gemini-3.8-flash-high`

---

## 🏗️ 架构全景

```
+-------------------------------------------------------+
|  外部 Agent 客户端 (LangChain, Dify, Cursor, SDK)    |
+-------------------------------------------------------+
                           │  HTTP POST /v1/chat/completions
                           ▼
┌───────────────────────────────────────────────────────┐
│               GeminiAgent (ASP.NET Core)              │
│                                                       │
│ ┌─────────────────────┐       ┌─────────────────────┐ │
│ │  OpenAI 协议解析器  │       │  模型注册与别名路由 │ │
│ └──────────┬──────────┘       └──────────┬──────────┘ │
│            ▼                             │            │
│ ┌────────────────────────────────────────▼──────────┐ │
│ │             GeminiRequestBuilder                 │ │
│ │  - Schema 规范化与清理                             │ │
│ │  - 消息多轮序列重构                               │ │
│ │  - ThoughtSignature 解码还原                     │ │
│ └──────────────────────┬────────────────────────────┘ │
│                        ▼                              │
│ ┌───────────────────────────────────────────────────┐ │
│ │             AntigravityAccountService             │ │
│ │  - Windows 凭据管理器 P/Invoke (gemini:antigravity)│ │
│ │  - OAuth 2.0 双 Token 刷新 (googleapis.com/token) │ │
│ │  - 60s 后台主动巡检保活 (TokenRefresherService)   │ │
│ └──────────────────────┬────────────────────────────┘ │
│                        │ Authorization: Bearer Token  │
└────────────────────────┼──────────────────────────────┘
                         ▼
+-------------------------------------------------------+
| Google 内部后端 (daily-cloudcode-pa.googleapis.com)    |
| Project: aicode-consumers                             |
+-------------------------------------------------------+
```

---

## 🚀 快速启动

### 准备工作
- **操作系统**：Windows 10/11（已登录 Antigravity 账号）或 Linux/macOS（通过环境变量配置 Token）
- **运行环境**：[.NET 8.0 或 .NET 10.0 SDK](https://dotnet.microsoft.com/download)
- **可选测试**：Python 3.10+ 及 `pip install openai requests`

### 1. 一键启动网关
在 PowerShell 中执行：
```powershell
.\scripts\start_gateway.ps1 -Port 5055
```
服务将在 `http://127.0.0.1:5055` 启动，并在控制台打印账号发现状态：
```text
Discovered Antigravity account from Windows Credential Manager [gemini:antigravity]: user@gmail.com
Antigravity token successfully refreshed for user@gmail.com (valid ~3600s)
Now listening on: http://127.0.0.1:5055
```

### 2. 一键执行完整闭环测试
在新终端中执行内置的端到端自动化测试验证套件：
```powershell
.\scripts\verify_all.ps1
```
测试脚本将依次验证：
1. **健康检查 (`/v1/health`)**：校验账号探测、Token 剩余寿命与可用模型状态。
2. **模型发现 (`/v1/models`)**：验证全部 24 个 upstream 模型的 OpenAI 格式列举。
3. **非流式问答 (`stream=false`)**：验证多轮对话与思考 Token 统计。
4. **实时流式生成 (`stream=true`)**：验证 SSE 数据流分块与 `[DONE]` 终止符。
5. **多轮函数调用 (Tool Calling)**：验证参数推导、签名编码与最终结果回传。
6. **官方 OpenAI SDK 兼容性**：验证 `openai.OpenAI(base_url="...", api_key="...")` 零代码变动对接。

---

## 💻 客户端接入示例

### 1. 官方 Python OpenAI SDK
```python
from openai import OpenAI

client = OpenAI(
    base_url="http://127.0.0.1:5055/v1",
    api_key="sk-antigravity"  # 若网关未配置 APIKey 可填任意值
)

# 1. 普通对话
response = client.chat.completions.create(
    model="gemini-3.8-flash-high",
    messages=[
        {"role": "user", "content": "9.11 和 9.9 谁更大？请用一句话回答。"}
    ]
)
print("回答:", response.choices[0].message.content)

# 2. 流式对话
stream = client.chat.completions.create(
    model="gemini-3.8-flash-high",
    messages=[{"role": "user", "content": "请数到 3"}],
    stream=True
)
for chunk in stream:
    if chunk.choices[0].delta.content:
        print(chunk.choices[0].delta.content, end="", flush=True)
print()
```

### 2. LangChain 接入
```python
from langchain_openai import ChatOpenAI

llm = ChatOpenAI(
    base_url="http://127.0.0.1:5055/v1",
    api_key="sk-antigravity",
    model="gemini-3.8-flash-high",
    temperature=0.2
)

response = llm.invoke("介绍一下量子计算的基本原理。")
print(response.content)
```

### 3. Cursor / Continue / Cline 配置
在开发工具的 OpenAI Compatible 模式中填入：
- **Base URL**: `http://127.0.0.1:5055/v1`
- **API Key**: `sk-antigravity`
- **Model Name**: `gemini-3.8-flash-high` 或 `gemini-2.5-pro`

### 4. cURL 测试
```bash
curl http://127.0.0.1:5055/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemini-3.8-flash-high",
    "messages": [{"role": "user", "content": "你好，请确认你的服务状态"}],
    "temperature": 0.3
  }'
```

---

## ⚙️ 高级配置与部署

配置文件位于 `src/GeminiAgent/appsettings.json`，支持通过环境变量直接覆盖：

| 环境变量 | 默认值 | 说明 |
| :--- | :--- | :--- |
| `GATEWAY_API_KEY` | *(空)* | 网关对外认证密钥，为空则允许任意请求访问 |
| `GEMINI_DEFAULT_MODEL` | `gemini-3.8-flash-high` | 未指定模型时的默认转发目标 |
| `ANTIGRAVITY_AUTH_SOURCE` | `Auto` | 认证源策略：`Auto`（凭据管理器优先）、`File`、`Env` |
| `ANTIGRAVITY_CREDENTIAL_TARGET` | `gemini:antigravity` | Windows 凭据管理器目标名称 |
| `ANTIGRAVITY_REFRESH_TOKEN` | *(空)* | 手动指定 Refresh Token（用于 Linux/Docker 部署） |
| `ANTIGRAVITY_MAX_CONCURRENCY` | `2` | 向上游发起请求的最大并发数 |

### Docker 容器化部署
支持跨机器部署（通过传入主机提取的 `refresh_token`）：
```bash
docker compose up -d
```

---

## 🧪 实机验证报告 (Verification Summary)

当前项目已在宿主机的真实 Antigravity 环境下通过严谨的闭环验证：

| 测试项 | 验证指标 | 实测结果 | 状态 |
| :--- | :--- | :--- | :--- |
| **凭据发现** | Windows Credential Manager P/Invoke | 成功发现 `pingcunhe@gmail.com` | ✅ 通过 |
| **双 Token 续期** | Google OAuth2 刷新机制 | 成功换取包含 `aicode` 作用域的 Access Token | ✅ 通过 |
| **后台保活** | 60s 轮询与主动提前刷新 | 预留 300s 阈值自动平滑续期 | ✅ 通过 |
| **模型列举** | `GET /v1/models` | 成功列出全部 24 个模型 | ✅ 通过 |
| **非流式问答** | `POST /v1/chat/completions` (stream=false) | 正确返回答案，reasoning_tokens 统计准确 | ✅ 通过 |
| **实时流式** | `POST /v1/chat/completions` (stream=true) | SSE 纳秒级响应，正常接收 `[DONE]` 终止符 | ✅ 通过 |
| **多轮 Tool Calling** | 函数推导与 Thought Signature 保留 | 模型成功调用 `calculate`，回传后输出 56,088 | ✅ 通过 |
| **官方 SDK** | `openai.OpenAI` Python SDK | 100% 协议兼容，无额外侵入性依赖 | ✅ 通过 |

---

## 📄 开源许可证
本项目基于 MIT License 许可协议开源。
