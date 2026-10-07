"""The room's scale: the model's own estimate, the founder's tape, and how the two are reconciled.

Each GPU batch comes back in its own metric frame, and the batches disagree with each other by
tens of percent. The merged room is therefore in the scale of one batch, the seed batch (batch 0),
and nothing in the photos says that scale is right. Two sources are recorded, in this order:

``tape``
    ``captures/<room>/measurements.json``: lengths the founder measured with a tape. One uniform
    scale is fitted to them (weighted least squares on the log of tape over model, never one scale
    per axis) and applied to the merged poses and to the room box, so the 25 cm cells, the 0.8 m
    low-photo threshold and every distance in the guidance are in real metres. The box is the one
    fitted before scaling, scaled with the poses (see ``scale_frame`` for why it is not fitted again).
``seed_batch``
    Without measurements the room keeps the seed batch's scale. The other batches' own estimates
    are reported only as an error bar relative to that scale.

Measurement file (never committed; it lives under ``captures/``)::

    {"measurements": [
        {"id": "length", "kind": "wall_to_wall", "between": ["B", "D"], "session_id": "s-...",
         "value_m": 5.842, "sigma_m": 0.01, "where": "along the floor, door to back wall"},
        {"id": "height", "kind": "floor_to_ceiling", "between": [], "session_id": "s-...", "value_m": 2.464},
        {"id": "corner-to-corner", "kind": "diagonal", "between": ["AB", "CD"], ...},
        {"id": "back-door", "kind": "opening", "between": ["B"], "value_m": 0.787, ...}]}

``session_id`` is the coverage session whose wall letters the founder confirmed on its map; a
measurement is used only for that session, because the letters follow the first photo. ``sigma_m``
is the tape's own reading error; the fit adds ``PLANE_FIT_SIGMA_M`` for the wall, floor and
ceiling planes the model's length depends on. Openings (doors, windows) are checks only: they are
compared with what the coverage grid saw, never fitted.
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass, field, replace
from pathlib import Path
from typing import Any

import numpy as np

from .chunks import move_prediction
from .grid import CELL_M, WALLS, runs
from .layout import RoomFrame

MEASUREMENTS_FILE = "measurements.json"
KINDS = ("wall_to_wall", "floor_to_ceiling", "diagonal", "opening")
OPPOSITE = {"A": "C", "C": "A", "B": "D", "D": "B"}
DEFAULT_TAPE_SIGMA_M = 0.01  # a tape read by eye
PLANE_FIT_SIGMA_M = 0.10  # where the model puts a wall, the floor or the ceiling
PLAUSIBLE_FACTOR = (0.6, 1.6)  # a fitted scale outside this is a wrong unit or a wrong wall letter
FLAG_FLOOR_M = 0.05  # never flag a residual under 5 cm, however tightly the wall copies agree
FLAG_SPREADS = 2.0  # flag a residual above this many wall-copy spreads


class MeasurementError(ValueError):
    """The measurements file is not usable; the message says what to fix."""


@dataclass(frozen=True)
class Measurement:
    id: str
    kind: str
    between: tuple[str, ...]
    session_id: str
    value_m: float
    sigma_m: float = DEFAULT_TAPE_SIGMA_M
    where: str = ""


@dataclass
class ScalePlan:
    """What scale the room gets, and why."""

    source: str  # "tape" or "seed_batch"
    factor: float = 1.0  # multiply the merged poses by this
    measurements: list[Measurement] = field(default_factory=list)  # those that count for this session
    fit: dict[str, Any] | None = None
    notes: list[str] = field(default_factory=list)  # plain words: why a measurement was left out


# --- the model's own estimate ----------------------------------------------------------------

def batch_scale_summary(batch_scales: dict[Any, float]) -> dict[str, Any]:
    """What the batches' own metric estimates say, relative to the scale the room is in.

    ``batch_scales`` maps batch -> the scale that brought that batch's raw prediction into the room
    frame (the seed batch is 1). Had batch b's raw prediction been metric, the room would have to
    be multiplied by ``1 / scale_b`` to be in metres, so ``vs_used_pct`` is the range of
    ``100 * (1 / scale_b - 1)``: the room could be that much smaller or larger than drawn.
    """
    scales = {int(k): float(v) for k, v in batch_scales.items()}
    implied = [1.0 / v for v in scales.values()]
    return {
        "batch_scales": {str(k): round(v, 4) for k, v in sorted(scales.items())},
        "median_batch_scale": round(float(np.median(list(scales.values()))), 4),
        "batches": len(scales),
        "vs_used_pct": {"low": round(100 * (min(implied) - 1), 1), "high": round(100 * (max(implied) - 1), 1)},
    }


def scale_prediction(pred: dict[str, np.ndarray], factor: float) -> dict[str, np.ndarray]:
    """A prediction scaled uniformly about the frame origin: its position and its point map."""
    S = np.eye(4)
    S[:3, :3] *= factor
    return move_prediction(pred, S)


def scale_frame(frame: RoomFrame, factor: float) -> RoomFrame:
    """The room box for poses scaled by ``factor``: the same box, scaled, not fitted again.

    Fitting the box again on the scaled points is not an option, because the ceiling and wall
    estimates are not stable enough: on the garage's poses the ceiling came out 2.48 m or 2.56 m
    depending on the fifth digit of the factor (the "outermost substantial peak" rule in ``layout``
    reacts to tiny changes in the points), which would put a random 8 to 10 cm into every residual
    against the tape.
    Scaling the fitted box keeps it exactly consistent with the scale the tape asked for. A world
    point X moves to ``factor * X`` and its room position to ``factor * (R X + t)``, so the rotation
    stays, the translation and every bound are scaled.
    """
    T = frame.world_to_room.copy()
    T[:3, 3] *= factor
    return replace(frame, world_to_room=T, ceiling_y=None if frame.ceiling_y is None else frame.ceiling_y * factor,
                   x_min=frame.x_min * factor, x_max=frame.x_max * factor, z_min=frame.z_min * factor,
                   z_max=frame.z_max * factor,
                   sources={**frame.sources, "scale": f"scaled by {factor:.4f} to the tape measurements"})


# --- the founder's tape ----------------------------------------------------------------------

def _number(entry: dict[str, Any], key: str, where: str, *, default: float | None = None) -> float:
    value = entry.get(key, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
        raise MeasurementError(f"{where}: '{key}' must be a number above zero, got {value!r}.")
    return float(value)


def _parse(entry: Any, index: int) -> Measurement:
    where = f"measurement {index + 1}"
    if not isinstance(entry, dict):
        raise MeasurementError(f"{where} must be an object.")
    ident = entry.get("id")
    if not isinstance(ident, str) or not ident.strip():
        raise MeasurementError(f"{where}: 'id' must be a short name.")
    where = f"measurement '{ident}'"
    kind = entry.get("kind")
    if kind not in KINDS:
        raise MeasurementError(f"{where}: 'kind' must be one of {', '.join(KINDS)}, got {kind!r}.")
    between = entry.get("between", [])
    if not isinstance(between, list) or not all(isinstance(b, str) for b in between):
        raise MeasurementError(f"{where}: 'between' must be a list of wall letters.")
    between = tuple(between)
    if kind == "wall_to_wall":
        if len(between) != 2 or any(b not in WALLS for b in between) or OPPOSITE[between[0]] != between[1]:
            raise MeasurementError(f"{where}: wall_to_wall needs two opposite walls (A and C, or B and D), "
                                   f"got {list(between)}.")
    elif kind == "floor_to_ceiling":
        if between:
            raise MeasurementError(f"{where}: floor_to_ceiling takes no walls; leave 'between' empty.")
    elif kind == "diagonal":
        ok = (len(between) == 2 and all(len(c) == 2 and c[0] != c[1] and set(c) <= set(WALLS) for c in between)
              and all(c[0] in "AC" and c[1] in "BD" or c[0] in "BD" and c[1] in "AC" for c in between)
              and set(between[0]) == {OPPOSITE[x] for x in between[1]})
        if not ok:
            raise MeasurementError(f"{where}: a diagonal needs two opposite corners, each named by the two walls "
                                   f"that meet there, like [\"AB\", \"CD\"], got {list(between)}.")
    else:  # opening
        if len(between) != 1 or between[0] not in WALLS:
            raise MeasurementError(f"{where}: an opening needs the one wall it is in, like [\"B\"].")
    session = entry.get("session_id")
    if not isinstance(session, str) or not session.strip():
        raise MeasurementError(f"{where}: 'session_id' must name the coverage session whose wall letters you "
                               f"checked on the map.")
    place = entry.get("where", "")
    return Measurement(ident.strip(), kind, between, session.strip(), _number(entry, "value_m", where),
                       _number(entry, "sigma_m", where, default=DEFAULT_TAPE_SIGMA_M),
                       place if isinstance(place, str) else "")


def load_measurements(path: Path, room: str | None = None) -> list[Measurement]:
    """The measurements in a file, checked. Raises ``MeasurementError`` saying what to fix."""
    try:
        data = json.loads(Path(path).read_bytes())
    except ValueError as exc:
        raise MeasurementError(f"{Path(path).name} is not valid JSON: {exc}") from exc
    if not isinstance(data, dict) or not isinstance(data.get("measurements"), list):
        raise MeasurementError(f"{Path(path).name} must be an object with a 'measurements' list.")
    if room is not None and data.get("room") not in (None, room):
        raise MeasurementError(f"{Path(path).name} is for room {data.get('room')!r}, not {room!r}.")
    found = [_parse(e, i) for i, e in enumerate(data["measurements"])]
    ids = [m.id for m in found]
    if len(set(ids)) != len(ids):
        raise MeasurementError("Measurement ids must be unique.")
    return found


def extents(frame) -> dict[str, float | None]:
    """The box's length along x, along z and its height, in the units the poses are in."""
    return {"x": frame.x_max - frame.x_min, "z": frame.z_max - frame.z_min, "y": frame.ceiling_y}


