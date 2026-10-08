# Brief 04: how players' own AI harnesses connect to a local MCP server

**Where:** a Codex cloud task, or any session with web access. This is research only.
**Branch:** `codex/04-byoai-connect-survey`.

**Context.** EnFractal's release model is "bring your own AI": players connect their own AI or agent harness to the game's local stdio MCP server. Read `docs/companion/README.md` first (the server, the play-only profile, and why a play session should expose no shell or file tools). The goal is to make connecting as easy as possible for arbitrary harnesses.

**For each of at least eight harnesses and clients** (include Claude Code, Codex CLI, Cursor, VS Code with Copilot, LM Studio, an Ollama-based client, Hermes Agent and OpenClaw; add others that are popular in October 2026):
- whether it supports local stdio MCP servers, and which protocol versions;
- the exact configuration format and where the file lives on Windows, with a link to its docs;
- whether a session can be limited to one MCP server, with no shell or file tools, and how;
- support for MCP resources and prompts;
- known pitfalls (paths with spaces, Windows `.exe` resolution, timeouts).

Finish with a proposal of one page or less: what the game could ship to make connecting a one-step job, such as a "copy config" button per harness, or a generated file.

```scope
docs/codex/reports/04-byoai-connect-survey.md
```
