"""A box for an object from its points: where it stands, how big it is, which way it faces.

The room frame has +Y up and a floor at y = 0. An object's box is turned about Y only (furniture
stands upright), so the fit is a 2-D problem in the floor plane plus a height. The box is the
tightest rectangle round the central part of the points (a few strays and depth-edge flyers do not
stretch it), turned by up to 90 degrees to the smallest area, with a mild pull toward the room's own
axes because most things stand square to a wall. Local axes: +X is the box's width, +Z its depth, and
an object's front looks along local -Z (Godot's forward), so ``yaw_deg`` turns local -Z to the
direction the front faces, positive turning left seen from above.

Photos show only the surfaces they see: a sofa against a wall is seen from the front, so its box is
as deep as its front face. The fit reports how much of the box was seen (``sides_seen``) and leaves
extending it to the wall to the caller, who knows the room.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np


@dataclass
class Box:
    centre_m: tuple[float, float, float]  # the box's centre (x, y, z)
    size_m: tuple[float, float, float]  # width (local X), height (Y), depth (local Z)
    yaw_deg: float  # turns local -Z (the front) to the direction it faces
    base_y: float  # underside of the box: 0 on the floor, the surface's height on a shelf or desk
    front_known: bool = False
    notes: list[str] = field(default_factory=list)

    @property
    def position_m(self) -> tuple[float, float, float]:
        """The pivot: the bottom centre of the box."""
        return (self.centre_m[0], self.base_y, self.centre_m[2])

    @property
    def footprint(self) -> tuple[float, float, float, float]:
        """Axis-aligned (x0, z0, x1, z1) of the turned box on the floor."""
        corners = box_corners_xz(self)
        return (float(corners[:, 0].min()), float(corners[:, 1].min()), float(corners[:, 0].max()), float(corners[:, 1].max()))


def forward_xz(yaw_deg: float) -> np.ndarray:
    """The direction (x, z) a box with this yaw faces: local -Z turned by yaw about +Y."""
    a = math.radians(yaw_deg)
    return np.array([-math.sin(a), -math.cos(a)])


def right_xz(yaw_deg: float) -> np.ndarray:
    """The direction (x, z) of local +X for this yaw."""
    a = math.radians(yaw_deg)
    return np.array([math.cos(a), -math.sin(a)])


def box_corners_xz(box: Box) -> np.ndarray:
    r, f = right_xz(box.yaw_deg), -forward_xz(box.yaw_deg)  # local +Z is behind the front
    w, d = box.size_m[0] / 2, box.size_m[2] / 2
    c = np.array([box.centre_m[0], box.centre_m[2]])
    return np.array([c - r * w - f * d, c + r * w - f * d, c + r * w + f * d, c - r * w + f * d])


def to_local_xz(points_xz: np.ndarray, centre_xz, yaw_deg: float) -> np.ndarray:
    """Floor positions (N, 2) as (local x, local z) of a box with this centre and yaw."""
    r, back = right_xz(yaw_deg), -forward_xz(yaw_deg)
    rel = np.asarray(points_xz) - np.asarray(centre_xz)
    return np.stack([rel @ r, rel @ back], -1)


def _spans(local: np.ndarray, lo_q: float, hi_q: float) -> np.ndarray:
    return np.percentile(local, hi_q, axis=0) - np.percentile(local, lo_q, axis=0)


def fit_footprint(points: np.ndarray, *, lo_q: float = 3.0, hi_q: float = 97.0, axis_pull: float = 0.06,
                  step_deg: float = 1.0) -> tuple[np.ndarray, np.ndarray, tuple[float, float], tuple[float, float]]:
    """The smallest rectangle round the points' central part, turned by up to 90 degrees.

    Returns the room directions (x, z) of its two sides, their lengths, and its centre (x, z). The first side
    runs along (cos t, sin t) and the second along (-sin t, cos t) for the best angle t; ``axis_pull`` adds that
    fraction of the area for a rectangle 45 degrees from the room's axes, so a near-square fit of a square-on
    thing snaps to square.
    """
    xz = np.asarray(points)[:, [0, 2]]
    best = None
    for theta in np.arange(0.0, 90.0, step_deg):
        t = math.radians(theta)
        d1, d2 = np.array([math.cos(t), math.sin(t)]), np.array([-math.sin(t), math.cos(t)])
        local = np.stack([xz @ d1, xz @ d2], -1)
        span = _spans(local, lo_q, hi_q)
        mis = min(theta, 90 - theta) / 45.0
        cost = float(span[0] * span[1]) * (1 + axis_pull * mis * mis)
        if best is None or cost < best[0]:
            mid = (np.percentile(local, lo_q, axis=0) + np.percentile(local, hi_q, axis=0)) / 2
            centre = mid[0] * d1 + mid[1] * d2
            best = (cost, d1, d2, (float(span[0]), float(span[1])), (float(centre[0]), float(centre[1])))
    assert best is not None
    return best[1], best[2], best[3], best[4]


def fit_box(points: np.ndarray, *, floor_m: float = 0.0, on_floor_below_m: float = 0.12, lo_q: float = 3.0,
            hi_q: float = 97.0, wide_first: bool = True, round_footprint: bool = False, on_floor: bool = False,
            y_range: tuple[float, float] | None = None) -> Box:
    """A box round an object's points (room frame).

    ``on_floor_below_m``: when the lowest points are within this of the floor the object stands on it and its box
    starts at the floor. ``on_floor`` says it stands there whatever the points say (a sofa's lowest visible point is
    its seat's overhang, not its base). ``y_range`` replaces the pooled height percentiles with a robust pair worked
    out photo by photo. ``wide_first`` makes the wider side the box's width (local X). A ``round_footprint`` has
    no meaningful yaw: the box is axis-aligned and square-ish.
    """
    pts = np.asarray(points, float)
    if len(pts) < 8:
        raise ValueError("need at least 8 points to fit a box")
    y_lo, y_hi = y_range if y_range is not None else (float(np.percentile(pts[:, 1], lo_q)), float(np.percentile(pts[:, 1], hi_q)))
    notes: list[str] = []
    if on_floor or y_lo < on_floor_below_m + floor_m:
        base = floor_m
    else:
        base = y_lo
        notes.append(f"rests on a surface {base:.2f} m up")
    if round_footprint:
        xz = pts[:, [0, 2]]
        lo, hi = np.percentile(xz, lo_q, axis=0), np.percentile(xz, hi_q, axis=0)
        span = hi - lo
        side = float(np.mean(span))
        centre = (lo + hi) / 2
        return Box((float(centre[0]), (base + y_hi) / 2, float(centre[1])), (side, max(y_hi - base, 0.01), side), 0.0, base, False,
                   notes + ["round footprint: yaw is meaningless"])
    d1, d2, (a, b), (cx, cz) = fit_footprint(pts, lo_q=lo_q, hi_q=hi_q)
    right = d1
    if wide_first and b > a:
        a, b, right = b, a, d2
    # Local +X is ``right``: right_xz(yaw) = (cos yaw, -sin yaw).
    yaw = math.degrees(math.atan2(-right[1], right[0]))
    yaw = (yaw + 180.0) % 360.0 - 180.0
    return Box((cx, (base + y_hi) / 2, cz), (a, max(y_hi - base, 0.01), b), yaw, base, False, notes)


def face_away_from(box: Box, direction_xz: np.ndarray) -> Box:
    """Turn a box so its front looks along ``direction_xz`` (by 0 or 180 degrees; the sides stay where they are)."""
    f = forward_xz(box.yaw_deg)
    if float(f @ np.asarray(direction_xz, float)) < 0:
        yaw = (box.yaw_deg + 180.0 + 180.0) % 360.0 - 180.0
        return Box(box.centre_m, box.size_m, yaw, box.base_y, True, box.notes)
    return Box(box.centre_m, box.size_m, box.yaw_deg, box.base_y, True, box.notes)


def front_from_height_profile(points: np.ndarray, box: Box, *, min_difference_m: float = 0.05) -> np.ndarray | None:
    """For a thing with a back (a sofa, a chair): the direction (x, z) of its front, from where it is taller.

    The back is the side whose points are higher on average. Returns None when the two halves are within
    ``min_difference_m`` of each other.
    """
    local = to_local_xz(np.asarray(points)[:, [0, 2]], (box.centre_m[0], box.centre_m[2]), box.yaw_deg)
    heights = np.asarray(points)[:, 1]
    behind, ahead = heights[local[:, 1] > 0], heights[local[:, 1] <= 0]  # local +Z is behind the front
    if len(behind) < 10 or len(ahead) < 10:
        return None
    # The top of each half: its upper quartile, which a seat cushion and a backrest tell apart.
    diff = float(np.percentile(behind, 80) - np.percentile(ahead, 80))
    if abs(diff) < min_difference_m:
        return None
    return forward_xz(box.yaw_deg) if diff > 0 else -forward_xz(box.yaw_deg)


def front_from_viewers(box: Box, camera_xz: np.ndarray) -> np.ndarray:
    """The side the photos mostly looked at it from: the direction (x, z) from the box toward the mean camera position."""
    d = np.asarray(camera_xz, float).mean(axis=0) - np.array([box.centre_m[0], box.centre_m[2]])
    n = float(np.linalg.norm(d))
    return d / n if n > 1e-6 else forward_xz(box.yaw_deg)
