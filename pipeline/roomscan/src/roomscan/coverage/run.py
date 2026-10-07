"""C2 orchestration: poses (cached per session), view graph, room frame, coverage, objects, guidance.

Outputs under ``<captures>/<room>/sessions/<session>/coverage/``:

    poses.npz, poses.json        merged poses and point maps (cache; recomputed with --recompute)
    detections.json              detector output per photo (cache)
    coverage.json                every number in the report
    coverage-map.png             the top-down map with wall bands, cameras, objects and guidance
    walls.png                    the four walls and the ceiling unfolded
    coverage-report.md / .html   the report and guidance for the founder
    coverage-run.json            timings, GPU time, peak VRAM, models and licences

The latest report and map are also copied to ``<captures>/<room>/coverage-report.html`` and
``<captures>/<room>/coverage-map.png`` so they are easy to find.
"""

from __future__ import annotations

import io
import json
import platform
import time
from pathlib import Path
from typing import Any, Callable

import numpy as np

from .. import __version__
from ..ingest import resolve_session
from ..jsonio import json_bytes, sha256_hex
from ..paths import OutputGuard
from . import backend as be
from .chunks import merge_batches, plan_batches
from .geometry import transform_points
from .graph import build_view_graph, overlap_matrix
from .grid import WALLS, ceiling_grid, floor_grid, wall_axes, wall_grid
from .visibility import Views
from .layout import camera_forward, camera_up, collect_points, fit_room
from .refine import consensus_scale, refine_batches

POSE_SETTINGS_VERSION = 3  # 3: draft-mode decoding, batch refinement


def select_views(manifest: dict[str, Any], max_views: int | None = None) -> list[dict[str, Any]]:
    """Photos for pose estimation: not a duplicate, not a near-duplicate, readable and not blurry."""
    usable = [p for p in manifest["photos"]
              if p["status"] == "ok" and "blurry" not in (p.get("quality") or {}).get("flags", [])]
    usable.sort(key=lambda p: (p.get("capture_index", 0), p["name"]))
    if max_views and len(usable) > max_views:
        keep = sorted(set(int(round(x)) for x in np.linspace(0, len(usable) - 1, max_views)))
        usable = [usable[i] for i in keep]
    return usable


def _pose_key(views: list[dict[str, Any]], settings: dict[str, Any]) -> str:
    listing = [[p["name"], p["sha256"]] for p in views]
    return sha256_hex(json_bytes({"views": listing, "settings": settings}))


