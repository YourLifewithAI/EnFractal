# Interpretation format, version 1

All coordinates are drawing units: `[image_x, image_y, depth_toward_viewer]`.
No metre conversion is needed. Width and height must use the same unit.
The front render preserves image left/right: game X is negative image X,
game Y is negative image Y, game Z is negative depth. The GLB faces -Z.

Root fields (all required):

```json
{
  "version": 1,
  "name": "A character",
  "image_size": [800, 1000],
  "origin": [400, 900, 0],
  "observations": ["What the drawing actually shows"],
  "uncertainties": ["What was inferred"],
  "colour_reasoning": "Where the supplied colours were placed",
  "motion": {"kind": "hop", "reason": "Short legs beneath a round body"},
  "palette": {"body": "#D29A54", "ink": "#302B39"},
  "parts": [
    {"name": "body", "role": "body", "parent": null,
     "pivot": [400, 600, 0], "shapes": [
       {"kind": "ellipsoid", "center": [400, 550, 0],
        "radii": [100, 140, 70], "colour": "body"}
     ]}
  ]
}
```

Parts have unique lowercase names (`[a-z][a-z0-9_]*`), a descriptive `role`,
`parent` (null or the name of a previous part), a joint `pivot` in absolute
drawing coordinates, and one or more shapes. Empty shapes are allowed for
joint-only nodes. A part's pivot does not change its shapes' coordinates.

Every shape has `kind` and `colour` (a palette key). Optional `surface` is a
previous part's name: that part's **first shape** must be an ellipsoid or volume.
The new shape's depth coordinates become offsets from that shape's front skin.
The skin is evaluated at every vertex, so pads and faces follow the curvature.
The part's pivot remains an absolute coordinate, even when its shapes attach.

Shapes:

- `ellipsoid`: `center: [x,y,d]`, `radii: [rx,ry,rd]`, all radii positive.
- `volume`: `contour: [[x,y], ...]` (3–64 points, no repeated last point),
  `center: [x,y,d]`, `thickness: positive_number`. Center x/y is a descriptive
  landmark; depth sets the middle plane. Concave outlines are supported. The
  contour is smoothed by Catmull–Rom interpolation unless `smooth: false`.
  A triangulated surface rounds both sides into a closed cushion, with skin
  thickness increasing with distance from the outline. Holes are unsupported.
  `smooth: false` is useful for deliberate points such as a heart's bottom.
- `tube`: `points: [[x,y,d], ...]` (2–64), `radii: [r,...]` (one per point,
  strictly positive; use a small value such as 0.3 for tips).
  Smoothed centerline and round cross-section, with closed end caps.
  Optional `smooth: false` makes straight sections. Radius is interpolated
  with the centerline; avoid coincident points and extreme radius changes.

Multiple shapes in a part share its pivot. Attach small marks to that part or
make them children so motion remains coherent. All geometry is opaque, rough
base colour. Transparency, skeletal rigging, textures and automatic OCR are
not supported in this version. Do not emit paths, URLs, scripts or tool calls.