def model_length(m: Measurement, frame) -> float | None:
    """The model's value for a measurement, or None when it cannot be read (no ceiling found)."""
    size = extents(frame)
    if m.kind == "wall_to_wall":
        value = size[WALLS[m.between[0]][0]]
    elif m.kind == "floor_to_ceiling":
        value = size["y"]
    elif m.kind == "diagonal":
        value = math.hypot(size["x"], size["z"])
    else:
        raise ValueError("an opening is a check, not a length of the box")
    return None if value is None else float(value)


def fit_tape_scale(measurements: list[Measurement], frame) -> dict[str, Any] | None:
    """One uniform scale from the tape, or None when nothing can be fitted.

    Weighted least squares on ``ln(tape / model)``: with one unknown (the log of the scale) that is
    the weighted mean, each measurement weighted by ``1 / sigma^2`` where sigma is the relative
    error ``hypot(tape sigma, PLANE_FIT_SIGMA_M) / tape``. Openings are not fitted.
    """
    rows = []
    for m in measurements:
        if m.kind == "opening":
            continue
        length = model_length(m, frame)
        if length is None or length <= 0:
            continue
        rows.append((m, length, math.log(m.value_m / length), math.hypot(m.sigma_m, PLANE_FIT_SIGMA_M) / m.value_m))
    if not rows:
        return None
    y = np.array([r[2] for r in rows])
    w = 1.0 / np.array([r[3] for r in rows]) ** 2
    log_scale = float((w * y).sum() / w.sum())
    return {"factor": math.exp(log_scale), "sigma_pct": 100 * float(1.0 / math.sqrt(w.sum())),
            "used": [r[0].id for r in rows]}