def compute_poses(room_dir: Path, views: list[dict[str, Any]], *, chunk_size: int | None, anchors: int,
                  exif_intrinsics: bool, backend_factory: Callable[[], Any] | None = None,
                  log=print) -> tuple[dict[int, dict[str, np.ndarray]], dict[str, Any]]:
    """Poses for every view, batched to fit the GPU and merged into one frame.

    Views whose batch could not be merged are missing from the returned dict.
    """
    paths = [room_dir / p["derived"]["jpeg"] for p in views]
    Ks = [be.exif_intrinsics(p) if exif_intrinsics else None for p in views]
    real_gpu = backend_factory is None
    free = be.wait_for_vram(4500, log=log) if real_gpu else None
    size = chunk_size or (be.batch_size_for(free) if free else 32)
    batches = plan_batches(len(views), size, anchors)
    log(f"{len(views)} photos in {len(batches)} GPU batches of up to {size}"
        + (f" ({free} MiB free before loading)" if free else ""))
    backend = be.MapAnythingBackend(log=log) if real_gpu else backend_factory()
    t0 = time.perf_counter()
    backend.load()
    load_s = time.perf_counter() - t0
    results = []
    gpu_free_log = []
    paused_s = 0.0
    for i, batch in enumerate(batches):
        if real_gpu and be.game_window_open():
            backend.unload()  # give the game the whole card while it runs
            paused_s += be.wait_while_game_runs(log=log)
            backend.load()
        mem = be.nvidia_smi_memory() if real_gpu else None
        gpu_free_log.append(None if mem is None else mem[1] - mem[0])
        preds = backend.predict([paths[v] for v in batch.views], [Ks[v] for v in batch.views])
        results.append({v: pred for v, pred in zip(batch.views, preds)})
        log(f"  batch {i + 1}/{len(batches)}: {len(batch.views)} photos in {backend.stats.batches[-1]['seconds']:.1f}s")
    backend.unload()
    t0 = time.perf_counter()
    merged, fits = merge_batches(batches, results, log=log)
    merge_s = time.perf_counter() - t0
    t0 = time.perf_counter()
    merged, refinement = refine_batches(merged, log=log)
    refinement["seconds"] = round(time.perf_counter() - t0, 2)
    scales = {0: 1.0}
    for f in fits[1:]:
        if f.get("merged"):
            extra = refinement["per_batch"].get(str(f["batch"]), {}).get("scale", 1.0)
            scales[f["batch"]] = f["scale"] * extra
    info = {
        "backend": backend.name,
        "model": be.POSE_MODEL if real_gpu else getattr(backend, "model_id", backend.name),
        "model_revision": be.MODEL_REGISTRY[be.POSE_MODEL]["revision"] if real_gpu else None,
        "batch_size": size,
        "anchors": anchors,
        "batches": [{"views": b.views, "anchors": b.anchors} for b in batches],
        "fits": fits,
        "refinement": refinement,
        # The room keeps the reference batch's metric scale; the spread of the other batches'
        # scales is the honest error bar on every distance in the report.
        "scale": {"batch_scales": {str(k): round(v, 4) for k, v in sorted(scales.items())},
                  **{k: round(v, 4) for k, v in consensus_scale(scales).items()}},
        "merged_views": len(merged),
        "gpu": {
            "load_seconds": round(load_s, 2),
            "inference_seconds": round(backend.stats.seconds, 2),
            "photo_prepare_seconds_cpu": round(backend.stats.prepare_seconds, 2),
            "merge_seconds_cpu": round(merge_s, 2),
            "paused_for_game_seconds": round(paused_s, 1),
            "peak_allocated_mib": round(backend.stats.peak_allocated_mib),
            "peak_reserved_mib": round(backend.stats.peak_reserved_mib),
            "weights_mib": round(backend.weights_mib),
            "per_batch": backend.stats.batches,
            "free_mib_before_each_batch": gpu_free_log,
        },
    }
    return merged, info


_POSE_ARRAYS = {"c2w": ((4, 4), np.float64), "K": ((3, 3), np.float64), "K_model": ((3, 3), np.float64)}


def save_poses(guard: OutputGuard, rel: Path, preds: dict[int, dict[str, np.ndarray]], n: int) -> None:
    first = next(iter(preds.values()))
    h, w = first["pts_cam"].shape[:2]
    shapes = dict(_POSE_ARRAYS)
    shapes.update({"pts_cam": ((h, w, 3), np.float16), "conf": ((h, w), np.float16), "mask": ((h, w), bool)})
    arrays: dict[str, np.ndarray] = {"placed": np.array([i in preds for i in range(n)])}
    for key, (shape, dtype) in shapes.items():
        arrays[key] = np.stack([np.asarray(preds[i][key], dtype) if i in preds else np.zeros(shape, dtype)
                                for i in range(n)])
    arrays["batch"] = np.array([int(preds[i].get("batch", 0)) if i in preds else -1 for i in range(n)])
    buf = io.BytesIO()
    np.savez_compressed(buf, **arrays)
    guard.write_bytes(rel, buf.getvalue())


def load_poses(path: Path) -> dict[int, dict[str, np.ndarray]]:
    data = np.load(path)
    out = {}
    for i in range(len(data["c2w"])):
        if not bool(data["placed"][i]):
            continue
        out[i] = {k: data[k][i] for k in ("c2w", "K", "K_model", "pts_cam", "conf", "mask")}
        out[i]["batch"] = int(data["batch"][i])
    return out


