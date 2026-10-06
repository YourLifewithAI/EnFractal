# Live conversation with the companion: design direction

**Status:** direction adopted by the founder on 6 October 2026. Implementation starts in Run 2. This summarises a research pass done that day from public sources. Prices, latencies and model names are as of that date; several are vendor claims, marked [V]. Re-check them every run.

## Goal

The player and the companion AI play live together through natural conversation, with **no approval clicks**. The player moves and interacts with the keyboard; voice is only for talking to the companion. Accessibility for as many players as possible is a hard requirement. Speech must never be required.

## Architecture: three layers

| Layer | What it does | Target speed |
|---|---|---|
| **Reflex** | "Stop", "yes", "no", "undo", plus a key for each. Plain local code, no AI; it works even when everything else is busy. | ≤ 250 ms |
| **Talker** | Local speech-to-text, then a **fixed command grammar** (about 25 commands), then pre-recorded or templated replies. A typed box uses the same parser. | about 0.3–0.6 s to acknowledge (estimate, to be measured) |
| **Actor** | A capable model through the game's MCP tools. Used only for wishes the grammar doesn't cover and for multi-step plans. It acknowledges at once ("On it"), then acts. | 1–3 s to visible action |

**Rules that keep it honest:**
- **Speech states intent; outcomes come from receipts.** The companion may say "Getting the mug". "Here you go" plays only when the host's receipt says the action committed. Shipped companions that broke this rule (PUBG Ally's "Okay" without acting, Stellar Cafe, Project Sid) were the most common complaint.
- **Corrections preempt.** "No, the other one" sends `goal.stop` for the in-flight action id, excludes that target and resolves again. Idempotent action ids make the retry safe.
- **The model is never the authority on game state.** Only the host's ledger is.

## Safety without clicks: host-enforced tiers

These replace click approvals. The host enforces them per operation and target class. **Every protection must hold with no tier configured at all.**

| Tier | Examples | Behaviour |
|---|---|---|
| **T0 auto** | Queries, `observe`, follow, look, point, come, stay, `goal.stop`, `effect.stop` | Runs immediately |
| **T1 auto plus undo** | Fetch; grab, release and place of unlocked movable objects within limits; catalogue `creation.place` within budget; `protect.lock`; undo of the companion's own changes | Runs immediately; snapshot for undo |
| **T2 preview, then commit** | Transform, bounded cosmetic effects, `creation.revise`, `style.set` to an existing preset, removing companion-made creations | A ghost preview commits after about 3 s unless the player says stop. The countdown is adjustable, or set to "always ask". |
| **T3 spoken or keyed "yes"** | Deleting scanned or player-made objects, room-wide or paid restyles, effects that spread beyond what can be snapshotted, anything over budget | The host holds the command; the **game's own recognizer or a key** confirms it, bound to the request shown on screen |
| **Never exposed** | `protect.unlock`, undoing the player's edits, saves, settings, the companion's own perception, budgets or policy, destructive actions whose target came only from text in the world | Not on the companion surface |

**How the "yes" works:**
- The spoken "yes" is a player-input event, exactly like a click. The model never sees or emits it, so "no request carries an approval" stays true.
- It is accepted only during push-to-talk, or while the companion's own voice is muted, so the companion cannot confirm itself through the speakers.
- **Provenance rule:** the target of a destructive action must trace back to the player's words or pointing, never to a sign or name read in the world.

## The fixed command set (first draft)

| Group | Commands |
|---|---|
| Local, never sent to a model | stop or freeze, undo that, yes or no, what can you do? or help, say that again, number two or "the red one" (to pick from a list) |
| Movement | follow me, stay, come here, go to that, look at that, point at or show me X |
| Questions | what's that? (crosshair target), what do you see? (line of sight), where's the X? (only what the companion has perceived) |
| Objects | bring me that or fetch X, pick that up, put it down, put it on the shelf or put that there (ghost preview), lock this |
| Magic | move or turn it (T2), bigger or smaller (T2, capped), make it glow or add a breeze (T2, bounded), stop the glow, make a ramp or block from a small catalogue |
| Open wishes | From Run 3, through the actor: a previewed plan that commits as one undo step |

