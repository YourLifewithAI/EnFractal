# ADR 0002: isolated Codex companion connection

**Status: proposed; provider choice accepted, connection boundary not yet validated. 2 October 2026.** The founder chose Codex through a dedicated game-only profile. This record establishes the integration direction and the evidence still needed before login or inference. It does not certify that a Codex configuration file alone removes every non-game capability.

## Proposed integration

Use a separately launched **Codex app-server child over private stdio**, controlled by a narrow EnFractal adapter. Keep its conversation, configuration, provider authentication and game grants separate from the developer's desktop Codex session. Use one explicitly configured EnFractal MCP server, with named game operations allowlisted. The model proposes intentions; the existing game authority authenticates the actor, checks protection/revisions/budgets and applies supported effects.

App-server offers managed login, streaming and conversation control suitable for this embedded interaction. Its protocol is not MCP: app-server is the provider-facing conversation transport, while EnFractal MCP supplies the game operations. A plain `codex exec` process is a possible diagnostic harness, but its CLI switches do not prove a game-only tool boundary. The removed `codex mcp-server` interface must not be used. [Official app-server documentation](https://learn.chatgpt.com/docs/app-server), [MCP server removal](https://learn.chatgpt.com/docs/mcp-server).

Only the trusted adapter can send app-server protocol messages. Player text and model output must never become arbitrary protocol requests, configuration overrides, paths, commands, URLs or tool registrations. In particular, the app-server protocol includes host-facing process/filesystem functions beyond conversation control; the adapter must not forward them. Client-side handling of approval events is not sufficient proof that every possible action was intercepted.

## Local evidence

Inspected binary: `codex-cli 0.155.0-alpha.16`, installed at `C:/Users/blues/AppData/Local/OpenAI/Codex/bin/d375f7df50d3b421/codex.exe` on the current development machine. SHA-256: `97d4d67419d0ac2f71342f9a5e850f9468aa622618de8ea823223edb9a91926a`. Do not hardcode that machine path into the game. Resolve and pin a tested installation/version as part of the companion adapter's compatibility gate.

Read-only CLI inspection established:

- `codex exec --help` describes `--profile` as layering `$CODEX_HOME/<name>.config.toml` over base user configuration. A named profile alone is therefore not isolation.
- `exec` offers `--ignore-user-config`, but its help explicitly says authentication still uses `CODEX_HOME`. `--ephemeral` addresses session persistence, not tool privileges.
- `app-server --help` supports `--listen stdio://` and `--strict-config`. Its schema generator runs without a login or model turn.
- Generated experimental `ThreadStartParams` contains `config`, `baseInstructions`, `developerInstructions`, `dynamicTools`, `selectedCapabilityRoots` and `ephemeral`. No global built-in-tool allowlist was found in that request schema. `dynamicTools` adds capabilities; it must not be treated as replacing the built-in catalogue.
- The generated configuration response's `ToolsV2` schema exposes web-search configuration, not a general tool allowlist. An extensible JSON `config` field is not evidence that an invented setting is supported.

Schema generation and feature inspection used a fresh scratch `CODEX_HOME` and a child-process environment allowlist. Evidence remains in the ignored `.cache/codex-game-profile-audit/` directory. No authentication files were inspected, credentials copied, login flow started, or inference requested. No existing Codex configuration was changed.

## Verified settings and limits

These names are documented configuration surfaces, **not a completed security profile**. Their runtime composition still requires the acceptance checks below. [Configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference).

| Setting | Intended role | Limit |
|---|---|---|
| `cli_auth_credentials_store = "ephemeral"` | Initial process-only fresh login | Does not restrict the account token to game operations |
| `forced_login_method = "chatgpt"` | Select the founder's Codex/ChatGPT login route | Not proof of model entitlement or a completed login |
| `approval_policy = "never"` | Prevent escalation prompts in the game process | Does not prohibit already allowed actions |
| `sandbox_mode = "read-only"` | Additional command sandbox restriction | Read-only still permits reads; not a no-files boundary |
| `web_search = "disabled"` | Remove web-search capability | Does not control every process/MCP/provider network path |
| `project_doc_max_bytes = 0` | Candidate suppression of project instruction loading | Must check actual instruction sources; does not disable skill discovery |
| `features.shell_tool = false` | Disable the default shell tool | Must also inspect other execution and file tools |
| `features.apps`, `features.hooks`, `features.multi_agent`, `features.remote_plugin`, `features.memories` set to `false` | Reduce unrelated runtime capabilities | Does not establish a complete tool inventory |
| `features.skill_mcp_dependency_install = false` | Prevent automatic skill dependency installation | Does not disable skills themselves |
| `tools.view_image = false` | Disable local-image attachment tool | Verify actual exposure in the pinned runtime |
| `mcp_servers.enfractal.enabled_tools` | Exact game-server tool allowlist | Does not filter Codex built-ins or other servers |

A local candidate configuration using the documented switches was parsed by `codex features list` with exit code 0. That output reported `apps`, `shell_tool`, `hooks`, `multi_agent`, `remote_plugin` and `memories` as false. It still reported `code_mode_host`, `plugins`, `skill_search`, `view_image` and `unified_exec` as true; notably `unified_exec` remained true despite setting `features.unified_exec = false`. A feature flag listing and a tool-specific override are different evidence, so this does not prove that all those tools were actually exposed. It does prove that the candidate cannot be accepted by merely inspecting its text.

`codex --strict-config features list` returned an explicit error that strict configuration is unsupported for that command. Do not report that as successful validation. Local feature metadata also contains `skip_host_skill_discovery`, but marks it under development; its name is not sufficient evidence of an exhaustive no-skills boundary. The removed `apply_patch_freeform` flag is not an approved way to disable all file editing.

MCP supports both stdio and Streamable HTTP, with an exact per-server allowlist and `required = true` to fail startup when a required enabled server cannot initialize. Prefer a private game-owned stdio adapter initially; never point it at a general-purpose filesystem, browser or developer MCP. Freeze the actual tool names after the game server contract exists rather than inventing them here. [Official MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

## State and fresh authentication

Give only the child process a dedicated, pre-created `CODEX_HOME` under the game's private local application-data area, outside the repository. Do not globally change the developer's environment or reuse/copy the desktop home, sessions, plugins or auth file. Explicitly prevent inherited `CODEX_SQLITE_HOME`, provider token/key variables and other credential-bearing environment values. The documented home controls Codex state locations; it is not an operating-system sandbox. [Environment variables](https://learn.chatgpt.com/docs/config-file/environment-variables).

Launch from a neutral game-owned working directory with no developer repository ancestors. Codex skills can also originate in user, repository, administrator and bundled locations. Therefore a fresh `CODEX_HOME` alone does not prove that the model sees only game instructions. Per-skill disabling exists, but a complete empty-inventory assertion must include all discovery locations. Do not modify or delete the player's existing skills to achieve this. [Official skill discovery documentation](https://learn.chatgpt.com/docs/build-skills).

After isolation is verified, the proposed first login is managed app-server `account/login/start` with `type = "chatgptDeviceCode"` or `type = "chatgpt"`. Show the provider-issued URL/code through trusted UI, let the player authenticate, and wait for `account/login/completed`. Keep the initial credentials in the isolated process using the ephemeral store. Never read the developer's existing credentials or ask them to paste a token into the game conversation. A later persistence choice needs an isolated private credential-store design, not an assumption about shared keyring namespaces. [Official app-server authentication](https://learn.chatgpt.com/docs/app-server).

“Game-only” describes the game's tool/grant boundary, not a verified OAuth scope. The login and account's model access must be tested separately. External-token mode is not permission to harvest credentials from another Codex installation. Public distribution or a custom sign-in flow needs its own provider-supported onboarding review; this ADR is a local prototype integration decision.

## Acceptance before real connection

1. **Catalogue and instruction proof:** Pin the CLI build, inspect effective config layers and loaded instruction sources, and assert the actual model-visible tool inventory contains only approved game operations. Verify no shell, patch, file/image reader, browser/computer use, plugins/apps, skill discovery/installation, subagents or alternate executor path remains. Treat any unknown tool as a failed startup gate.
2. **Process boundary:** Demonstrate that a non-game tool attempt cannot read/write the host, spawn a process or reach an unrelated endpoint. Provider login/inference and the private game transport are the only intended network uses. Codex's sandbox proxy does not cover every MCP/app/hosted-tool path; enforce any required broader restriction at the process boundary and test it. Keep authentication material outside model-visible observations.
3. **Trusted adapter:** Permit only the minimal authentication/conversation methods from fixed host code. Validate every game call against a fixed schema, inject actor/grant identity outside the model, and reject stale revisions, protected targets and oversized requests. Deny unknown messages rather than sending them to a general executor.
4. **Cancellation and failure:** Stop must interrupt the turn, revoke its game grant and reject late results. Provider outage, malformed output and refusal leave the world coherent. Manual controls remain available, but their success does not count as validation of the central AI experience.
5. **Real experience evidence:** Only after the previous gates pass, perform a fresh player-controlled login and a real interpretation-to-game-operation turn. Record the supported operation, model/provider version, resulting game feedback and protected-target rejection. A deterministic fixture, model listing or successful login alone is not this evidence.

No runnable “secure game-only” template is shipped with this ADR because the complete built-in/skills exclusion has not yet been demonstrated. Continue building the game authority/MCP contracts and provider adapter behind a disabled connection gate. If the pinned Codex runtime cannot satisfy the exact boundary, evaluate a supported inference harness that registers only game functions, or a suitably isolated runtime with demonstrable denial of every extra capability. Such a change must preserve the founder's provider/account choice explicitly rather than silently switching to API billing.
