# Reading a drawing (instructions for the AI step)

You are helping a child's drawing become a 3D character they can play as in EnFractal (a 10 cm avatar in a game
made from their room). You look at a photo of the drawing and its short description, and write a **reading**: a
small JSON file that says which marks are which part, and which colours go where. A program then measures the
exact drawn shapes from the photo and builds the 3D model. You never draw coordinates of whole shapes by hand: you
point *into* the drawn areas, and the program follows the child's own lines.

**The goal:** the child recognises their drawing at once. Keep every shape, proportion, feature and wobble the
drawing has. Don't tidy it into a generic character, don't add things that aren't drawn (except where the colours
need a home), and keep its expression exactly.

## What you get and what you write

- `photo`: a phone photo of a pen or pencil drawing on paper. Paper texture, shadows, spiral binding, other doodles
  and handwritten names are part of the photo; they are not the character.
- `description`: `Name:`, `About:` and `Colours:` lines typed by the player. The colours are the family's choices.
- You write `reading.json` (UTF-8). Coordinates are **fractions of the photo's width and height** (0 to 1), origin at
  the **top left**, of the photo as it displays upright.

## Workflow

1. Look at the whole photo. Decide what the character is: its body, head (if separate), limbs, features (eyes,
   mouth, nose, cheeks, brows), and anything it carries or wears (hat, wand, torch, rainbow, horns, tail).
   Decide what to ignore (writing, the binding, stray marks).
2. Make grid pictures to read positions: the whole sheet, then zoomed crops of the face and any small details.
   Write them to a scratch folder, never into the repository:
   `python <this folder>/grid.py <photo> <scratch>/grid.png` and
   `python <this folder>/grid.py <photo> <scratch>/face.png 0.40 0.15 0.62 0.32` (x0 y0 x1 y1).
3. Write the reading (format below).
4. Check it: run the converter with `--no-render --debug-overlay <scratch>/overlay.png`. The overlay colours each
   area it found. Read its `note:` lines. Fix seeds that landed on a line or in the wrong area, and run again.
   Two or three rounds is normal. Then run it once more without `--no-render` to get the turntable.

## The format

```json
{
 "format": "enfractal.drawing-reading/1",
 "name": "Cloudpuff",
 "motion": "floats",
 "motion_why": "one line: what in the drawing says so",
 "figure_box": [x0, y0, x1, y1],
 "ignore": ["free text: what you left out and why"],
 "parts": [ {"name": "body", "role": "body", "parent": null, "pieces": [ ... ]}, ... ]
}
```

- **figure_box:** the character with a small margin (about 0.01) on every side, and nothing else: leave out the
  handwriting and the binding. Every line of the character must be inside it.
- **motion:** `floats` (no legs, or a cloud, ghost or bubble), `waddles` (short legs, round body), `hops` (small
  springy legs, an animal that would bounce) or `strides` (long legs).
- **parts:** listed **parents first**. Each has a unique `name` (`body`, `head`, `arm_left`, `hand_left`,
  `leg_right`, `foot_right`, `eye_left`, `mouth`, `hat`, `wand`...), a `role`, a `parent` (null only for the root,
  normally `body`) and `pieces`.
  - **left and right are the character's own:** its left side is on the image's right.
  - **roles:** `body`, `head`, `arm`, `hand`, `leg`, `foot`, `tail`, `horn`, `ear`, `wing`, `hair`, `hat`, `prop`
    (anything held or attached: wand, torch, rainbow), `eye`, `mouth`, `nose`, `brow`, `cheek`, `mark`.
  - A part's pivot (where it bends) is found from where it meets its parent, so give limbs, horns and props the
    parent they grow out of: `hand` under `arm`, `foot` under `leg`, `horn` under `head`, a held wand under the hand.
  - Make eyes, mouth, brows and cheeks their own parts (the game blinks and talks with them), parented to the part
    they sit on.

## Pieces: four kinds

A part is made of one or more pieces, each with a `colour` (`#rrggbb`, or `#rrggbbaa` for see-through).

### `puff`: a rounded 3D shape from an enclosed drawn area

```json
{"kind": "puff", "seeds": [[0.52, 0.47]], "colour": "#8b5a3c"}
```
- **seeds:** one or more points **inside the blank paper between the lines** of the area, not on a line. If lines
  inside the area cut it into pieces (a sock line across a leg, stripes on a horn), give one seed per sub-area that
  belongs to this piece. Put seeds in the roomy middle of an area, away from lines and from other areas' seeds.
- The program floods out from each seed until it meets the drawn lines; it bridges small gaps in the pencil and
  tries fainter pencil when an area runs out. An area that would swallow another piece's seed is treated as a leak,
  so keep each seed inside its own area.
