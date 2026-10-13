# Growth and island magic playtest

Everything for this build is on `codex/gubble-growth-runtime` in `C:\dev\EnFractal-codex\gubble-growth-runtime`.

From PowerShell 7 in that directory, run:

```powershell
.\run-room.ps1 -Profile growth -Room landscape
```

This rebuilds the game and launches the generated garage landscape. `-Profile growth` keeps this playtest's saves, wishes and companion session in `.cache/profiles/growth`, apart from your own; delete that folder to start fresh. `-Room landscape` picks the newest landscape `tools/test-room.ps1` prepared (derived data, not committed); `-Room <id or path>` selects another room.

Open **Magic** for the Island, Glow and Wishes pages. **Choose spot** closes the panel for one click, then returns to it. Existing `1`/Glow, `2`/Bloom and the right-button wheel still work; the help shows remapped keys. Fire and wishes use their explicit panel controls.

1. **Glow.** Cast a Lantern on the Gubble; try an elevated tree/building/hill for Torch, water for Bioluminesce, three distinct nearby ground lights for a constellation, and Skylight at a spot with open sky. Clear lights between experiments when you reach the active limit. The journal records real accepted discoveries. Two distinct discoveries allow four lights; all five allow five and unlock moving points and editing links.
2. **Touches.** Try Plain, Air, Ember, Frost and Chime. Air/Ember/Frost are expressive effects; their looks do not apply force, heat or cooling. Try volume and mute. Start Bubbles, then choose Glowing bubbles to light the live particles; individual bubble grabbing is a later feature.
3. **Modes.** Compare Creative, Challenge and Live in the Island page. Challenge and Live have separate saved traits and Glow progress. Climb a tree, rest on the ground, then try a taller cliff. Observe stamina, capacity rewards for distinct qualifying ledges, and fall damage. Injury recovery returns you to spawn and preserves your creations and earned traits.
4. **Bloom and weather.** Cast on grass/soil, try an unsuitable surface, and toggle the Gubble's flower trail. Weather cycles through fair weather, showers and rain over six minutes. Light and moisture affect growth; rain and wind affect Fire. Player weather control is a later ability.
5. **Fire.** Choose dry fuel, press Preview and inspect its outlined area. Hold the confirmation button; release early to cancel. In Creative, a fire that would reach an unlocked creation asks once on the approval card: Approve lets it burn (repair is free), Deny lights the fire and spares the build. In Challenge/Live nothing asks: your fire and the Gubble's burn your builds, and stepping into either hurts. The Gubble waits a moment between fires; "Let the Gubble use fire" (Fire section) turns its fire off, and X puts its fire out at once. Locks remain fireproof. Compare a wet spell with a dry one.
6. **Repair and undo.** Pick a scorched creation, inspect its material quote and shared supply, then repair it. Scorched creations retain their design and stop their triggers until repaired. Other flammable room objects can burn away. In Creative only, Undo reverses the most recent nature event, including its burn and repair costs; a newer ordinary room edit prevents undo across that edit, and so does anything that happened in Challenge or Live. Challenge and Live have no undo control. Undo does not erase injury.
7. **Wishes.** Make two wishes and watch their order, then Stop. Without a runner they stay queued. The game explains how to connect one before the first wish; it never starts another program. The page always says that the words of a wish are sent to the AI you connected, and to the company that runs it when it is online. A wish is granted only after the game has seen every action it started finish: while the Gubble is still walking to fetch something the wish shows *Working on it*, and a fetch that fails afterwards leaves the wish not granted. For a complete test without an AI account or provider charge, open a second PowerShell terminal in this checkout and run:

   ```powershell
   .\start-growth-wishes.ps1 -Demo
   ```

   Submit **Make a light**. This deterministic demo supports light/Glow/Lantern requests, and marks other requests unsupported. It tests the real authenticated runner path; it is not an AI. Use `-Client <adapter.json>` for your own configured AI adapter. See [the runner setup guide](../../companion/WISH-RUNNER.md).

All balance numbers are provisional. Initial climbing capacity is 0.45 m; distinct climbs of at least 0.25 m add 0.10 m capacity, up to 3 m. Ground rest restores stamina. Safe rest heals after ten seconds. Zero health returns you to spawn with half health and stamina. The first material convention is one unit per original authored part, with a saved quote for each material; the initial shared repair supply is wood 20, stone 10, copper 10 and leaf 20. A broader resource economy, critter combat, player combat and multiplayer are later work.

Headless tests verify behavior; your playtest judges the look, sound, clarity, frame rate and balance. Normal reload restores the nature sidecar. Export includes nature data, but aggregate room-state import explicitly refuses it until a transaction can restore both stores together.
