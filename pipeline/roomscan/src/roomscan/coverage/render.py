"""The coverage map (top-down) and the unfolded walls, drawn with matplotlib (no display needed)."""

from __future__ import annotations

import io
import math
from pathlib import Path
from typing import Any

import numpy as np

from ..paths import OutputGuard
from .grid import CELL_M, GOOD_VIEWS, WALLS
from .visibility import LOW_CAMERA_M

COLOURS = {
    "good": "#3f7fbf",  # three or more photos
    "thin": "#f0b545",  # one or two
    "unseen": "#d6544c",  # none
    "blocked": "#d4d4d4",  # hidden behind furniture: no photo could see it
    "opening": "#c3b6e2",  # photos see past it: a doorway, a window, an open garage door
    "outside": "#ffffff",
    "ink": "#1f2a36",
    "camera": "#7a8591",
    "camera_low": "#2f9e6e",
    "object": "#5b3f8c",
    "marker": "#1f2a36",
}
STATE_ORDER = ("unseen", "thin", "good", "blocked", "opening")  # grid.state() values 0 to 4
LANE_M = 0.16
DPI = 110
# The ceiling grid is drawn like the floor map: +x to the right, wall A at the top. That is the room
# as seen from above, not as seen by someone lying on the floor looking up (which is mirrored).
CEILING_TITLE = "Ceiling, drawn like the map: as if seen from above, wall A at the top"
# Where on a wall's strip a numbered step is marked, by height band (metres up the wall).
BAND_MARK_M = {"low": 0.3, "middle": 1.1, "high": 1.9}


def _plt():
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    return plt


def _rgb(name: str) -> np.ndarray:
    h = COLOURS[name].lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)])


def state_image(state: np.ndarray) -> np.ndarray:
    lut = np.stack([_rgb(n) for n in STATE_ORDER])
    return lut[state]


def band_states(grid, axes_len: float) -> list[np.ndarray]:
    """Per wall column, the state of the low, middle and high bands (worst-covered majority)."""
    rows, cols = grid.views.shape
    out = []
    for lo_m, hi_m in ((0.0, 0.6), (0.6, 1.6), (1.6, 99.0)):
        r0, r1 = int(lo_m / CELL_M), min(rows, int(math.ceil(hi_m / CELL_M)))
        st = np.full(cols, 3)
        if r0 < r1:
            vis = grid.visible[r0:r1]
            v = grid.views[r0:r1]
            n_vis = vis.sum(0)
            good = (grid.good[r0:r1] & vis).sum(0)
            seen = ((v > 0) & vis).sum(0)
            hidden_state = np.where(grid.opening[r0:r1].sum(0) > grid.blocked[r0:r1].sum(0), 4, 3)
            st = np.where(n_vis == 0, hidden_state,
                          np.where(good >= 0.5 * n_vis, 2, np.where(seen >= 0.5 * n_vis, 1, 0)))
        out.append(st)
    return out


def free_spot(x: float, z: float, taken: list[tuple[float, float]], *, gap: float = 0.32) -> tuple[float, float]:
    """The nearest spot to (x, z) at least ``gap`` from every marker already drawn."""
    for ring in range(0, 6):
        steps = 1 if ring == 0 else 8 * ring
        for k in range(steps):
            a = 2 * math.pi * k / steps
            cx, cz = x + ring * gap * math.cos(a), z + ring * gap * math.sin(a)
            if all(math.hypot(cx - tx, cz - tz) >= gap for tx, tz in taken):
                return cx, cz
    return x, z


def _save(fig, guard: OutputGuard, rel: Path) -> Path:
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=DPI, metadata={"Software": None})
    _plt().close(fig)
    return guard.write_bytes(rel, buf.getvalue())


