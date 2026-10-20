# Explicit conversation and tool turns

`ILlmConversationClient` is the conversation-shaped provider boundary in
`Dominatus.Llm.OptFlow`. `OpenAiConversationClient` implements Responses API
function calling. `AnthropicConversationClient` implements Claude Messages and
`GeminiConversationClient` implements Gemini generateContent tool turns. These do
not replace `ILlmClient`, `Llm.Call`, decision clients
or their existing cassette formats.

The host supplies instructions, explicit input items and function schemas. A turn
returns assistant text, typed function requests, usage and the original provider
output items. The host executes admitted tools and appends `function_call_output`
items with the matching call IDs. It must retain opaque reasoning items when
continuing the conversation. The adapter requests encrypted reasoning content and
uses `store: false`; no provider conversation ID is used as hidden runtime memory.

Claude and Gemini return a `provider_message` envelope containing the provider,
model, complete native assistant message and normalized call IDs. Return that
envelope unchanged in subsequent `Input`, followed by the same
`function_call_output` items the host uses for OpenAI. The adapters translate only
user text and tool results; Claude thinking/redacted-thinking signatures and Gemini
thought signatures remain in the original native messages. All results from a
parallel tool turn are grouped into one following user message. Missing/foreign
results reject locally. When Gemini omits a native call ID, the adapter assigns a
host correlation ID in the envelope without altering the signed native part.
Start a fresh transcript when changing provider or model. Do not send opaque
reasoning from one provider/model to another.

The native adapters admit `low`, `medium` and `high` effort (default `medium`),
mapped to Claude adaptive thinking/output effort and Gemini thinking level.
Other effort spellings reject rather than silently changing the requested policy.
They retain the request's output-token cap. Incomplete, blocked or truncated turns
never expose executable calls. These adapters currently support text and registered
client functions, not images, provider-hosted tools or streamed responses.

Incomplete responses, unknown function names, duplicate call IDs and malformed
argument objects reject before any host effect. Transport and HTTP errors do not
echo request bodies, credentials or raw provider errors. `HttpClientLlmTransport`
accepts an optional host-owned HttpClient for timeout policy and test injection.

The adapter does not grant tool authority. Project revisions, filesystem scope,
compiler legality, checkpoints, user approval and durable task orchestration stay
with their owning runtime. A host can assemble instructions using
`Dominatus.Llm.Context` packets; packet omissions must be handled explicitly.
Agent freedom to choose a strategy does not require hidden memory or unrestricted
host capabilities. A series of explicit model/tool turns can perform substantial
engineering work without reducing every request to a closed option choice.

The first consumer is Helios through Leviathan. Its current model choices are
`gpt-6-astra`, `gpt-6.1-sol`, `gpt-6-luna`, `claude-opus-5-5`,
`claude-sonnet-5-5`, `claude-haiku-5-5`, `gemini-4-argon` and `gemini-3.8-flash`.
Leviathan owns that product catalog; the generic adapters accept explicit model
and endpoint options. Model access must be established
using actual account credentials; protocol fixtures do not establish live access.
The initial conversation adapter is nonstreaming. Durable conversation cassettes,
task resume and provider fallback are not implemented by this contract.

References: [function calling](https://developers.openai.com/api/docs/guides/function-calling),
[conversation state](https://developers.openai.com/api/docs/guides/conversation-state),
[Claude tool results](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls),
[Gemini function calling](https://ai.google.dev/gemini-api/docs/function-calling), and
[Gemini thought signatures](https://ai.google.dev/gemini-api/docs/generate-content/thought-signatures).

Qualification: run `tests/Dominatus.Llm.OptFlow.Tests` on net8.0 and net10.0.
