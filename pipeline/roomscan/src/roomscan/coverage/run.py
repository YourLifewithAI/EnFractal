"""C2 orchestration: poses (cached per session), view graph, room frame, coverage, objects, guidance.

Outputs under ``<captures>/<room>/sessions/<session>/coverage/``:

    poses.npz, poses.json        merged poses and point maps (cache; recomputed with --recompute)
    detections.json              detector output per photo (cache)
    coverage.json                every number in the report
    coverage-map.png             the top-down map with wall strips
    coverage-report.md / .html   the report and guidance for the founder
    coverage-run.json            timings, GPU time, peak VRAM, models and licences
"""

from __future__ import annotations

import json
import platform
import time
from pathlib import Path
from typing import Any

import numpy as np

from .. import __version__
from ..ingest import resolve_session
from ..jsonio import json_bytes, sha256_hex, text_bytes
from ..paths import OutputGuard
from . import backend as be
from .chunks import merge_batches, plan_batches
from .geometry import transform_points
from .graph import build_view_graph, overlap_matrix
from .grid import WALLS, ceiling_grid, floor_grid, wall_axes, wall_grid
from .layout import camera_up, collect_points, fit_room

POSE_SETTINGS_VERSION = 1


def select_views(manifest: dict[str, Any], max_views: int | None = None) -> list[dict[str, Any]]:
    """Photos for pose estimation: not a duplicate, not a near-duplicate, readable and not blurry."""
    usable = [p for p in manifest["photos"]
              if p["status"] == "ok" and "blurry" not in (p.get("quality") or {}).get("flags", [])]
    usable.sort(key=lambda p: p.get("capture_index", 0))
    if max_views and len(usable) > max_views:
        keep = sorted(set(int(round(x)) for x in np.linspace(0, len(usable) - 1, max_views)))
        usable = [usable[i] for i in keep]
    return usable


def _pose_key(views: list[dict[str, Any]], settings: dict[str, Any]) -> str:
    listing = [[p["name"], p["sha256"]] for p in views]
    return sha256_hex(json_bytes({"views": listing, "settings": settings}))


def compute_poses(room_dir: Path, views: list[dict[str, Any]], *, chunk_size: int | None, anchors: int,
                  exif_intrinsics: bool, log=print) -> tuple[dict[int, dict[str, np.ndarray]], dict[str, Any]]:
    paths = [room_dir / p["derived"]["jpeg"] for p in views]
    Ks = [be.exif_intrinsics(p) if exif_intrinsics else None for p in views]
    free = be.wait_for_vram(4500, log=log)
    size = chunk_size or be.batch_size_for(free)
    batches = plan_batches(len(views), size, anchors)
    log(f"{len(views)} photos in {len(batches)} GPU batches of up to {size} ({free} MiB free before loading)")
    backend = be.MapAnythingBackend(log=log)
    backend.load()
    results = []
    gpu_free_log = []
    for i, batch in enumerate(batches):
        mem = be.nvidia_smi_memory()
        gpu_free_log.append(None if mem is None else mem[1] - mem[0])
        preds = backend.predict([paths[v] for v in batch.views], [Ks[v] for v in batch.views])
        results.append({v: pred for v, pred in zip(batch.views, preds)})
        log(f"  batch {i + 1}/{len(batches)}: {len(batch.views)} photos in {backend.stats.batches[-1]['seconds']:.1f}s")
    backend.unload()
    merged, fits = merge_batches(batches, results, log=log)
    info = {
        "backend": backend.name,
        "model": be.POSE_MODEL,
        "model_revision": be.MODEL_REGISTRY[be.POSE_MODEL]["revision"],
        "batch_size": size,
        "anchors": anchors,
        "batches": [{"views": b.views, "anchors": b.anchors} for b in batches],
        "fits": fits,
        "gpu": {
            "inference_seconds": round(backend.stats.seconds, 2),
            "peak_allocated_mib": round(backend.stats.peak_allocated_mib),
            "peak_reserved_mib": round(backend.stats.peak_reserved_mib),
            "weights_mib": round(backend.weights_mib),
            "per_batch": backend.stats.batches,
            "free_mib_before_each_batch": gpu_free_log,
        },
    }
    return merged, info


def save_poses(guard: OutputGuard, rel: Path, preds: dict[int, dict[str, np.ndarray]], n: int) -> None:
    import io

    order = list(range(n))
    arrays = {
        "c2w": np.stack([preds[i]["c2w"] for i in order]),
        "K": np.stack([preds[i]["K"] for i in order]),
        "K_model": np.stack([preds[i]["K_model"] for i in order]),
        "pts_cam": np.stack([preds[i]["pts_cam"] for i in order]),
        "conf": np.stack([preds[i]["conf"] for i in order]),
        "mask": np.stack([preds[i]["mask"] for i in order]),
        "batch": np.array([preds[i].get("batch", 0) for i in order]),
    }
    buf = io.BytesIO()
    np.savez_compressed(buf, **arrays)
    guard.write_bytes(rel, buf.getvalue())


def load_poses(path: Path) -> dict[int, dict[str, np.ndarray]]:
    data = np.load(path)
    out = {}
    for i in range(len(data["c2w"])):
        out[i] = {k: data[k][i] for k in ("c2w", "K", "K_model", "pts_cam", "conf", "mask", "batch")}
    return out


