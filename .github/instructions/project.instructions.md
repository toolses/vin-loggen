---
applyTo: "**"
---

# VinLoggen – Copilot Instructions

Refer to [INSTRUCTIONS.md](../../INSTRUCTIONS.md) at the repo root for the full project guide (tech stack, structure, conventions, env vars, and local dev setup).

## Key rules

- Follow all API and frontend conventions defined in INSTRUCTIONS.md.
- Do not add NgModules, `zone.js`, or class-based Angular patterns – the app is fully Zoneless with Signals.
- Do not use raw `HttpClient` in components; route calls through services in `src/app/services/`.
- New API endpoints must follow the `MapXxxEndpoints` extension-method pattern and be registered in `Program.cs`.
- Use `TypedResults` and return `ProblemHttpResult` on errors.
- Never expose secrets or connection strings in code or comments.
