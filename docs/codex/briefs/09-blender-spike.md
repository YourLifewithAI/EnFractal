# Brief 09: can an AI build household objects in Blender on its own?

**Where:** the Codex CLI on the founder's machine, through `tools/codex/run.ps1` (your own checkout, the workspace-write sandbox, web search on).
**Branch:** `codex/09-blender-spike` (your checkout is already on it; the integrator commits your files).

**Context.** The founder's principle (7 October): the player's own AI makes the game's objects with software on the player's computer (or its own tools), guided by the game's MCP. Nothing hosted, nothing paid. Run 2's five objects:
- a cardboard box;
- a couch with wooden legs;
- a gaming laptop;
- an empty Bonne Maman jam jar;
- a metal French press.

Image-to-3D models struggle with glass and shiny metal. Simple household objects may be better built directly with Blender's Python API from photos and a few measurements. This spike tests that route on two of them, before any photos exist.

Blender is installed at `C:\Users\blues\AppData\Local\Programs\Blender`. Find the executable and its version there. Run it **headless only**: `blender --background --factory-startup --python <script>`, with Blender's user config and temp directories pointed inside your checkout or your temp folder. Never open Blender's window, and never use the GPU: render with Cycles on the CPU or with Workbench, at small sizes. Read AGENTS.md: metres, +Y up and -Z forward in Godot, so mind Blender's Z-up when you export glTF; the pivot is the bottom centre.

**Build two objects, each from one parametric script:**
1. **The cardboard box.** It needs:
   - walls with real thickness (about 4 mm);
   - closed or slightly open flaps;
   - a bevel on the edges;
   - a kraft-brown material;
   - parameters for width, depth and height.
2. **The empty Bonne Maman jam jar.**
   - Its shape: a profile spun round its axis, with the faceted lower body if you can, the shoulder, a threaded neck and the lid.
   - Its materials: clear glass, and the red-and-white gingham lid made procedurally.
   - Its size: the 370 g jar. Look up its real dimensions and cite the source, or say what you assumed.

**For each object:**
- export a `.glb` with the pivot at the bottom centre, metre units and Y up;
- render one small preview PNG (no larger than 640 × 480, CPU);
- report the triangle count, the bounding box in metres, and the run time.

**Also report:**
- what went wrong, and what Blender, the sandbox or the Python API made hard;
- how long each script took to write;
- what would make this a repeatable recipe for any capable AI, as notes towards the game's capture guidance (the MCP and skill), for example the parameters to measure, the order of steps, and checks before export;
- a short honest view of the other three objects (couch, laptop, French press): easy, hard, or better done another way.

Do not commit binary files larger than 1 MB. The `.glb` files stay in your temp folder unless each is under 1 MB.

```scope
docs/codex/spikes/09-blender/**
docs/codex/reports/09-blender-spike.md
```
