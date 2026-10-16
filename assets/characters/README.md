# The family's characters

The five characters the founder and their kids drew, and what the drawing-to-character converter made of them: the Gubble, Potato Man, Tomato Man, Cloudpuff and Stickbear (the brown-and-red creature with horns and bear-claw paws; working name). The founder approved committing all of it, drawings and renders included, on 13 October 2026, knowing this repository is public.

- `drawings/`: the two photos of the drawings the converter read (no location or camera metadata).
- One folder per character, from round 2 of the converter (`pipeline/characters/conv_a`), after the family's blind vote of 9 October (it won all five) and their notes (Cloudpuff puffier, Potato Man wider and rounder, Tomato Man a real tomato with a calyx):
  - `character.glb`: the model, in metres, +Y up, facing -Z, pivot at the bottom centre;
  - `character.json`: its parts, colours, material and motion hints, a collision hint, and its provenance as hashes;
  - `interpretation.json`: the converter's reading of the drawing (outlines, parts, colours);
  - `front.png`, `side.png`, `back.png`, `three_quarter.png`, `turntable.png`, `sheet.png`: renders from the converter's shared turntable.

**The Gubble, round 3 (13 October):** at the founder's word the torch is no longer part of the Gubble. Its interpretation drops the `torch` and `torch_flame` parts (an `applied_notes` entry says so); the arm and hand stay, reaching out to hold what it carries. The tallest point is now a floating sparkle, so the whole Gubble scales up to stay 0.10 m tall (the bubble about 8.3 cm, from 6.7). Rebuilt with the converter's own `build` from the same photo's provenance and a new description (`C:\dev\EnFractal-art\characters\round3\gubble-description.txt`, outside Git; the round-2 description is on DiamondAge); its renders and `sheet.png` (round 2 against round 3) are new. The torch is now a prop of its own: [../props/torch/](../props/torch/).

**In the game since the characters round** ([docs/runs/RUN-2-CHARACTERS.md](../../docs/runs/RUN-2-CHARACTERS.md)); the game reads its copies in `game/characters/` (`python tools/characters/sync_characters.py`). Originally: The character item in [docs/runs/RUN-2-STATUS.md](../../docs/runs/RUN-2-STATUS.md): a character choice per player, bouncy motion from the motion hints, one shared collision body, and the soap-bubble look for the Gubble from its `bubble` material hint.