Notes on the command set:
- **There is no "unlock" command:** unlocking is player-only.
- "This", "that" and "there" resolve from the player's crosshair at the moment the word is spoken.
- On ambiguity the game shows numbered tags.
- After two misunderstandings, the game opens the command list and the text box.
- **The founder asked for a help tab or panel listing these commands.**
- Symbolic buttons for the same commands are the non-voice path.

## Speech on the founder's machine

The dev machine has an RTX 2070 SUPER (8 GB) and an Intel i7-9700K (8 cores). The card is below vendors' minimums for running AI beside a game (NVIDIA's in-game inference SDK requires RTX 30-series), so **speech runs on the CPU**.

| Job | Candidates | Notes |
|---|---|---|
| Speech-to-text | Parakeet TDT 0.6B v3 (ONNX int8, CC-BY-4.0, 25 European languages), Moonshine v2 (MIT for English), faster-whisper small | Published speeds come from newer CPUs; measure on the 9700K |
| Speech end and voice activity | Silero VAD (MIT), Pipecat Smart Turn (BSD-2) | Both cost almost nothing on the CPU |
| Text-to-speech | Kyutai Pocket TTS, Kokoro-82M (Apache-2.0), Supertonic | Pre-render the acknowledgement lines so they cost 0 ms. Piper is GPL-3.0 now; F5-TTS and XTTS-v2 weights are non-commercial. |

