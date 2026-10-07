"""Coverage grids over the floor, the four walls and the ceiling (25 cm cells).

Each cell's state comes from ``visibility``: how many fitted photos see it, how many see something
in front of it, and how many see past it. Cells hidden behind furniture are *blocked* rather than
missing (no photo can see the wall behind a shelving unit, and the guidance must not ask for one),
and cells photos keep looking through are an *opening* (a doorway, a window, an open garage door).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

from .visibility import Views, cell_samples, classify

GOOD_VIEWS = 3
CELL_M = 0.25

# Map orientation: looking down, +x to the right and +z toward the bottom of the image.
WALLS = {
    # key: (axis index of the normal, side, inward normal (x, z), label)
    "A": ("z", "min", (0.0, 1.0), "top edge of the map"),
    "B": ("x", "max", (-1.0, 0.0), "right edge of the map"),
    "C": ("z", "max", (0.0, -1.0), "bottom edge of the map"),
    "D": ("x", "min", (1.0, 0.0), "left edge of the map"),
}
# How far in front of / behind the fitted plane a photo's depth may land and still be "this surface",
# and how close an obstruction must be to count as furniture on the cell rather than clutter between.
# Floors get more room behind the plane: garage floors slope toward the door for drainage.
TOLERANCE = {"floor": (0.08, 0.15, 0.45), "wall": (0.25, 0.15, 0.8), "ceiling": (0.15, 0.15, 0.45)}
# Three photos all taken from one spot do not let a surface be rebuilt: the directions they see a
# cell from must spread a little. 0.985 is the mean resultant length of directions about 10 degrees
# apart on average (three photos a step apart, two to three metres away).
SPREAD_MAX = 0.985


@dataclass
class SurfaceGrid:
    name: str
    views: np.ndarray  # (rows, cols) number of distinct photos seeing the cell
    blocked: np.ndarray  # (rows, cols) bool: hidden behind something nearer
    extent: tuple[float, float, float, float]  # u0, u1, v0, v1 in metres
    view_sets: list[list[set[int]]] = field(default_factory=list)
    opening: np.ndarray | None = None  # (rows, cols) bool: photos see past the surface here
    hidden_by: np.ndarray | None = None  # (rows, cols) photos that saw furniture on or against it
    blocked_far: np.ndarray | None = None  # (rows, cols) photos blocked by clutter further away
    seen_through: np.ndarray | None = None  # (rows, cols) photos that saw past it
    low_views: np.ndarray | None = None  # (rows, cols) photos from below knee height that see it
    spread: np.ndarray | None = None  # (rows, cols) 1 = every photo from the same direction
    # Floor only: blocked cells with open space underneath (a desk, the bottom of a shelf), where a
    # knee-height photo could see the floor.
    open_below: np.ndarray | None = None

    def __post_init__(self) -> None:
        if self.opening is None:
            self.opening = np.zeros_like(self.blocked, bool)
        if self.spread is None:
            self.spread = np.zeros(self.views.shape)
        if self.low_views is None:
            self.low_views = np.zeros_like(self.views)
        if self.blocked_far is None:
            self.blocked_far = np.zeros_like(self.views)

    @property
    def visible(self) -> np.ndarray:
        return ~self.blocked & ~self.opening

    @property
    def good(self) -> np.ndarray:
        """Seen by enough photos, from more than one direction."""
        return (self.views >= GOOD_VIEWS) & (self.spread <= SPREAD_MAX)

    def stats(self) -> dict[str, Any]:
        total = self.views.size
        blocked = int(self.blocked.sum())
        opening = int(self.opening.sum())
        visible = self.visible
        good = int((self.good & visible).sum())
        thin = int(((self.views > 0) & ~self.good & visible).sum())
        unseen = int(((self.views == 0) & visible).sum())
        seeable = max(1, total - blocked - opening)
        return {
            "cells": total,
            "cell_m": CELL_M,
            "blocked": blocked,
            "opening": opening,
            "good": good,
            "thin": thin,
            "unseen": unseen,
            "good_pct": round(100 * good / seeable, 1),
            "thin_pct": round(100 * thin / seeable, 1),
            "unseen_pct": round(100 * unseen / seeable, 1),
            "median_views": float(np.median(self.views[visible])) if visible.any() else 0.0,
            "low_seen_pct": round(100 * int(((self.low_views > 0) & visible).sum()) / seeable, 1),
            "blocked_from_afar": int(((self.blocked_far >= 2) & ~self.good & visible).sum()),
        }

    def state(self) -> np.ndarray:
        """0 unseen, 1 thin, 2 good, 3 blocked, 4 opening."""
        s = np.where(self.good, 2, np.where(self.views > 0, 1, 0))
        s = np.where(self.blocked, 3, s)
        return np.where(self.opening, 4, s)


def _states(vis, extent, name: str, *, openings: bool = True) -> SurfaceGrid:
    """Blocked only by furniture on or against the cell; clutter further away leaves a gap.

    ``openings`` is off for the floor: nothing is seen through a floor, so depth beyond it means
    the floor is not quite flat there, not a doorway.
    """
    seen, covered, through = vis.seen, vis.covered, vis.through
    weak = seen < GOOD_VIEWS
    blocked = weak & (covered >= 2) & (covered >= 2 * seen + 1) & (covered >= through)
    opening = weak & ~blocked & (through >= 2) & (through >= 2 * seen + 1) & (through > covered)
    if not openings:
        opening = np.zeros_like(opening)
    return SurfaceGrid(name, seen, blocked, extent, vis.view_sets, opening=opening, hidden_by=covered,
                       blocked_far=vis.blocked_far, seen_through=through, low_views=vis.low_seen,
                       spread=vis.spread)


def _shape(extent, cell: float) -> tuple[int, int]:
    u0, u1, v0, v1 = extent
    return max(1, int(np.ceil((v1 - v0) / cell - 1e-9))), max(1, int(np.ceil((u1 - u0) / cell - 1e-9)))


def _occupancy(u: np.ndarray, v: np.ndarray, extent, cell: float, min_points: int) -> np.ndarray:
    u0, u1, v0, v1 = extent
    rows, cols = _shape(extent, cell)
    ci = np.floor((u - u0) / cell).astype(int)
    ri = np.floor((v - v0) / cell).astype(int)
    inside = (ci >= 0) & (ci < cols) & (ri >= 0) & (ri < rows)
    grid = np.zeros((rows, cols), int)
    np.add.at(grid, (ri[inside], ci[inside]), 1)
    return grid >= min_points


def floor_grid(views: Views, xyz: np.ndarray, bounds, *, cell: float = CELL_M) -> SurfaceGrid:
    x0, x1, z0, z1 = bounds
    extent = (x0, x1, z0, z1)
    rows, cols = _shape(extent, cell)
    samples = cell_samples(np.array([x0, 0.0, z0]), np.array([1.0, 0, 0]), np.array([0, 0, 1.0]), rows, cols, cell)
    front, back, near = TOLERANCE["floor"]
    grid = _states(classify(samples, np.array([0.0, 1.0, 0.0]), views, front_slack=front, back_tol=back,
                            near_m=near), extent, "floor", openings=False)
    # Blocked cells with a surface above them but nothing low: the space under a desk or a shelf.
    low = (xyz[:, 1] > 0.10) & (xyz[:, 1] < 0.35)
    low_occ = _occupancy(xyz[low, 0], xyz[low, 2], extent, cell, 15)
    above = (xyz[:, 1] >= 0.35) & (xyz[:, 1] < 1.2)
    high_occ = _occupancy(xyz[above, 0], xyz[above, 2], extent, cell, 15)
    grid.open_below = grid.blocked & high_occ & ~low_occ
    return grid


def ceiling_grid(views: Views, bounds, ceiling_y: float, *, cell: float = CELL_M) -> SurfaceGrid:
    x0, x1, z0, z1 = bounds
    extent = (x0, x1, z0, z1)
    rows, cols = _shape(extent, cell)
    samples = cell_samples(np.array([x0, ceiling_y, z0]), np.array([1.0, 0, 0]), np.array([0, 0, 1.0]), rows, cols,
                           cell)
    front, back, near = TOLERANCE["ceiling"]
    return _states(classify(samples, np.array([0.0, -1.0, 0.0]), views, front_slack=front, back_tol=back,
                            near_m=near), extent, "ceiling")


def wall_axes(key: str, bounds) -> dict[str, Any]:
    """Coordinates along a wall as seen from inside the room: u runs left to right, v is height."""
    x0, x1, z0, z1 = bounds
    axis, side, inward, label = WALLS[key]
    inward = np.array(inward)
    # Facing the wall means looking along -inward; with +Y up the viewer's right is facing x up,
    # which in (x, z) is (-facing_z, facing_x).
    facing = -inward
    right = np.array([-facing[1], facing[0]])
    if axis == "x":
        plane = x1 if side == "max" else x0
    else:
        plane = z1 if side == "max" else z0
    # Endpoints of the wall in xz, ordered from the viewer's left to right.
    if axis == "x":
        ends = [np.array([plane, z0]), np.array([plane, z1])]
    else:
        ends = [np.array([x0, plane]), np.array([x1, plane])]
    if (ends[1] - ends[0]) @ right < 0:
        ends.reverse()
    length = float(np.linalg.norm(ends[1] - ends[0]))
    return {"key": key, "axis": axis, "plane": float(plane), "inward": inward, "right": right,
            "left_end": ends[0], "right_end": ends[1], "length": length, "label": label}


def wall_grid(key: str, views: Views, bounds, height: float, *, cell: float = CELL_M) -> SurfaceGrid:
    w = wall_axes(key, bounds)
    extent = (0.0, w["length"], 0.0, height)
    rows, cols = _shape(extent, cell)
    origin = np.array([w["left_end"][0], 0.0, w["left_end"][1]])
    u_dir = np.array([w["right"][0], 0.0, w["right"][1]])
    normal = np.array([w["inward"][0], 0.0, w["inward"][1]])
    front, back, near = TOLERANCE["wall"]
    vis = classify(cell_samples(origin, u_dir, np.array([0.0, 1.0, 0.0]), rows, cols, cell), normal, views,
                   front_slack=front, back_tol=back, near_m=near)
    return _states(vis, extent, f"wall {key}")


def runs(mask: np.ndarray) -> list[tuple[int, int]]:
    """[start, end) index runs where a 1-D boolean mask is True."""
    out, start = [], None
    for i, m in enumerate(list(mask) + [False]):
        if m and start is None:
            start = i
        elif not m and start is not None:
            out.append((start, i))
            start = None
    return out


def regions(mask: np.ndarray) -> list[list[tuple[int, int]]]:
    """4-connected regions of a 2-D boolean mask, largest first."""
    rows, cols = mask.shape
    seen = np.zeros_like(mask, bool)
    found = []
    for r in range(rows):
        for c in range(cols):
            if not mask[r, c] or seen[r, c]:
                continue
            stack, cells = [(r, c)], []
            seen[r, c] = True
            while stack:
                y, x = stack.pop()
                cells.append((y, x))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < rows and 0 <= nx < cols and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            found.append(cells)
    found.sort(key=len, reverse=True)
    return found