- **depth** (optional): how deep the shape is compared with its drawn width: about 1 is round like a sausage or
  ball; 0.3 to 0.6 is flatter (wings, ears, paws, a flat sign); up to 1.8 for a body much wider than it is tall
  (a cloud). Bodies and heads get a sensible depth on their own; leave it out unless you have a reason.
- **forward** (optional): pushes the shape forward by this fraction of its thickness. Feet get 0.6 on their own.
- **clip** `[x0, y0, x1, y1]` (optional): keep only the part of the area inside this box. Use it to split one drawn
  area into two parts, such as a leg and a foot drawn without a line between them (leg: clip above the ankle; foot:
  clip below it).
- **outline** (optional): a rough polygon `[[x, y], ...]` around the area. It is used only if the lines leak (the
  child left a big gap) or with `silhouette`.
- **silhouette: true** with an **outline**: take everything inside the figure's outer lines and inside the outline
  polygon. Use it for an area that is filled with pencil shading or has too many inner lines to seed (a shaded
  rainbow), together with `minus_parts` and `minus_seeds`.
- **minus_parts** `["body"]`: remove areas of earlier parts. **minus_seeds**: remove the blank areas at these
  points (the sky inside a rainbow).
- **bands** `["#e8403f", "#f5913a", ...]`: split the area into stripes that follow its shape, outermost first, one
  colour each. With `minus_seeds`, stripes run from the outer edge to that inner hole (a rainbow's arcs).

### `sticker`: a flat feature lying on a surface

```json
{"kind": "sticker", "seeds": [[0.44, 0.25]], "colour": "#fbfaf5"}
```
Eye whites, irises, pupils, noses, paw pads, spots and patches. Seeds work as for `puff`. List stickers in the order
they stack: an eye white before its iris, an iris before a highlight.
- **blob: true:** the feature is drawn as a solid dark filled shape (a pupil coloured in); the seed goes on it.
- **thickness** (optional): how far it stands out, 1 by default.

### `ink`: drawn lines kept as lines

```json
{"kind": "ink", "box": [0.468, 0.275, 0.498, 0.289], "colour": "#22160f"}
```
Mouths, brows, eyelids, lashes, whiskers, blush marks, stripes, stitches and outlines of eyes. The program finds the
pencil lines inside the **box** and lays a thin raised line along each, on whatever surface is in front.
- Draw the box tightly around the line(s) you mean. Lines that only pass through the box (a face outline) are left
  out unless you set **take_all: true** (use it when the line you want is joined to an outline, like a lid line or
  the stripes on a horn, and for the outline of an eye).
- **weight** (optional): 1.3 for soft blush marks, 0.8 for fine lines.
- Instead of a box you may give **points** `[[x, y], ...]` along the line, for a line too faint to find.
- Ink colour: a very dark version of the character's colours (not pure black); blush is pink.

### `tube`: a single drawn line that is a thing of its own

```json
{"kind": "tube", "box": [0.5545, 0.612, 0.577, 0.696], "take_all": true, "radius": 0.011, "behind": 0.7, "colour": "#8b5a3c"}
```
A tail, antenna, whisker that sticks out, a stick arm or leg, a wand's stick, a bow string. **radius** is a fraction
of the character's height (0.008 thin to 0.02 thick). **behind** moves it back by that fraction of its parent's
thickness (a tail hangs behind). It connects itself to its parent.

## Colours

- Use the family's colours from the description, where they say. Where they don't say, place them where they make
  sense for this character: the main colour on the body, head and limbs; a second colour on feet, horns, pads, a
  hat band or a prop; keep eyes readable (a light white for eye whites, a dark colour for irises and pupils).
- A part that is a cloud, ghost or bubble can be see-through: `#rrggbbaa` with aa about `99`.
- Gently varied shades of one colour (a slightly darker foot, a lighter paw) help parts read in 3D.
- Pencil shading in the drawing usually means "this part is a different, darker colour": give it one.

## Good habits

- Every enclosed area of the character should belong to some piece, or be a hole on purpose (the sky under a
  rainbow, the gap between legs).
- One seed per enclosed sub-area. Two pieces never share an area.
- Give eyes an `ink` piece with `take_all` around the eye's outline when the drawing outlines its eyes: the dark
  outline is what makes a white eye readable on a light face.
- Keep the drawing's own pose: raised arms stay raised, a wink stays a wink, a lopsided smile stays lopsided.
- Save the reading next to the character it made (the converter copies it into its output as `reading.json`).

## What varies

Two readers will place seeds a little differently; that does not change the shapes, which come from the lines.
What can change is a judgement call: which colour goes where, whether a line is a tail, whether an area is a hat
or hair. Write those calls in `ignore` or `motion_why` so a person can check them.
