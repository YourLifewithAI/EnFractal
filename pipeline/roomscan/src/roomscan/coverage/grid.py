"""Coverage grids over the floor, the four walls and the ceiling.

A cell counts as *seen by a photo* when that photo contributes at least ``min_points`` of its
sampled surface points to the cell. Cells hidden behind furniture are marked *blocked* rather than
missing: no photo can see the wall behind a shelving unit, and the guidance must not ask for one.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

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


@dataclass
class SurfaceGrid:
    name: str
    views: np.ndarray  # (rows, cols) number of distinct photos seeing the cell
    blocked: np.ndarray  # (rows, cols) bool: hidden behind something nearer
    extent: tuple[float, float, float, float]  # u0, u1, v0, v1 in metres
    view_sets: list[list[set[int]]] = field(default_factory=list)
    # Floor only: blocked cells with open space underneath (a desk, the bottom of a shelf), where a
    # knee-height photo could see the floor.
    open_below: np.ndarray | None = None

    def stats(self) -> dict[str, Any]:
        total = self.views.size
        blocked = int(self.blocked.sum())
        visible = ~self.blocked
        good = int(((self.views >= GOOD_VIEWS) & visible).sum())
        thin = int(((self.views > 0) & (self.views < GOOD_VIEWS) & visible).sum())
        unseen = int(((self.views == 0) & visible).sum())
        seeable = max(1, total - blocked)
        return {
            "cells": total,
            "cell_m": CELL_M,
            "blocked": blocked,
            "good": good,
            "thin": thin,
            "unseen": unseen,
            "good_pct": round(100 * good / seeable, 1),
            "thin_pct": round(100 * thin / seeable, 1),
            "unseen_pct": round(100 * unseen / seeable, 1),
            "median_views": float(np.median(self.views[visible])) if visible.any() else 0.0,
        }

    def state(self) -> np.ndarray:
        """0 unseen, 1 thin, 2 good, 3 blocked."""
        s = np.where(self.views >= GOOD_VIEWS, 2, np.where(self.views > 0, 1, 0))
        return np.where(self.blocked & (self.views < GOOD_VIEWS), 3, s)


def _count_views(u: np.ndarray, v: np.ndarray, owner: np.ndarray, extent, cell: float,
                 min_points: int) -> tuple[np.ndarray, list[list[set[int]]]]:
    u0, u1, v0, v1 = extent
    cols = max(1, int(np.ceil((u1 - u0) / cell)))
    rows = max(1, int(np.ceil((v1 - v0) / cell)))
    ci = np.floor((u - u0) / cell).astype(int)
    ri = np.floor((v - v0) / cell).astype(int)
    inside = (ci >= 0) & (ci < cols) & (ri >= 0) & (ri < rows)
    ci, ri, ow = ci[inside], ri[inside], owner[inside]
    sets: list[list[set[int]]] = [[set() for _ in range(cols)] for _ in range(rows)]
    counts = np.zeros((rows, cols), int)
    if len(ci):
        key = (ri * cols + ci).astype(np.int64) * 100000 + ow.astype(np.int64)
        uniq, n = np.unique(key, return_counts=True)
        for k, c in zip(uniq, n):
            if c < min_points:
                continue
            cell_id, view = divmod(int(k), 100000)
            r, cc = divmod(cell_id, cols)
            sets[r][cc].add(view)
    for r in range(rows):
        for c in range(cols):
            counts[r, c] = len(sets[r][c])
    return counts, sets


def _occupancy(u: np.ndarray, v: np.ndarray, extent, cell: float, min_points: int) -> np.ndarray:
    u0, u1, v0, v1 = extent
    cols = max(1, int(np.ceil((u1 - u0) / cell)))
    rows = max(1, int(np.ceil((v1 - v0) / cell)))
    ci = np.floor((u - u0) / cell).astype(int)
    ri = np.floor((v - v0) / cell).astype(int)
    inside = (ci >= 0) & (ci < cols) & (ri >= 0) & (ri < rows)
    grid = np.zeros((rows, cols), int)
    np.add.at(grid, (ri[inside], ci[inside]), 1)
    return grid >= min_points


def floor_grid(xyz: np.ndarray, nor: np.ndarray, owner: np.ndarray, bounds, *, cell: float = CELL_M,
               min_points: int = 8) -> SurfaceGrid:
    x0, x1, z0, z1 = bounds
    on_floor = (np.abs(xyz[:, 1]) < 0.06) & (nor[:, 1] > 0.7)
    counts, sets = _count_views(xyz[on_floor, 0], xyz[on_floor, 2], owner[on_floor], (x0, x1, z0, z1), cell, min_points)
    above = (xyz[:, 1] > 0.10) & (xyz[:, 1] < 1.9)
    blocked = _occupancy(xyz[above, 0], xyz[above, 2], (x0, x1, z0, z1), cell, 40)
    low = (xyz[:, 1] > 0.10) & (xyz[:, 1] < 0.35)
    low_occ = _occupancy(xyz[low, 0], xyz[low, 2], (x0, x1, z0, z1), cell, 15)
    return SurfaceGrid("floor", counts, blocked, (x0, x1, z0, z1), sets, open_below=blocked & ~low_occ)


def ceiling_grid(xyz: np.ndarray, nor: np.ndarray, owner: np.ndarray, bounds, ceiling_y: float, *,
                 cell: float = CELL_M, min_points: int = 8) -> SurfaceGrid:
    x0, x1, z0, z1 = bounds
    near = (np.abs(xyz[:, 1] - ceiling_y) < 0.08) & (nor[:, 1] < -0.6)
    counts, sets = _count_views(xyz[near, 0], xyz[near, 2], owner[near], (x0, x1, z0, z1), cell, min_points)
    return SurfaceGrid("ceiling", counts, np.zeros_like(counts, bool), (x0, x1, z0, z1), sets)


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


def wall_grid(key: str, xyz: np.ndarray, nor: np.ndarray, owner: np.ndarray, bounds, height: float, *,
              cell: float = CELL_M, min_points: int = 8) -> SurfaceGrid:
    w = wall_axes(key, bounds)
    a = 0 if w["axis"] == "x" else 2
    inward_n = w["inward"][0] if a == 0 else w["inward"][1]
    dist = (xyz[:, a] - w["plane"]) * np.sign(inward_n)  # distance into the room
    rel = xyz[:, [0, 2]] - w["left_end"]
    u = rel @ w["right"]
    v = xyz[:, 1]
    on_wall = (np.abs(dist) < 0.10) & (nor[:, a] * np.sign(inward_n) > 0.6)
    extent = (0.0, w["length"], 0.0, height)
    counts, sets = _count_views(u[on_wall], v[on_wall], owner[on_wall], extent, cell, min_points)
    front = (dist > 0.12) & (dist < 0.9)
    blocked = _occupancy(u[front], v[front], extent, cell, 40)
    return SurfaceGrid(f"wall {key}", counts, blocked, extent, sets)


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