def render_map(guard: OutputGuard, rel: Path, context: dict[str, Any]) -> Path:
    plt = _plt()
    from matplotlib.patches import FancyArrowPatch, Patch, Rectangle
    from matplotlib.lines import Line2D

    frame = context["frame"]
    floor = context["floor"]
    walls = context["walls"]
    axes_all = context["wall_axes"]
    guidance = context.get("guidance") or {"items": [], "walls": {}}
    x0, x1, z0, z1 = frame.x_min, frame.x_max, frame.z_min, frame.z_max
    pad = 3 * LANE_M + 0.9
    w, d = x1 - x0, z1 - z0
    fig_w = 11.0
    fig_h = fig_w * (d + 2 * pad) / (w + 2 * pad) + 1.6
    fig, ax = plt.subplots(figsize=(fig_w, min(fig_h, 16)))
    ax.set_facecolor(COLOURS["outside"])

    # Floor cells.
    fx0, fx1, fz0, fz1 = floor.extent
    rows, cols = floor.views.shape
    img = state_image(floor.state())
    ax.imshow(img, extent=(fx0, fx0 + cols * CELL_M, fz0 + rows * CELL_M, fz0), interpolation="nearest", zorder=1)
    if floor.open_below is not None:
        for r, c in zip(*np.nonzero(floor.open_below & ~floor.good)):
            ax.add_patch(Rectangle((fx0 + c * CELL_M, fz0 + r * CELL_M), CELL_M, CELL_M, fill=False, hatch="....",
                                   edgecolor=COLOURS["ink"], linewidth=0, alpha=0.5, zorder=2))
    # Cell grid lines, faint.
    for c in range(cols + 1):
        ax.plot([fx0 + c * CELL_M] * 2, [fz0, fz0 + rows * CELL_M], color="white", lw=0.3, zorder=2)
    for r in range(rows + 1):
        ax.plot([fx0, fx0 + cols * CELL_M], [fz0 + r * CELL_M] * 2, color="white", lw=0.3, zorder=2)

    # Wall strips: three lanes outside the floor, inner = near the floor, outer = near the ceiling.
    for key, grid in walls.items():
        axes = axes_all[key]
        lanes = band_states(grid, axes["length"])
        out = -np.asarray(axes["inward"])
        right = np.asarray(axes["right"])
        for lane, states in enumerate(lanes):
            for c, s in enumerate(states):
                u0 = c * CELL_M
                u1 = min((c + 1) * CELL_M, axes["length"])
                if u1 <= u0:
                    continue
                p0 = axes["left_end"] + right * u0 + out * (0.03 + lane * LANE_M)
                p1 = axes["left_end"] + right * u1 + out * (0.03 + (lane + 1) * LANE_M)
                xs, zs = sorted([p0[0], p1[0]]), sorted([p0[1], p1[1]])
                ax.add_patch(Rectangle((xs[0], zs[0]), xs[1] - xs[0], zs[1] - zs[0], facecolor=COLOURS[STATE_ORDER[s]],
                                       edgecolor="white", linewidth=0.3, zorder=3))
        mid = axes["left_end"] + right * axes["length"] / 2 + out * (3 * LANE_M + 0.32)
        prof = guidance.get("walls", {}).get(key, {})
        name = prof.get("name", "")
        label = f"Wall {key}" + (f"\n{name}" if name and name != f"wall {key}" else "")
        rot = 0 if WALLS[key][0] == "z" else (90 if key == "D" else -90)
        ax.text(mid[0], mid[1], label, ha="center", va="center", fontsize=9, rotation=rot, color=COLOURS["ink"],
                fontweight="bold", zorder=6)
    ax.plot([x0, x1, x1, x0, x0], [z0, z0, z1, z1, z0], color=COLOURS["ink"], lw=1.2, zorder=4)

    # Objects.
    for o in context.get("objects") or []:
        ax.add_patch(Rectangle((o.lo[0], o.lo[2]), o.size[0], o.size[2], fill=False, edgecolor=COLOURS["object"],
                               linewidth=1.4, zorder=5))
        ax.text(o.centre[0], o.centre[2], o.label, ha="center", va="center", fontsize=7, color=COLOURS["object"],
                zorder=6, bbox={"boxstyle": "round,pad=0.15", "fc": "white", "ec": "none", "alpha": 0.75})

    # Where the photos were taken from.
    cams = context["cams"]
    looks = context["looks"]
    for v in context["registered"]:
        c, lk = cams[v], looks[v]
        colour = COLOURS["camera_low"] if c[1] < LOW_CAMERA_M else COLOURS["camera"]
        ax.plot(c[0], c[2], "o", ms=2.6, color=colour, zorder=7)
        n = math.hypot(lk[0], lk[2])
        if n > 0.2:
            ax.plot([c[0], c[0] + 0.22 * lk[0] / n], [c[2], c[2] + 0.22 * lk[2] / n], color=colour, lw=0.7, zorder=7)

    # Numbered guidance.
    placed: list[tuple[float, float]] = []
    for it in guidance.get("items", []):
        if it.get("target") is None:
            continue
        tx, tz = it["target"]
        sx, sz = it["stand"] if it.get("stand") is not None else (tx, tz)
        px, pz = free_spot(sx, sz, placed)
        placed.append((px, pz))
        if it.get("stand") is not None:
            ax.add_patch(FancyArrowPatch((px, pz), (tx, tz), arrowstyle="-|>", mutation_scale=11, lw=1.3,
                                         color=COLOURS["marker"], shrinkA=7, shrinkB=4, zorder=8))
        ax.plot(px, pz, "o", ms=15, color=COLOURS["marker"], zorder=9)
        ax.text(px, pz, str(it["number"]), ha="center", va="center", color="white", fontsize=8, fontweight="bold",
                zorder=10)

    # Scale bar.
    sb_x, sb_z = x0, z1 + 3 * LANE_M + 0.65
    ax.plot([sb_x, sb_x + 1.0], [sb_z, sb_z], color=COLOURS["ink"], lw=2.5, zorder=6)
    ax.text(sb_x + 0.5, sb_z + 0.08, "1 metre", ha="center", va="top", fontsize=8, color=COLOURS["ink"])

    ax.set_xlim(x0 - pad, x1 + pad)
    ax.set_ylim(z1 + pad, z0 - pad)  # +z runs down the page: wall A at the top
    ax.set_aspect("equal")
    ax.axis("off")
    first = context["views"][min(context["registered"])]["name"] if context["registered"] else "?"
    ax.set_title(f"What the photos cover, seen from above ({w:.1f} m x {d:.1f} m)\n"
                 f"Wall A is at the top: the wall you faced in your first photo, {first}", fontsize=11,
                 color=COLOURS["ink"])
    handles = [Patch(color=COLOURS["good"], label=f"{GOOD_VIEWS}+ photos, different spots"),
               Patch(color=COLOURS["thin"], label="1-2 photos, or all from one spot"),
               Patch(color=COLOURS["unseen"], label="no photo"),
               Patch(color=COLOURS["blocked"], label="furniture stands there"),
               Patch(color=COLOURS["opening"], label="photos see through (door, window)"),
               Line2D([], [], marker="o", ls="", color=COLOURS["camera"], label="photo taken standing"),
               Line2D([], [], marker="o", ls="", color=COLOURS["camera_low"], label=f"photo taken low (<{LOW_CAMERA_M:.1f} m)"),
               Patch(fill=False, edgecolor=COLOURS["object"], label="furniture found"),
               Line2D([], [], marker="o", ls="", ms=10, color=COLOURS["marker"], label="next photos (see list)")]
    ax.legend(handles=handles, loc="upper center", bbox_to_anchor=(0.5, -0.01), ncol=3, fontsize=8, frameon=False)
    fig.text(0.5, 0.005, "Wall strips: inner lane = near the floor, middle = waist to chest, outer = up high.",
             ha="center", fontsize=8, color=COLOURS["ink"])
    fig.tight_layout()
    return _save(fig, guard, rel)