def run_coverage(captures_root: Path, room: str, *, session: str | None = "latest", max_views: int | None = None,
                 chunk_size: int | None = None, anchors: int = 6, skip_detection: bool = False,
                 reuse_poses: bool = True, exif_intrinsics: bool = True, log=print) -> dict[str, Any]:
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

    settings = {"version": POSE_SETTINGS_VERSION, "model": be.POSE_MODEL,
                "revision": be.MODEL_REGISTRY[be.POSE_MODEL]["revision"], "anchors": anchors,
                "chunk_size": chunk_size, "exif_intrinsics": exif_intrinsics, "stride": be.STORE_STRIDE}
    key = _pose_key(views, settings)
    poses_path = guard.path(cov_rel / "poses.npz")
    meta_path = guard.path(cov_rel / "poses.json")
    t0 = time.perf_counter()
    pose_info = None
    if reuse_poses and poses_path.is_file() and meta_path.is_file():
        meta = json.loads(meta_path.read_bytes())
        if meta.get("key") == key:
            preds = load_poses(poses_path)
            pose_info = meta["info"]
            pose_info["reused_from_cache"] = True
            log(f"Reusing cached poses for {len(views)} photos")
    if pose_info is None:
        preds, pose_info = compute_poses(room_dir, views, chunk_size=chunk_size, anchors=anchors,
                                         exif_intrinsics=exif_intrinsics, log=log)
        save_poses(guard, cov_rel / "poses.npz", preds, len(views))
        guard.write_bytes(cov_rel / "poses.json", json_bytes({"key": key, "names": [p["name"] for p in views],
                                                             "info": pose_info}))
        pose_info["reused_from_cache"] = False
    timings["poses"] = time.perf_counter() - t0

    # View graph and registration.
    t0 = time.perf_counter()
    order = list(range(len(views)))
    O = overlap_matrix([preds[i] for i in order])
    graph = build_view_graph(O)
    registered = [i for i in order if graph["registered"][i]]
    timings["graph"] = time.perf_counter() - t0
    log(f"View graph: {len(registered)}/{len(views)} photos registered, {len(graph['edges'])} links, "
        f"{len(graph['components'])} components")

    # Room frame.
    t0 = time.perf_counter()
    pts = collect_points(preds, registered)
    up0 = camera_up(preds, registered)
    frame = fit_room(pts, up0)
    T = frame.world_to_room
    xyz = transform_points(T, pts.xyz)
    nor = pts.normal @ T[:3, :3].T
    cams = {i: transform_points(T, preds[i]["c2w"][:3, 3]) for i in order}
    looks = {i: T[:3, :3] @ preds[i]["c2w"][:3, :3] @ np.array([0.0, 0.0, 1.0]) for i in order}
    bounds = (frame.x_min, frame.x_max, frame.z_min, frame.z_max)
    height = frame.ceiling_y if frame.ceiling_y else 2.5
    floor = floor_grid(xyz, nor, pts.view, bounds)
    walls = {k: wall_grid(k, xyz, nor, pts.view, bounds, height) for k in WALLS}
    ceiling = ceiling_grid(xyz, nor, pts.view, bounds, frame.ceiling_y) if frame.ceiling_y else None
    timings["layout_and_grids"] = time.perf_counter() - t0

    # Objects.
    objects: list = []
    det_info: dict[str, Any] = {"skipped": True}
    if not skip_detection:
        from .detect import detect_objects

        t0 = time.perf_counter()
        objects, det_info = detect_objects(guard, cov_rel, room_dir, views, preds, registered, T, bounds,
                                           xyz, pts.view, cams, log=log)
        timings["objects"] = time.perf_counter() - t0

    # Guidance, map and report.
    from .guidance import build_guidance
    from .render import render_map
    from .report import write_reports

    t0 = time.perf_counter()
    context = {
        "manifest": manifest, "views": views, "registered": registered, "graph": graph, "frame": frame,
        "floor": floor, "walls": walls, "ceiling": ceiling, "cams": cams, "looks": looks,
        "objects": objects, "xyz": xyz, "nor": nor, "owner": pts.view, "pose_info": pose_info,
        "wall_axes": {k: wall_axes(k, bounds) for k in WALLS}, "overlap": O,
    }
    guidance = build_guidance(context)
    context["guidance"] = guidance
    map_path = render_map(guard, cov_rel / "coverage-map.png", context)
    report_paths = write_reports(guard, cov_rel, context)
    timings["report"] = time.perf_counter() - t0
    timings["total"] = time.perf_counter() - t_all

    run = {
        "session_id": sid,
        "roomscan_version": __version__,
        "python": platform.python_version(),
        "finished_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "timing_s": {k: round(v, 2) for k, v in timings.items()},
        "poses": pose_info,
        "detection": det_info,
        "models": {m: be.MODEL_REGISTRY[m] for m in ([be.POSE_MODEL] + ([] if skip_detection else ["google/owlv2-base-patch16-ensemble"]))},
        "money_spent_usd": 0,
    }
    guard.write_bytes(cov_rel / "coverage-run.json", json_bytes(run))
    log(f"Coverage done in {timings['total']:.1f}s")
    return {"report": report_paths["html"], "markdown": report_paths["md"], "map": map_path, "run": run,
            "guidance": guidance}
