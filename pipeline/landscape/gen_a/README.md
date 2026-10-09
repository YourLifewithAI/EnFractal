# Summer valley

A deterministic, offline first build for the two synthetic garage observations.
Only `room.json` and `inventory.json` are read. The source observations are never
changed. All geometry is generated with the Python standard library and passed
to the frozen [landscape harness](../harness/README.md).

Run from the repository root. Choose **new, empty output folders**; the package
writer intentionally refuses to overwrite a package. Keep generated packages
and draft renders outside the repository.

```powershell
python -B -m pipeline.landscape.gen_a.generate --room pipeline/landscape/corpus/rooms/garage_nominal --out "$env:TEMP/valley-package"
python -B -m pipeline.landscape.gen_a.checks --package "$env:TEMP/valley-package" --room pipeline/landscape/corpus/rooms/garage_nominal
python -B -m pipeline.landscape.harness.render --package "$env:TEMP/valley-package" --room pipeline/landscape/corpus/rooms/garage_nominal --label "SUMMER VALLEY" --out "$env:TEMP/valley-render" --expect-setup pipeline/landscape/harness/ab-setup.json --draft
```

Use `garage_scan_17` for both room arguments to generate the noisy observation.
Omit `--draft` for final rendering. The renderer enforces background Blender,
CPU rendering, the pinned version, and the fixed setup. No window, GPU, network,
installation, or additional dependency is needed.

One-command test suite:

```powershell
python -B -S -m unittest pipeline.landscape.gen_a.test_generate -v
```

Tests build the nominal room twice and the scan once in unique `os.makedirs`
folders beneath the system temporary directory. They compare every package byte,
validate both decoded packages, check per-object uplift by ablation, and inject
five faults to check the checks. Temporary test packages are then removed.

The terrain is one connected 2.5 cm grid. Checks reconstruct its exact float32
triangles, including topology and winding, and sample those triangles rather
than the generator's ideal height function. Water cross-sections, triangle
interiors and edges must stay above their carved bed and flow downhill. Building
and pickup footprints must meet the terrain within 0.1 mm. A* uses a 4 cm grid,
an 11 cm carrying envelope, solid-prop clearance, a 20-degree maximum support
incline and centimetre samples along edges. The route is searched both ways.
Trees use trunk clearance; grass, ferns and flowers are non-solid vegetation.
Buildings use conservative outside bounds: the tested destinations are their
approaches, not an interior navigation certificate.

The seed is `170621`. The supported scope is the garage layout, including its
noisy variant; settlement composition and drainage control points are authored
for that layout. This is not a universal room converter, physical erosion
simulation, room-manifest exporter or Godot playtest. The package's `x_` records
retain the seed, paths, source grounding witnesses and invented interventions.
Only the rendered meshes and instance transforms are used for geometric checks.

See [REPORT.md](REPORT.md) for results and limitations, and
[EVIDENCE.md](EVIDENCE.md) for raw command evidence and iteration failures.