def wall_markers(guidance: dict[str, Any], wall: str, height_m: float) -> list[tuple[float, float, int]]:
    """(metres along the wall, metres up, step number) for every step that points at this wall.

    Both kinds of wall step are marked: the ordinary ones and the ones for the bottom of a wall
    that no photo saw from low down.
    """
    marks = []
    for it in guidance.get("items", []):
        if it.get("kind") in ("wall", "low_wall") and it.get("wall") == wall:
            u0, u1 = it["span_m"]
            v = float(np.mean([min(BAND_MARK_M[b], height_m - 0.2) for b in it["bands"]]))
            marks.append(((u0 + u1) / 2, v, it["number"]))
    return marks


def render_walls(guard: OutputGuard, rel: Path, context: dict[str, Any]) -> Path:
    plt = _plt()
    from matplotlib.patches import Patch, Rectangle

    walls = context["walls"]
    axes_all = context["wall_axes"]
    guidance = context.get("guidance") or {"items": [], "walls": {}}
    ceiling = context.get("ceiling")
    panels = list(WALLS) + (["ceiling"] if ceiling is not None else [])
    fig, axs = plt.subplots(len(panels), 1, figsize=(10, 2.3 * len(panels) + 0.8))
    axs = np.atleast_1d(axs)
    for ax, key in zip(axs, panels):
        if key == "ceiling":
            g = ceiling
            u0, u1, v0, v1 = g.extent
            rows, cols = g.views.shape
            ax.imshow(state_image(g.state()), extent=(u0, u0 + cols * CELL_M, v0 + rows * CELL_M, v0),
                      interpolation="nearest")
            ax.set_title(CEILING_TITLE, fontsize=9, loc="left")
            ax.set_aspect("equal")
            ax.set_xticks([])
            ax.set_yticks([])
            continue
        g = walls[key]
        axes = axes_all[key]
        rows, cols = g.views.shape
        ax.imshow(state_image(g.state()), extent=(0, cols * CELL_M, 0, rows * CELL_M), origin="lower",
                  interpolation="nearest")
        for o in context.get("objects") or []:
            if o.wall != key:
                continue
            corners = np.array([[o.lo[0], o.lo[2]], [o.lo[0], o.hi[2]], [o.hi[0], o.lo[2]], [o.hi[0], o.hi[2]]])
            u = (corners - axes["left_end"]) @ axes["right"]
            ax.add_patch(Rectangle((u.min(), o.lo[1]), u.max() - u.min(), o.size[1], fill=False,
                                   edgecolor=COLOURS["object"], lw=1.3))
            ax.text((u.min() + u.max()) / 2, o.hi[1] + 0.05, o.label, ha="center", va="bottom", fontsize=7,
                    color=COLOURS["object"])
        for u, v, number in wall_markers(guidance, key, rows * CELL_M):
            ax.plot(u, v, "o", ms=14, color=COLOURS["marker"])
            ax.text(u, v, str(number), ha="center", va="center", color="white", fontsize=8, fontweight="bold")
        prof = guidance.get("walls", {}).get(key, {})
        ax.set_title(f"Wall {key} ({axes['label']}), as you face it: {prof.get('name', '')}", fontsize=9, loc="left")
        ax.set_xlabel("metres from the left corner", fontsize=8)
        ax.set_ylabel("height (m)", fontsize=8)
        ax.set_aspect("equal")
        ax.tick_params(labelsize=7)
    handles = [Patch(color=COLOURS["good"], label=f"{GOOD_VIEWS}+ photos, different spots"),
               Patch(color=COLOURS["thin"], label="1-2 photos, or one spot"),
               Patch(color=COLOURS["unseen"], label="no photo"), Patch(color=COLOURS["blocked"], label="furniture stands there"),
               Patch(color=COLOURS["opening"], label="photos see through (door, window)")]
    fig.legend(handles=handles, loc="lower center", ncol=5, fontsize=8, frameon=False)
    fig.tight_layout(rect=(0, 0.03, 1, 1))
    return _save(fig, guard, rel)