def plan_scale(measurements: list[Measurement] | None, session_id: str, frame) -> ScalePlan:
    """Decide the room's scale from the measurements that apply to this session."""
    if not measurements:
        return ScalePlan("seed_batch")
    plan = ScalePlan("seed_batch")
    mine = [m for m in measurements if m.session_id == session_id]
    others = sorted({m.session_id for m in measurements if m.session_id != session_id})
    if others:
        plan.notes.append(
            f"{len(measurements) - len(mine)} measurement(s) were confirmed against session "
            f"{', '.join(others)}, not this session ({session_id}), and were not used: the wall letters follow "
            f"the first photo, so check the letters on this map and put this session's id in the file.")
    plan.measurements = mine
    if not mine:
        return plan
    fit = fit_tape_scale(mine, frame)
    if fit is None:
        plan.notes.append("None of the measurements could be fitted (openings are only checked, and a "
                          "floor-to-ceiling length needs the ceiling to be found), so the scale was not set.")
        return plan
    if not PLAUSIBLE_FACTOR[0] <= fit["factor"] <= PLAUSIBLE_FACTOR[1]:
        plan.notes.append(
            f"The measurements imply scaling the model by {fit['factor']:.2f}, which is not plausible, so nothing was "
            f"scaled. Check the units (metres) and that the wall letters match this session's map.")
        return plan
    plan.source, plan.factor, plan.fit = "tape", float(fit["factor"]), fit
    return plan


# --- checking the result ---------------------------------------------------------------------

def wall_copy_spread(xyz: np.ndarray, nor: np.ndarray, batch: np.ndarray, bounds, *, min_points: int = 300,
                     reach: float = 0.35, band: tuple[float, float] = (0.3, 2.2)) -> dict[str, Any]:
    """How far apart the batches put their own copies of each wall.

    For every wall and every batch with enough wall-facing points near the fitted plane, the median
    position of those points; the spread is the standard deviation of those medians across batches.
    ``typical_m`` is the median over the walls that have two or more batches: the size of the
    "doubled wall" error, and the yardstick a tape residual is judged against. ``xyz`` and ``nor``
    are in the room frame, ``batch`` is the batch of each point, ``bounds`` is (x min, x max, z min, z max).
    """
    x0, x1, z0, z1 = bounds
    planes = {"A": (2, +1, z0), "B": (0, -1, x1), "C": (2, -1, z1), "D": (0, +1, x0)}
    in_band = (xyz[:, 1] > band[0]) & (xyz[:, 1] < band[1])
    per_wall: dict[str, float | None] = {}
    for key, (axis, sign, plane) in planes.items():
        near = in_band & (nor[:, axis] * sign > 0.8) & (np.abs(xyz[:, axis] - plane) < reach)
        medians = []
        for b in np.unique(batch[near]):
            pick = near & (batch == b)
            if pick.sum() >= min_points:
                medians.append(float(np.median(xyz[pick, axis]) - plane))
        per_wall[key] = float(np.std(medians)) if len(medians) >= 2 else None
    known = [v for v in per_wall.values() if v is not None]
    return {"per_wall_m": {k: None if v is None else round(v, 3) for k, v in per_wall.items()},
            "typical_m": round(float(np.median(known)), 3) if known else None}


