# Claude API setup

1. Create an account at https://console.anthropic.com, add billing, and create an API key (Settings → API keys).
2. Set on the server (`deploy/.env`): `AI__Claude__ApiKey=sk-ant-...`
3. Optional tuning:
   - `AI__Claude__Model` — default `claude-opus-5-5` (best quality; $4 / $20 per million input/output tokens). For lower cost you can set `claude-sonnet-5-5` ($2/$10) or `claude-haiku-5-5` ($0.10/$0.50). Update `AI__Claude__Pricing__<model>__InputPerMTok/OutputPerMTok` if prices change.
   - `AI__Claude__Effort` — `low` | `medium` (default) | `high`. Lower effort = fewer tokens.
   - `AI__Claude__EnableFallbacks` — server-side refusal fallbacks (beta), on by default.
4. Restart: `docker compose up -d api worker`. Settings → Integrations should show the AI as *Ready*.

How it's used: the official Anthropic C# SDK with **structured outputs** (JSON schema) for lead analysis and pitches. Every call is logged in `UsageLogs` with tokens and estimated cost; budgets (Settings → Budgets) pause AI automatically when reached. Prompts are versioned (`lead-analysis/v1.0`, `pitch/v1.0`) and stored with each result.

Typical cost: one lead analysis ≈ 3–6k input + 1–2k output tokens → roughly $0.02–0.06 with Opus 5.5, ~$0.01–0.03 with Sonnet 5.5.
