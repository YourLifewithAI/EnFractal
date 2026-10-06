# Barton tree asset proof

> Renamed from the Barton art pass on 6 October 2026. These are original painterly reference assets kept for outdoor test rooms and creations; the Central Texas species framing is historical.

These are original, illustrative live-oak and Ashe-juniper studies for the
grounded painterly 3D pipeline. They are not surveyed individuals or scanned
trees, and have not yet passed the art-quality gate.

`live-oak-v1.blend` and `ashe-juniper-v1.blend` retain the final meshes, original
packed painted textures, editable branch-curve controls, canopy placement/normal
guides, and a copy of the generator source. The hidden guide collection is a
construction aid. No closed crown surface is exported or rendered.

The authored architecture and placement recipe live in
`tools/art/build_barton_trees.py`. Edit that recipe and rebuild to reproduce the
exports. Direct mesh/curve edits in the Blender file are possible, but rerunning
the script currently rebuilds from the Python recipe; it does not read those
manual edits back automatically.

Run with Blender 4.5.14 LTS:

```powershell
& .cache/blender/blender-4.5.14-windows-x64/blender.exe --background --python-exit-code 1 --python tools/art/build_barton_trees.py -- --species all
```

The portable Blender archive came from the official download server:
`https://download.blender.org/release/Blender4.5/blender-4.5.14-windows-x64.zip`.
Its SHA-256 was checked against the release's official `.sha256` file:
`b9533d2397ac1984db4466fb23a7a4649391cca93f6e84209f9bcc60d071c8b9`.
Blender is cached locally and is not part of the repository.

Each game export has `Trunk` and `Leaves` mesh nodes. The leaf material is named
`Foliage oak` or `Foliage juniper`. Runtime GLBs contain no image bytes: Godot
assigns the shared material family and texture files at integration time.
The Blender sources retain packed originals for preview and editing.

Leaf UVs sample the full original cluster/spray image. The meshes use authored
ellipsoidal canopy-proxy normals, retained in export. Vertex color is **data**,
not RGB albedo:

- R: authored radial/height canopy-occlusion factor, not ray-traced AO.
- G: normalized height control for wind.
- B: deterministic per-card variation.
- A: one.

Bark UVs follow branch circumference and length. Dimensions, triangle counts,
source and texture hashes, vertex-channel contracts and limitations are recorded
in the adjacent runtime `.asset.json` files. Blender uses Z-up metres; glTF
exports Y-up for Godot. No collision mesh or distance LOD is included yet.

The tree geometry and builder were authored for EnFractal. Texture provenance
is recorded separately in the Barton texture source documents. These files are
an asset-production proof, not evidence of an accepted art style or measured
low-device performance.