def opening_width_m(grid, *, rows_m: tuple[float, float] = (0.3, 1.8), share: float = 0.5) -> float | None:
    """Width of the widest doorway-like gap in a wall's coverage grid, to the nearest cell.

    A column counts as open when at least ``share`` of the cells between the two heights are
    "photos see past the wall" cells. None when the wall has no such gap.
    """
    rows = grid.opening.shape[0]
    r0, r1 = int(rows_m[0] / CELL_M), min(rows, int(math.ceil(rows_m[1] / CELL_M)))
    if r0 >= r1:
        return None
    best = max((b - a for a, b in runs(grid.opening[r0:r1].mean(axis=0) >= share)), default=0)
    return best * CELL_M if best else None


def check_measurements(plan: ScalePlan, frame_before, frame_after, walls: dict[str, Any],
                       spread: dict[str, Any]) -> dict[str, Any]:
    """Every measurement against the room as it now stands: residuals, diagnostics and flags.

    ``frame_before`` is the box in the model's own units (the per-axis scale each measurement would
    ask for alone is taken from it, as a diagnostic only); ``frame_after`` is the box after the
    uniform scale was applied. A residual is model minus tape. It is flagged above twice the
    wall-copy spread (at least ``FLAG_FLOOR_M``); an opening is flagged above one grid cell.
    """
    typical = spread.get("typical_m")
    limit = max(FLAG_SPREADS * (typical if typical is not None else PLANE_FIT_SIGMA_M), FLAG_FLOOR_M)
    rows, squares = [], []
    for m in plan.measurements:
        row: dict[str, Any] = {"id": m.id, "kind": m.kind, "between": list(m.between), "where": m.where,
                               "tape_m": m.value_m, "tape_sigma_m": m.sigma_m, "check_only": m.kind == "opening"}
        if m.kind == "opening":
            width = opening_width_m(walls[m.between[0]])
            row.update({"model_m": width, "residual_m": None if width is None else width - m.value_m,
                        "flagged": width is not None and abs(width - m.value_m) > CELL_M + 1e-9,
                        "tolerance_m": CELL_M})
        else:
            before, after = model_length(m, frame_before), model_length(m, frame_after)
            if after is None or before is None:
                row.update({"model_m": None, "residual_m": None, "flagged": False})
            else:
                residual = after - m.value_m
                squares.append(residual ** 2)
                row.update({"model_m": after, "residual_m": residual, "residual_pct": 100 * residual / m.value_m,
                            "implied_scale": m.value_m / before, "flagged": abs(residual) > limit})
        rows.append(row)
    return {
        "rows": rows,
        "rms_cm": round(100 * math.sqrt(sum(squares) / len(squares)), 1) if squares else None,
        "flag_limit_cm": round(100 * limit, 1),
        "wall_copy_spread_cm": None if typical is None else round(100 * typical, 1),
        "wall_copy_spread_per_wall_cm": {k: None if v is None else round(100 * v, 1)
                                         for k, v in spread.get("per_wall_m", {}).items()},
    }


def scale_record(plan: ScalePlan, batch: dict[str, Any], checks: dict[str, Any] | None,
                 frame_before, frame_after) -> dict[str, Any]:
    """Everything about the scale as plain JSON (coverage.json and coverage-run.json)."""
    tape = None
    if plan.measurements or plan.notes:
        tape = {"used": plan.source == "tape", "notes": list(plan.notes),
                "fit_sigma_pct": None if plan.fit is None else round(plan.fit["sigma_pct"], 2),
                "fitted": [] if plan.fit is None else plan.fit["used"], "checks": checks}
    size_before, size_after = extents(frame_before), extents(frame_after)
    return {
        "scale_source": plan.source,
        "applied_factor": round(plan.factor, 4),
        "room_before_m": {k: None if v is None else round(v, 3) for k, v in size_before.items()},
        "room_m": {k: None if v is None else round(v, 3) for k, v in size_after.items()},
        "batch": batch,
        "tape": tape,
    }