def run_coverage(captures_root: Path, room: str, *, session: str | None = "latest", max_views: int | None = None,
                 chunk_size: int | None = None, anchors: int = 6, skip_detection: bool = False,
                 reuse_poses: bool = True, exif_intrinsics: bool = True,
                 backend_factory: Callable[[], Any] | None = None, detector_factory: Callable[[], Any] | None = None,
                 log=print) -> dict[str, Any]:
    t_all = time.perf_counter()
    timings: dict[str, float] = {}
    session_dir = resolve_session(captures_root, room, session)
    room_dir = session_dir.parents[1]
    guard = OutputGuard(room_dir)
    manifest = json.loads((session_dir / "manifest.json").read_bytes())
    sid = manifest["session_id"]
    cov_rel = Path("sessions") / sid / "coverage"
    guard.mkdir(cov_rel)
    views = select_views(manifest, max_views)
    if len(views) < 2:
        raise ValueError("Need at least two usable photos for coverage.")

    settings = {"version": POSE_SETTINGS_VERSION, "anchors": anchors, "chunk_size": chunk_size,
                "exif_intrinsics": exif_intrinsics, "stride": be.STORE_STRIDE,
                "backend": "mapanything" if backend_factory is None else "injected"}
    if backend_factory is None:
        settings.update({"model": be.POSE_MODEL, "revision": be.MODEL_REGISTRY[be.POSE_MODEL]["revision"]})
    key = _pose_key(views, settings)
    poses_path = guard.path(cov_rel / "poses.npz")
    meta_path = guard.path(cov_rel / "poses.json")
    t0 = time.perf_counter()
    pose_info = None
    if reuse_poses and backend_factory is None and poses_path.is_file() and meta_path.is_file():
        meta = json.loads(meta_path.read_bytes())
        if meta.get("key") == key:
            preds = load_poses(poses_path)
            pose_info = meta["info"]
            pose_info["reused_from_cache"] = True
            log(f"Reusing cached poses for {len(views)} photos")
    if pose_info is None:
        preds, pose_info = compute_poses(room_dir, views, chunk_size=chunk_size, anchors=anchors,
                                         exif_intrinsics=exif_intrinsics, backend_factory=backend_factory, log=log)
        if not preds:
            raise RuntimeError("No batch could be placed; see the log.")
        save_poses(guard, cov_rel / "poses.npz", preds, len(views))
        guard.write_bytes(cov_rel / "poses.json", json_bytes({"key": key, "names": [p["name"] for p in views],
                                                             "info": pose_info}))
        pose_info["reused_from_cache"] = False
    timings["poses"] = time.perf_counter() - t0

    # View graph and registration: which photos fit together.
    t0 = time.perf_counter()
    order = list(range(len(views)))
    O = overlap_matrix([preds.get(i) for i in order])
    graph = build_view_graph(O)
    registered = [i for i in order if graph["registered"][i]]
    timings["view_graph"] = time.perf_counter() - t0
    log(f"View graph: {len(registered)}/{len(views)} photos fitted, {len(graph['edges'])} links, "
        f"{len(graph['components'])} groups")
    if len(registered) < 2:
        raise RuntimeError("Fewer than two photos fit together; nothing to map.")

    # Room frame and coverage grids.
    t0 = time.perf_counter()
    pts = collect_points(preds, registered)
    up0 = camera_up(preds, registered)
    frame = fit_room(pts, up0, forward=camera_forward(preds, registered[0]))
    T = frame.world_to_room
    xyz = transform_points(T, pts.xyz)
    nor = pts.normal @ T[:3, :3].T
    cams = {i: transform_points(T, preds[i]["c2w"][:3, 3]) for i in preds}
    looks = {i: T[:3, :3] @ preds[i]["c2w"][:3, :3] @ np.array([0.0, 0.0, 1.0]) for i in preds}
    bounds = (frame.x_min, frame.x_max, frame.z_min, frame.z_max)
    height = frame.ceiling_y if frame.ceiling_y else 2.5
    seen_by = Views.from_preds(preds, registered, T)
    floor = floor_grid(seen_by, xyz, bounds)
    walls = {k: wall_grid(k, seen_by, bounds, height) for k in WALLS}
    ceiling = ceiling_grid(seen_by, bounds, frame.ceiling_y) if frame.ceiling_y else None
    timings["layout_and_grids"] = time.perf_counter() - t0

    # Objects.
    objects: list = []
    det_info: dict[str, Any] = {"skipped": True}
    if not skip_detection:
        from .detect import detect_objects

        t0 = time.perf_counter()
        try:
            objects, det_info = detect_objects(guard, cov_rel, room_dir, views, preds, registered, T, bounds,
                                               xyz, pts.view, cams, detector_factory=detector_factory, log=log)
        except Exception as exc:  # report without objects rather than no report at all
            det_info = {"error": f"{type(exc).__name__}: {exc}"}
            log(f"Object detection failed ({det_info['error']}); the report has no per-object counts.")
        timings["objects"] = time.perf_counter() - t0

    # Guidance, map and report.
    from .guidance import build_guidance
    from .render import render_map, render_walls
    from .report import coverage_record, write_reports

    t0 = time.perf_counter()
    context = {
        "manifest": manifest, "views": views, "registered": registered, "graph": graph, "frame": frame,
        "floor": floor, "walls": walls, "ceiling": ceiling, "cams": cams, "looks": looks,
        "objects": objects, "xyz": xyz, "nor": nor, "owner": pts.view, "pose_info": pose_info,
        "wall_axes": {k: wall_axes(k, bounds) for k in WALLS}, "overlap": O, "detection": det_info,
        "room_dir": room_dir,
    }
    guidance = build_guidance(context)
    context["guidance"] = guidance
    map_path = render_map(guard, cov_rel / "coverage-map.png", context)
    walls_path = render_walls(guard, cov_rel / "walls.png", context)
    context["map_path"], context["walls_path"] = map_path, walls_path
    guard.write_bytes(cov_rel / "coverage.json", json_bytes(coverage_record(context)))
    timings["report"] = time.perf_counter() - t0
    timings["total"] = time.perf_counter() - t_all

    models = {}
    if backend_factory is None:
        models[be.POSE_MODEL] = be.MODEL_REGISTRY[be.POSE_MODEL]
        models.update({f"torch.hub {k}": v for k, v in be.TORCH_HUB_PINS.items()})
    if not skip_detection and detector_factory is None and "error" not in det_info:
        from .objects import DETECTOR

        models[DETECTOR] = be.MODEL_REGISTRY[DETECTOR]
    run = {
        "session_id": sid,
        "roomscan_version": __version__,
        "python": platform.python_version(),
        "finished_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "timing_s": {k: round(v, 2) for k, v in timings.items()},
        "poses": pose_info,
        "detection": det_info,
        "models": models,
        "money_spent_usd": 0,
    }
    context["run"] = run
    report_paths = write_reports(guard, cov_rel, context)
    guard.write_bytes(cov_rel / "coverage-run.json", json_bytes(run))
    # Easy-to-find copies of the latest report and map at the room folder's top level.
    guard.write_bytes("coverage-report.html", Path(report_paths["html"]).read_bytes())
    guard.write_bytes("coverage-report.md", Path(report_paths["md"]).read_bytes())
    guard.write_bytes("coverage-map.png", Path(map_path).read_bytes())
    guard.write_bytes("walls.png", Path(walls_path).read_bytes())
    log(f"Coverage done in {timings['total']:.1f}s")
    return {"report": report_paths["html"], "markdown": report_paths["md"], "map": map_path, "walls": walls_path,
            "run": run, "guidance": guidance, "room_report": guard.path("coverage-report.html"),
            "room_map": guard.path("coverage-map.png")}
