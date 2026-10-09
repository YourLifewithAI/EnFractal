# Synthetic room to landscape experiment

Open [sheet.png](sheet.png) first. Everything here is synthetic. There are no photos,
captures, GLBs, saved Blender scenes, game changes, or location metadata.

```powershell
python -B docs/codex/spikes/12-room-to-landscape/build.py --blender 'C:\Users\blues\AppData\Local\Programs\Blender\blender-5.2.2-windows-x64\blender.exe'
python -B docs/codex/spikes/12-room-to-landscape/verify.py
python -B contracts/validate.py --room docs/codex/spikes/12-room-to-landscape/room
```

`build.py` regenerates the room and its pinned primitive source metadata. The five
default sizes and full colour slots come from the read-only recipe specifications.
For the reference image, the worker imports each actual recipe, resolves
`style: "plain"`, constructs its materials, and uses its fitting/manifold checks.
Shelves and rug are synthetic additions. The room manifest is contract-valid;
`inventory.json` and the receipts are author experiment formats, not new contracts.

The Blender child processes use `--background --factory-startup`, four threads,
Cycles on the CPU, and OpenImageDenoise with GPU denoising disabled. All Blender
configuration/pattern scratch files live in directories made with `os.makedirs`
under the system temp folder and are removed after the run. Text outputs are UTF-8
with LF. Finalization strips PNG metadata while retaining the rendered RGB pixels;
receipts distinguish Blender's `raw_bytes` from metadata-free `final_bytes`.
A build replaces its own generated outputs; do not hand-edit them.

The kind route creates a slate ridge/valley, terraced butte, flooded ruin, crystal,
watchtower, terrace hamlet and heather meadow. The volume route accepts only box
geometry, pose and colour in its surface generator: a rounded rim, analytic relief
and basin, followed by an exact Boolean union of solids with the floor. It colours
tops, steep faces, low feet and opposing faces separately. Water surfaces remain
separate. It does not run a physical erosion simulation or reconstruct cavities.

The hybrid mixes 25% automatic relief with 75% authored relief for recognised
heightfield landmarks. Crystal and tower receive automatic foothills. It then
unites the solid contributions with the floor. Unrecognised geometry uses the
volume algorithm; in this room, the rug supplies the unchanged generic base case.
Shell slabs use their source positions and apertures: broken mountain silhouettes
on rear/header slabs, a ceiling retained as a cloud roof, and a light waterfall in
the existing window for the semantic routes. Shell solids stay separate from the
interior union so the same camera cutaway can be applied to all routes.

The overview hides the ceiling and the two camera-facing walls. The low camera
is at 0.10 m and keeps the ceiling. Window, door and ceiling fixture supply the
lights, with cool world fill; this is an author review rig, not validation of the
game's closed-room lighting model. Every image includes a measured 0.10 m figure.

Receipts audit the actual fitted donor geometry, including decoration, before
Boolean merging removes internal contacts. All seven AABBs match their source
boxes within 0.01 mm. This preserves contributor envelopes, not occupied-shape
fidelity: shelves become a filled cliff, the sofa loses its underside, and the
volume laptop becomes a solid mound. Union topology is reported separately.
Walk/climb fractions use assumed 35/55 degree thresholds on upward-facing area;
they do not establish connected routes, clearance, step height or safe collision.

For production, derive render and collision geometry from room data through the
kernel's command path. This script is disposable author tooling, never a companion
capability. See the [report](../../reports/12-room-to-landscape.md) for the judgement,
scan/game requirements, historical review and raw evidence.