Microphone and accessibility:
- **Microphone mode:** toggle push-to-talk by default (the founder's decision). Open mic is opt-in, with a headset recommended for echo cancellation.
- **Speech-off fallback:** the keyboard, through the same commands.

## A local model inside the game (deferred)

**Superseded the same evening by the release decision below: bring your own AI.** No model ships with the game for now. This section stays as research for a possible later exploration. The fixed command grammar (the reflex and talker layers) still runs locally and needs no model.

**Fast layer: no model needed.** The fixed grammar does the work.

**Actor: plausible, to be measured.** The best small tool-caller found is **Qwen3-4B-Instruct-2507**:
- licence: Apache-2.0, so it can be bundled;
- size: about 2.5 GB at 4-bit;
- Berkeley Function Calling Leaderboard v4: 76.4 live accuracy, and 84.9 on correctly *not* calling a tool;
- llama.cpp (MIT) can be embedded. On this exact GPU it runs a 7B 4-bit model at about 88 tokens/s with the GPU idle.

**What stays unknown:**
- the frame-time cost beside the Forward+ room (the placeholder room used about 0.5 GB of video memory, so there is room on 8 GB);
- quality on loose wishes;
- players with weaker GPUs, where a CPU-only model is slow.

**Plan (deferred):** a local model may join a later actor bake-off as a comparison. It is not the default.

## Cost per hour of play

As of 6 October 2026:

| Setup | $ per hour |
|---|---|
| All local (speech plus fixed commands, optional local actor) | **$0** |
| Local speech plus a cheap hosted actor (nano or Flash-Lite class) | about $0.02–0.03 |
| Local speech plus Claude Haiku 4.5 | about $0.35 |
| Hosted speech-to-speech (Gemini Live, OpenAI Realtime) | about $0.30 up to $3 or more |

Budget and funding:
- **Development budget:** $10 a month for hosted actor tests, from Run 3 (approved).
- **Scaled costs:** speech never needs to cost per use because it runs locally. If any voice path ever does, the founder's fallback is symbolic UI buttons.

**Release model: bring your own AI (decided by the founder, 6 October 2026).** This answers who supplies and pays for the AI when the game ships. The options were:
1. a local model in the game;
2. the player brings their own AI;
3. the game bundles hosted AI.

The founder chose **(2), and widened it.** Players connect their own AI, and that includes their own agent harnesses (for example Hermes Agent, OpenClaw or a homebrew harness), not only an API key. The audience already uses AI well and wants a malleable sandbox where their AI can create and experiment. Multiplayer, with players and their AIs playing with and against each other, comes later.
- (1) is deferred. The founder judges small local models not yet good enough, and a local model may be explored later.
- (3) is out.
- Developer cost for the actor is zero. Players pay their own provider, which is about $0.02–0.35 an hour depending on the model (table above).

Consumer subscriptions such as Claude Pro or ChatGPT Plus cannot currently be used by third-party games through the vendors' APIs. A player's own harness may still be signed in to one; that is between the player and their provider.

**What BYOAI asks of the surface:**
- The MCP server stays strictly vendor-neutral and harness-neutral.
- Connecting must be easy: a documented launch command and token handoff for common harnesses, and the transports they expect.
- The security boundary assumes an arbitrary, possibly adversarial client: tiers, player-only ops, provenance, and no files, shell, URLs or credentials. That boundary matters more, not less, when the harness is unknown.

## Hosted voice, for comparison

- **Tool calling:** every major hosted speech-to-speech API supports it (OpenAI Realtime, Gemini Live, Azure Voice Live, Nova Sonic and others). Anthropic has no voice API.
- **Vendor "native MCP" can't reach the game:** in those APIs the vendor's cloud calls a public URL, and the game's server is loopback-only. Bridge instead: list the local tools, declare them as functions, and forward each call through `tools/call`.
- **Session limits:** OpenAI allows 60 min; Gemini Live about 10–15 min per connection.

## Accessibility checklist

- Speech is never required: a typed box with full parity, and every command bound to a key (so Windows Voice Access, Talon and VoiceAttack work).
- Push-to-talk is a toggle, never a hold.
- Captions are on by default and name the speaker. A "Heard: …" line shows what was recognised.
- The companion's state is visible: listening, thinking, acting, waiting for your yes.
- The vocabulary is small and forgiving. Risky actions never fire from free-form transcription alone (speech-to-text sometimes invents words).
- The preview countdown is adjustable.
- **Privacy:** no stored audio, no voiceprints, local by default. Children's voice recordings fall under COPPA.

## Experiments that settle the unknowns

| Unknown | Experiment | When |
|---|---|---|
| Frame time and memory with speech and a local model running | p50/p99 frame times over 5 minutes, with speech on and off, plus a 4B model on the GPU | Run 2 |
| Recognition of object names and atypical speech | 200 utterances × at least 5 speakers; fixed grammar vs free transcription | Run 2 |
| Saying vs doing | An automatic check that every completion line has a committed receipt | Run 2 |
| Stop reliability over game audio | 100 stops on speakers and on a headset; p95 ≤ 250 ms | Run 2 |
| Self-confirmation and in-world injection | The companion saying "yes" through speakers; signs and names aimed at destructive actions | Run 3 |
| Does preview-then-commit feel better than clicks? | A/B playtest; tune the countdown | Run 3 |
| Actor quality, latency and cost | 50 fixed wishes across 3–4 models, including the local 4B | Run 3 |

## Plan by run

1. **Run 2, voice v0 with no model in the loop.**
   - Speech-to-text bake-off on the CPU.
   - Keyword path for stop, yes, no and undo.
   - The grammar for the core commands, the text box, captions, the help panel and visible companion states.
   - Exit when "fetch the mug" works by voice and by text.
2. **Run 3.**
   - The actor is the player's own AI, through the MCP surface and thin adapters for common harnesses. Make connecting a harness a short, documented step.
   - The tiers replace click approvals; red-team in-world injection.
   - Exit when a loose wish completes with zero clicks.
3. **Run 4.** Scenarios: dragon (T2 when contained, T3 otherwise), spaceport restyle (T3 with a preview), interrupting plans mid-way, and a talk budget.
4. **Run 5.** Accessibility playtests (non-native speakers, stutter or dysarthria, deaf and hard-of-hearing players, Voice Access users), minimum-spec and offline tests, a model licence review, and release.
