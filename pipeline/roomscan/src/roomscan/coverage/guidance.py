"""Turn coverage numbers into specific capture instructions in plain language.

Every instruction says what to photograph, from where, at what height, how far away and how
many photos, and names places by what is there (objects on a wall, the photo the founder took
nearby) as well as by the map letters. Instructions are ranked: the floor and the lower walls
matter most to a 10 cm player, so gaps there come first.

Object labels come from a fixed vocabulary (``objects.VOCABULARY``), never from text in a photo.
"""

from __future__ import annotations

import math
from typing import Any

import numpy as np

from ..exif import digitally_zoomed
from .grid import CELL_M, regions, runs
from .visibility import LOW_CAMERA_M
from .objects import ObjectInstance, sector_of

# Height bands on walls: (key, from m, to m, how to hold the phone)
BANDS = (
    ("low", 0.0, 0.6, "near the floor, with the phone at knee height (about 40 cm) pointing straight at the wall"),
    ("middle", 0.6, 1.6, "at waist-to-chest height, phone held upright and level"),
    ("high", 1.6, 9.9, "up high, phone at head height tilted up toward where the wall meets the ceiling"),
)
BAND_WEIGHT = {"low": 1.6, "middle": 1.0, "high": 0.6}
NUMBER_WORDS = {1: "one", 2: "two", 3: "three", 4: "four", 5: "five", 6: "six", 7: "seven", 8: "eight",
                9: "nine", 10: "ten", 11: "eleven", 12: "twelve"}
# Furniture that stands on legs or has a gap under its lowest shelf: room for a 10 cm player.
LEGGED = {"desk", "table", "workbench", "shelving unit", "chair", "office chair", "easel", "sofa", "cabinet",
          "chest of drawers", "ladder"}
MAX_MAIN_ITEMS = 12
# Labels that name a wall well ("the garage-door wall") when they sit on it.
WALL_LANDMARKS = ("garage door", "door", "window", "shelving unit", "workbench", "painting", "cabinet",
                  "refrigerator", "washing machine", "mirror", "television")


def words(n: int) -> str:
    return NUMBER_WORDS.get(n, str(n))


def metres(d: float) -> str:
    """Distances rounded to what a person can pace out."""
    d = max(0.5, round(d * 2) / 2)
    named = {0.5: "half a metre", 1.0: "one metre", 1.5: "one and a half metres", 2.0: "two metres",
             2.5: "two and a half metres", 3.0: "three metres"}
    return named.get(d, f"{d:g} metres")


def plural(n: int, one: str, many: str | None = None) -> str:
    return one if n == 1 else (many or one + "s")


def _join(items: list[str]) -> str:
    items = [i for i in items if i]
    if len(items) <= 1:
        return "".join(items)
    return ", ".join(items[:-1]) + " and " + items[-1]


# --- naming places -----------------------------------------------------------------------------

def object_u_range(inst: ObjectInstance, axes: dict[str, Any]) -> tuple[float, float]:
    """The stretch of a wall (metres from its left end, facing it) an object covers."""
    corners = np.array([[inst.lo[0], inst.lo[2]], [inst.lo[0], inst.hi[2]], [inst.hi[0], inst.lo[2]], [inst.hi[0], inst.hi[2]]])
    u = (corners - axes["left_end"]) @ axes["right"]
    return float(u.min()), float(u.max())


def wall_profiles(context: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """For each wall: a plain name, the objects on it and the photo that shows most of it."""
    objects: list[ObjectInstance] = context.get("objects") or []
    walls = context["walls"]
    axes_all = context["wall_axes"]
    registered = set(context["registered"])
    views = context["views"]
    first = min(registered) if registered else None
    out: dict[str, dict[str, Any]] = {}
    for key, grid in walls.items():
        axes = axes_all[key]
        on_wall = [o for o in objects if o.wall == key]
        on_wall.sort(key=lambda o: -(object_u_range(o, axes)[1] - object_u_range(o, axes)[0]))
        labels: list[str] = []
        for landmark in WALL_LANDMARKS:
            if any(o.label == landmark for o in on_wall) and landmark not in labels:
                labels.append(landmark)
        for o in on_wall:
            if o.label not in labels:
                labels.append(o.label)
        # The photo that sees the most cells of this wall.
        counts: dict[int, int] = {}
        for row in grid.view_sets:
            for cell in row:
                for v in cell:
                    if v in registered:
                        counts[v] = counts.get(v, 0) + 1
        best = max(counts, key=lambda v: (counts[v], -v)) if counts else None
        faced_first = False
        if first is not None and first in context["looks"]:
            look = context["looks"][first]
            faced_first = float(np.array([look[0], look[2]]) @ -axes["inward"]) > 0.7
        if "garage door" in labels:
            name = "the garage-door wall"
        elif labels:
            name = "the wall with the " + _join([f"{lab}" for lab in labels[:2]]).replace(" and ", " and the ")
        else:
            name = f"wall {key}"
        out[key] = {"key": key, "name": name, "objects": on_wall, "labels": labels,
                    "best_photo": views[best]["name"] if best is not None else None,
                    "faced_first": faced_first, "map_side": axes["label"], "length_m": axes["length"]}
    # Two walls must never share a name.
    seen: dict[str, int] = {}
    for prof in out.values():
        seen[prof["name"]] = seen.get(prof["name"], 0) + 1
    for key, prof in out.items():
        if seen[prof["name"]] > 1:
            prof["name"] = f"{prof['name']} ({prof['map_side']})"
    return out


def wall_ref(prof: dict[str, Any]) -> str:
    """'the wall with the shelving unit (wall D, left edge of the map)'."""
    if prof["name"] == f"wall {prof['key']}":
        return f"wall {prof['key']} ({prof['map_side']})"
    return f"{prof['name']} (wall {prof['key']}, {prof['map_side']})"


def wall_hint(prof: dict[str, Any]) -> str:
    bits = []
    if prof["faced_first"]:
        bits.append("the wall you faced in your first photo")
    if prof["best_photo"]:
        bits.append(f"it fills most of {prof['best_photo']}")
    if not bits:
        return ""
    text = "; ".join(bits)
    return text[0].upper() + text[1:] + "."


def span_phrase(u0: float, u1: float, prof: dict[str, Any], axes: dict[str, Any]) -> str:
    """Where a stretch of wall is, by the objects on it, or by its corners.

    "Around the X" is said only of an object that covers the middle of the stretch. An object that
    only touches one end is a neighbour: the stretch is to the right or left of it.
    """
    length = axes["length"]
    mid = (u0 + u1) / 2
    on_left, on_right, over = [], [], []  # objects over the middle, and the nearest ones either side of it
    for o in prof["objects"]:
        a, b = object_u_range(o, axes)
        if a <= mid <= b:
            over.append(o)
        elif b < mid:
            on_left.append((max(0.0, u0 - b), o))
        else:
            on_right.append((max(0.0, a - u1), o))
    near_l = min(on_left, key=lambda x: x[0])[1] if on_left else None
    near_r = min(on_right, key=lambda x: x[0])[1] if on_right else None
    where = f"from {u0:.1f} m to {u1:.1f} m along it, counting from its left corner as you face it"
    to_left_corner = u0 < 0.3
    to_right_corner = u1 > length - 0.3
    if to_left_corner and to_right_corner:
        return "along its whole length"
    if over:
        return f"around the {over[0].label} ({where})"
    if near_l is not None and near_r is not None:
        return f"between the {near_l.label} and the {near_r.label} ({where})"
    if near_r is not None:
        corner = ", up to the left-hand corner" if to_left_corner else ""
        return f"to the left of the {near_r.label}{corner} ({where})"
    if near_l is not None:
        corner = ", up to the right-hand corner" if to_right_corner else ""
        return f"to the right of the {near_l.label}{corner} ({where})"
    if to_left_corner:
        return f"at its left end ({where})"
    if to_right_corner:
        return f"at its right end ({where})"
    return f"in its middle ({where})"


MIN_HORIZONTAL_LOOK = 0.5  # a photo points at a wall or a piece of furniture only if this much of its look is level


def nearest_photo(context: dict[str, Any], stand_xz: np.ndarray, face_xz: np.ndarray | None = None) -> str | None:
    """The fitted photo taken closest to a spot, among those that faced the same way.

    When a direction is given, a photo whose view is mostly up or down (its level part is under
    ``MIN_HORIZONTAL_LOOK`` of a unit view) is not a candidate: its tiny level component says nothing
    about which wall it faced, and a photo of the floor is no help in finding a wall.
    """
    best, best_score = None, math.inf
    for v in context["registered"]:
        c = context["cams"][v]
        d = float(np.linalg.norm(c[[0, 2]] - stand_xz))
        if face_xz is not None:
            look = context["looks"][v][[0, 2]]
            n = float(np.linalg.norm(look))
            if n < MIN_HORIZONTAL_LOOK:
                continue
            d += 0.8 * (1 - float(look @ face_xz) / n)
        if d < best_score:
            best, best_score = v, d
    return context["views"][best]["name"] if best is not None and best_score < 2.0 else None


def place_phrase(context: dict[str, Any], xz: np.ndarray, exclude: ObjectInstance | None = None) -> str:
    """A spot on the floor described by nearby objects and walls."""
    objects = [o for o in (context.get("objects") or []) if o is not exclude]
    frame = context["frame"]
    near = []
    for o in objects:
        c = o.centre[[0, 2]]
        half = o.size[[0, 2]] / 2
        gap = np.maximum(np.abs(xz - c) - half, 0)
        near.append((float(np.linalg.norm(gap)), o))
    near.sort(key=lambda x: x[0])
    close = [o for d, o in near if d < 1.2][:2]
    walls_near = []
    gaps = {"A": xz[1] - frame.z_min, "B": frame.x_max - xz[0], "C": frame.z_max - xz[1], "D": xz[0] - frame.x_min}
    for k, g in sorted(gaps.items(), key=lambda kv: kv[1]):
        if g < 0.9:
            walls_near.append(k)
    if len(close) == 2 and close[0].label != close[1].label:
        return f"between the {close[0].label} and the {close[1].label}"
    if close:
        return f"next to the {close[0].label}"
    if len(walls_near) >= 2:
        return f"in the corner where walls {walls_near[0]} and {walls_near[1]} meet"
    if walls_near:
        return f"along wall {walls_near[0]}"
    return "in the open middle of the room"


# --- guidance items ----------------------------------------------------------------------------

def _item(kind: str, priority: float, photos: int, text: str, why: str, *, target=None, stand=None,
          near_photo: str | None = None, **extra) -> dict[str, Any]:
    out = {"kind": kind, "priority": round(float(priority), 3), "photos": int(photos), "text": text, "why": why,
           "target": None if target is None else [round(float(target[0]), 2), round(float(target[1]), 2)],
           "stand": None if stand is None else [round(float(stand[0]), 2), round(float(stand[1]), 2)],
           "near_photo": near_photo}
    out.update(extra)
    return out


ROW_WORDS = {
    "low": "one with the phone at knee height (about 40 cm)",
    "middle": "one at chest height",
    "high": "one at head height tilted up to where the wall meets the ceiling",
}


def _band_spans(grid, axes: dict[str, Any]) -> list[dict[str, Any]]:
    """Stretches of each height band where at least half the visible wall needs more photos."""
    rows, _ = grid.views.shape
    visible = grid.visible
    spans = []
    for band, lo_m, hi_m, _how in BANDS:
        r0 = int(lo_m / CELL_M)
        r1 = min(rows, int(math.ceil(hi_m / CELL_M)))
        if r0 >= r1:
            continue
        band_vis = visible[r0:r1]
        band_need = band_vis & ~grid.good[r0:r1]
        band_unseen = band_vis & (grid.views[r0:r1] == 0)
        vis_cols = band_vis.sum(0)
        need_share = np.where(vis_cols > 0, band_need.sum(0) / np.maximum(vis_cols, 1), 0.0)
        for c0, c1 in runs(need_share >= 0.5):
            u0, u1 = c0 * CELL_M, min(c1 * CELL_M, axes["length"])
            area_need = float(band_need[:, c0:c1].sum()) * CELL_M ** 2
            area_unseen = float(band_unseen[:, c0:c1].sum()) * CELL_M ** 2
            if u1 - u0 < 0.5 or area_need < 0.25:
                continue
            spans.append({"band": band, "u0": u0, "u1": u1, "need": area_need, "unseen": area_unseen,
                          "height": min(hi_m, rows * CELL_M) - lo_m})
    return spans


def _group_spans(spans: list[dict[str, Any]]) -> list[list[dict[str, Any]]]:
    """Spans of different bands over the same stretch of wall become one item with rows."""
    groups: list[list[dict[str, Any]]] = []
    for s in sorted(spans, key=lambda s: (s["u0"], s["band"])):
        for g in groups:
            g0, g1 = min(x["u0"] for x in g), max(x["u1"] for x in g)
            overlap = min(g1, s["u1"]) - max(g0, s["u0"])
            if overlap >= 0.5 * min(g1 - g0, s["u1"] - s["u0"]) and s["band"] not in {x["band"] for x in g}:
                g.append(s)
                break
        else:
            groups.append([s])
    return groups


def wall_items(context: dict[str, Any], profiles: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    items = []
    order = [b[0] for b in BANDS]
    for key, grid in context["walls"].items():
        axes = context["wall_axes"][key]
        prof = profiles[key]
        for group in _group_spans(_band_spans(grid, axes)):
            group.sort(key=lambda s: order.index(s["band"]))
            bands = [s["band"] for s in group]
            u0, u1 = min(s["u0"] for s in group), max(s["u1"] for s in group)
            need = sum(s["need"] for s in group)
            unseen = sum(s["unseen"] for s in group)
            per_row = max(2, math.ceil((u1 - u0) / 0.6))
            n = per_row * len(bands)
            height = sum(s["height"] for s in group)
            dist = min(2.5, max(1.0, 0.9 * max(min(height, 1.6), min(u1 - u0, 2.0))))
            target = axes["left_end"] + axes["right"] * (u0 + u1) / 2
            stand = target + axes["inward"] * dist
            span = span_phrase(u0, u1, prof, axes)
            if len(bands) == 1:
                how = next(b[3] for b in BANDS if b[0] == bands[0])
                text = (f"{words(n).capitalize()} {plural(n, 'photo')} of {wall_ref(prof)}, {span}: {how}, "
                        f"standing about {metres(dist)} back, one step sideways between photos.")
            else:
                rows_text = _join([ROW_WORDS[b] for b in bands])
                text = (f"{words(n).capitalize()} photos of {wall_ref(prof)}, {span}, in {words(len(bands))} rows "
                        f"of {words(per_row)}: {rows_text}, all pointing straight at the wall. Stand about "
                        f"{metres(dist)} back and take one step sideways between photos.")
            state = "No photo shows" if unseen >= 0.6 * need else "Only one or two photos show"
            why = f"{state} this stretch of wall {_join([band_label(b) for b in bands])}. {wall_hint(prof)}".strip()
            prio = sum(BAND_WEIGHT[s["band"]] * (s["need"] + s["unseen"]) for s in group)
            items.append(_item("wall", prio, n, text, why, target=target, stand=stand,
                               near_photo=nearest_photo(context, stand, -axes["inward"]), wall=key, bands=bands,
                               span_m=[round(u0, 2), round(u1, 2)], area_m2=round(need, 2)))
    return items


def low_wall_items(context: dict[str, Any], profiles: dict[str, dict[str, Any]],
                   wall_items_done: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Stretches of the bottom of a wall no photo has seen from low down, as a 10 cm player would."""
    items = []
    for key, grid in context["walls"].items():
        axes = context["wall_axes"][key]
        prof = profiles[key]
        rows_low = min(grid.views.shape[0], int(math.ceil(0.6 / CELL_M)))
        vis = grid.visible[:rows_low]
        n_vis = vis.sum(0)
        missing = (vis & (grid.low_views[:rows_low] == 0)).sum(0)
        share = np.where(n_vis > 0, missing / np.maximum(n_vis, 1), 0.0)
        done = [it["span_m"] for it in wall_items_done if it.get("wall") == key and "low" in it.get("bands", [])]
        for c0, c1 in runs(share >= 0.6):
            u0, u1 = c0 * CELL_M, min(c1 * CELL_M, axes["length"])
            if u1 - u0 < 0.75:
                continue
            if any(min(u1, b1) - max(u0, b0) > 0.5 * (u1 - u0) for b0, b1 in done):
                continue
            n = max(2, math.ceil((u1 - u0) / 0.6))
            target = axes["left_end"] + axes["right"] * (u0 + u1) / 2
            stand = target + axes["inward"] * 1.0
            text = (f"{words(n).capitalize()} {plural(n, 'photo')} at knee height of {wall_ref(prof)}, "
                    f"{span_phrase(u0, u1, prof, axes)}: crouch so the phone is about 40 cm off the floor, "
                    f"pointing straight at the wall from about one metre away (closer where furniture is in "
                    f"the way), one step sideways between photos.")
            why = (f"None of your photos of the bottom of this stretch was taken from below {LOW_CAMERA_M:.1f} m. "
                   f"The player is 10 cm tall and sees it from the floor. {wall_hint(prof)}").strip()
            items.append(_item("low_wall", 0.8 * (u1 - u0), n, text, why, target=target, stand=stand,
                               near_photo=nearest_photo(context, stand, -axes["inward"]), wall=key,
                               bands=["low"], span_m=[round(u0, 2), round(u1, 2)]))
    return items


def band_label(band: str) -> str:
    return {"low": "near the floor", "middle": "at waist-to-chest height", "high": "up near the ceiling"}[band]


def _split_region(cells: list[tuple[int, int]], block_cells: int) -> list[list[tuple[int, int]]]:
    """A big region cut into blocks of about ``block_cells`` x ``block_cells`` grid cells."""
    parts: dict[tuple[int, int], list[tuple[int, int]]] = {}
    for r, c in cells:
        parts.setdefault((r // block_cells, c // block_cells), []).append((r, c))
    return list(parts.values())


def floor_items(context: dict[str, Any]) -> list[dict[str, Any]]:
    grid = context["floor"]
    x0, _, z0, _ = grid.extent
    items = []
    visible = grid.visible
    need = visible & ~grid.good
    visible_area = float(visible.sum()) * CELL_M ** 2
    need_area = float(need.sum()) * CELL_M ** 2
    if need_area > 4.0 and need_area > 0.4 * visible_area:
        # Most of the floor is missing: one systematic pass beats a list of spots.
        rr, cc = np.nonzero(need)
        xz = np.array([x0 + (cc.mean() + 0.5) * CELL_M, z0 + (rr.mean() + 0.5) * CELL_M])
        n = int(min(30, max(6, round(need_area / 1.2))))
        unseen = float((need & (grid.views == 0)).sum()) * CELL_M ** 2
        text = (f"Sweep the floor: walk across the room in rows about one metre apart, like mowing a lawn, with "
                f"the phone at chest height tilted down so the floor fills most of the picture. One photo per "
                f"step, about {words(n) if n <= 12 else n} photos. Then three or four from knee height, looking "
                f"across the floor toward each wall.")
        why = (f"{round(100 * need_area / max(visible_area, 1e-6))}% of the open floor (about {need_area:.0f} square "
               f"metres) is in fewer than three photos, {unseen:.0f} square metres of it in none. The floor is "
               f"where the 10 cm player walks.")
        items.append(_item("floor", 1.8 * (need_area + unseen), n + 4, text, why, target=xz,
                           near_photo=None, area_m2=round(need_area, 2), sweep=True))
        regions_to_list: list[list[tuple[int, int]]] = []
    else:
        regions_to_list = []
        for cells in regions(need):
            regions_to_list += _split_region(cells, 8) if len(cells) * CELL_M ** 2 > 4.0 else [cells]
    for cells in regions_to_list:
        if len(cells) * CELL_M ** 2 < 0.5:
            continue
        rr = np.array([c[0] for c in cells])
        cc = np.array([c[1] for c in cells])
        xz = np.array([x0 + (cc.mean() + 0.5) * CELL_M, z0 + (rr.mean() + 0.5) * CELL_M])
        area = len(cells) * CELL_M ** 2
        unseen = sum(1 for r, c in cells if grid.views[r, c] == 0) * CELL_M ** 2
        n = max(2, min(8, math.ceil(area / 1.0)))
        where = place_phrase(context, xz)
        text = (f"{words(n).capitalize()} {plural(n, 'photo')} of the floor {where}: stand about one metre away, "
                f"phone at chest height tilted down so the floor fills most of the picture, and step sideways "
                f"between photos. Add one from knee height looking across the floor.")
        why = (f"About {area:.1f} square metres of open floor here "
               + ("has no photo looking at it." if unseen >= 0.6 * area else "is in only one or two photos.")
               + " The floor is where the 10 cm player walks.")
        items.append(_item("floor", 1.8 * (area + unseen), n, text, why, target=xz, stand=None,
                           near_photo=nearest_photo(context, xz), area_m2=round(area, 2)))
    # Under furniture with open space below (desks, shelves on legs).
    if grid.open_below is not None:
        under = grid.open_below & ~grid.good
        for cells in regions(under):
            if len(cells) * CELL_M ** 2 < 0.25:
                continue
            rr = np.array([c[0] for c in cells])
            cc = np.array([c[1] for c in cells])
            xz = np.array([x0 + (cc.mean() + 0.5) * CELL_M, z0 + (rr.mean() + 0.5) * CELL_M])
            host = _object_at(context, xz)
            if host is None or host.label not in LEGGED:
                continue  # without furniture that stands on legs, "open below" is more likely noise
            what = f"under the {host.label}"
            text = (f"Two photos {what}: crouch so the phone is at knee height or lower, about one metre away, "
                    f"and point it into the space underneath.")
            why = "There is open space down there a 10 cm player can walk into, and no photo looks into it."
            area = len(cells) * CELL_M ** 2
            items.append(_item("under", 1.5 * area + 0.5, 2, text, why, target=xz,
                               near_photo=nearest_photo(context, xz), area_m2=round(area, 2)))
    return items


def inside_room(context: dict[str, Any], xz: np.ndarray, margin: float = 0.3) -> np.ndarray:
    f = context["frame"]
    return np.array([np.clip(xz[0], f.x_min + margin, f.x_max - margin), np.clip(xz[1], f.z_min + margin, f.z_max - margin)])


def _object_at(context: dict[str, Any], xz: np.ndarray) -> ObjectInstance | None:
    for o in context.get("objects") or []:
        if o.lo[0] - 0.1 <= xz[0] <= o.hi[0] + 0.1 and o.lo[2] - 0.1 <= xz[1] <= o.hi[2] + 0.1:
            return o
    return None


def ceiling_items(context: dict[str, Any]) -> list[dict[str, Any]]:
    grid = context.get("ceiling")
    frame = context["frame"]
    centre = np.array([(frame.x_min + frame.x_max) / 2, (frame.z_min + frame.z_max) / 2])
    if grid is None:
        text = ("Five photos of the ceiling: walk from one end of the room to the other, pointing the phone "
                "straight up, one photo every step or two.")
        return [_item("ceiling", 3.0, 5, text, "I could not find the ceiling in any photo.", target=centre)]
    stats = grid.stats()
    if stats["good_pct"] >= 60:
        return []
    need = grid.visible & ~grid.good
    x0, _, z0, _ = grid.extent
    items = []
    for cells in regions(need)[:2]:
        area = len(cells) * CELL_M ** 2
        if area < 1.5:
            continue
        rr = np.array([c[0] for c in cells])
        cc = np.array([c[1] for c in cells])
        xz = np.array([x0 + (cc.mean() + 0.5) * CELL_M, z0 + (rr.mean() + 0.5) * CELL_M])
        n = max(2, min(6, math.ceil(area / 3.0)))
        where = place_phrase(context, xz)
        text = (f"{words(n).capitalize()} {plural(n, 'photo')} of the ceiling above the floor {where}: "
                f"point the phone straight up and take one photo every step.")
        why = (f"About {area:.0f} square metres of ceiling are in fewer than three photos. From 10 cm tall, "
               f"the ceiling is a big part of what the player sees.")
        items.append(_item("ceiling", 0.25 * area, n, text, why, target=xz, near_photo=None, area_m2=round(area, 2)))
    return items


def object_items(context: dict[str, Any], profiles: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    objects: list[ObjectInstance] = sorted(context.get("objects") or [], key=lambda o: -float(np.prod(o.size[[0, 2]]) + o.size[1]))
    items = []
    low_object: list[ObjectInstance] = []
    names = object_names(objects, profiles)
    for o in objects[:12]:
        seen = [sector_of(a) for a in o.view_angles_deg]
        counts = {s: seen.count(s) for s in set(seen)}
        if o.wall is not None:
            wanted = ["front", "front-left", "front-right"]
        else:
            wanted = ["front", "left side", "right side", "back"]
        missing = [s for s in wanted if counts.get(s, 0) == 0]
        n_views = len(o.views)
        low = [h for h in o.view_heights_m if h < LOW_CAMERA_M]
        if n_views >= 3 and (not missing or len(counts) >= 3):
            if not low and float(np.max(o.size)) >= 0.5:
                low_object.append(o)  # well covered, but never from the player's height
            continue  # seen from three different sides is enough to rebuild it
        top = float(o.hi[1])
        mid = max(0.3, min(1.6, (o.lo[1] + o.hi[1]) / 2))
        dist = min(2.0, max(0.8, 0.9 * max(float(np.max(o.size[[0, 2]])), float(o.size[1]))))
        if not missing:
            missing = ["front"]
        sides = [side_words(s) for s in missing]
        n = max(3 - n_views, len(missing) * (2 if top > 1.3 else 1), 2)
        if top > 1.3:
            height = f"at two heights, knee height and about {mid:.1f} m up (the middle of the {o.label})"
        else:
            height = f"at about {max(0.4, top):.1f} m up, level with its top, plus one from knee height"
        text = (f"{words(n).capitalize()} more {plural(n, 'photo')} of {names[o.id]} from {_join(sides)}, "
                f"{height}, about {metres(dist)} away.")
        seen_text = (f"It is in {n_views} {plural(n_views, 'photo')}" +
                     (f", all from {_join(sorted({side_words(s) for s in counts}))}" if counts else "") + ".")
        why = f"{seen_text} Each object needs at least three photos from different sides to be rebuilt in one piece."
        f = o.front if o.front is not None else np.array([0.0, 1.0])
        left = np.array([-f[1], f[0]])
        direction = {"front": f, "front-left": f + left, "front-right": f - left, "left side": left,
                     "right side": -left, "back": -f}[missing[0]]
        direction = direction / max(1e-6, np.linalg.norm(direction))
        c = o.centre[[0, 2]]
        stand = inside_room(context, c + direction * (dist + float(np.max(o.size[[0, 2]])) / 2))
        prio = 1.2 * (len(missing) + max(0, 3 - n_views)) * min(3.0, 1.0 + float(np.prod(o.size[[0, 2]])))
        items.append(_item("object", prio, n, text, why, target=c, stand=stand,
                           near_photo=nearest_photo(context, stand, -direction), object=o.id, label=o.label,
                           views=n_views, missing_sides=missing))
    for o in low_object[:6]:
        f = o.front if o.front is not None else np.array([0.0, 1.0])
        c = o.centre[[0, 2]]
        stand = inside_room(context, c + f * (1.0 + float(np.max(o.size[[0, 2]])) / 2))
        text = (f"Two photos of {names[o.id]} from knee height (about 40 cm), one from the front and one from "
                f"a side, about one metre away.")
        why = (f"It is in {len(o.views)} photos, all taken from above {LOW_CAMERA_M:.1f} m. From 10 cm tall, its "
               f"lower edges and underside are what the player sees.")
        items.append(_item("object_low", 0.6 + 0.2 * min(3.0, float(np.max(o.size))), 2, text, why, target=c,
                           stand=stand, near_photo=nearest_photo(context, stand, -f), object=o.id, label=o.label,
                           views=len(o.views), missing_sides=[]))
    return items


def side_words(sector: str) -> str:
    return {"front": "the front", "front-left": "the front-left", "front-right": "the front-right",
            "left side": "the left side", "right side": "the right side", "back": "behind"}.get(sector, sector)


def object_names(objects: list[ObjectInstance], profiles: dict[str, dict[str, Any]]) -> dict[str, str]:
    """'the shelving unit on the garage-door wall', unique per object."""
    names = {}
    for o in objects:
        if o.wall is not None and o.wall in profiles:
            prof = profiles[o.wall]
            if prof["name"].startswith(f"the wall with the {o.label}") or prof["name"] == f"wall {o.wall}":
                names[o.id] = f"the {o.label} on wall {o.wall}"
            else:
                names[o.id] = f"the {o.label} on {prof['name']} (wall {o.wall})"
        else:
            names[o.id] = f"the {o.label} in the room"
    counts: dict[str, int] = {}
    for n in names.values():
        counts[n] = counts.get(n, 0) + 1
    for o in objects:
        if counts[names[o.id]] > 1:
            names[o.id] = f"{names[o.id]} ({o.id.replace('_', ' ')})"
    return names


def height_item(context: dict[str, Any]) -> list[dict[str, Any]]:
    cams = context["cams"]
    heights = np.array([cams[v][1] for v in context["registered"]])
    if len(heights) == 0:
        return []
    low_share = float((heights < LOW_CAMERA_M).mean())
    if low_share >= 0.2:
        return []
    n = 12
    text = (f"Walk once around the room with the phone at knee height (about 40 cm), pointing it straight "
            f"ahead at the walls and furniture, one step per photo: about {words(n)} photos.")
    why = (f"Only {round(100 * low_share)}% of your photos were taken below {LOW_CAMERA_M:.1f} m (half were above "
           f"{np.median(heights):.1f} m). The player is 10 cm tall, so the view from low down matters most.")
    return [_item("low_lap", 2.5 + 10 * (0.2 - low_share), n, text, why)]


def bridge_items(context: dict[str, Any]) -> list[dict[str, Any]]:
    graph = context["graph"]
    views = context["views"]
    lost = graph.get("not_fitted", []) + graph.get("unplaced", [])
    if not lost:
        return []
    names = [views[i]["name"] for i in lost]
    n = 2 * len(names)
    target = "the spot it shows" if len(names) == 1 else "the spot each one shows"
    text = (f"For {plural(len(names), 'this photo', 'these photos')} ({', '.join(names[:8])}"
            f"{', ...' if len(names) > 8 else ''}): go back to {target} and take two more, each a step "
            f"further back, so the spot and something already photographed are both in the picture.")
    why = (f"{len(names)} {plural(len(names), 'photo')} did not fit with the rest: nothing else overlaps "
           f"enough with what {plural(len(names), 'it shows', 'they show')}, so {plural(len(names), 'it is', 'they are')} left off the map.")
    return [_item("bridge", 1.0 + 0.5 * len(names), n, text, why, photos_named=names)]


def notes(context: dict[str, Any]) -> list[dict[str, Any]]:
    """Things to know that are not about where to point the camera."""
    m = context["manifest"]
    out = []
    blurry = m["flags"].get("blurry", [])
    if blurry:
        out.append({"kind": "retake", "photos_named": blurry,
                    "text": (f"{len(blurry)} {plural(len(blurry), 'photo is', 'photos are')} blurry and were left out. "
                             f"If a spot matters, retake it holding the phone still for a moment.")})
    front = [p["name"] for p in m["photos"] if p["status"] == "ok" and p.get("lens_kind") == "front"]
    if front:
        out.append({"kind": "lens", "photos_named": front,
                    "text": (f"{len(front)} {plural(len(front), 'photo was', 'photos were')} taken with the front "
                             f"(selfie) camera. They were used, but the back camera gives sharper, better-measured "
                             f"pictures.")})
    wide = [p["name"] for p in m["photos"] if p["status"] == "ok" and p.get("lens_kind") == "ultra_wide"]
    if wide:
        out.append({"kind": "lens", "photos_named": wide,
                    "text": (f"{len(wide)} {plural(len(wide), 'photo was', 'photos were')} taken with the 0.5x "
                             f"ultra-wide lens. Please stay on the normal 1x lens: it bends the picture less.")})
    zoomed = [p["name"] for p in m["photos"] if p["status"] == "ok" and digitally_zoomed(p.get("exif"))
              and "blurry" not in (p.get("quality") or {}).get("flags", [])]  # only photos that were used
    if zoomed:
        out.append({"kind": "zoom", "photos_named": zoomed,
                    "text": (f"{len(zoomed)} {plural(len(zoomed), 'photo was', 'photos were')} taken zoomed in with the "
                             f"camera's own zoom. They were used, but without their lens data, because a phone does not "
                             f"say reliably how much the zoom narrowed the view. Stay on 1x and step closer instead.")})
    exact = m["duplicates"]["exact"]
    if exact:
        copies = sum(len(g) - 1 for g in exact)
        out.append({"kind": "duplicates",
                    "text": (f"{copies} of the {m['summary']['files']} files are exact copies of another file "
                             f"(the ' (1)' names, likely the same photos uploaded twice). Each photo was used once; "
                             f"the copies do no harm and can stay.")})
    return out


def build_guidance(context: dict[str, Any]) -> dict[str, Any]:
    profiles = wall_profiles(context)
    walls = wall_items(context, profiles)
    low_walls = low_wall_items(context, profiles, walls)
    items = (walls + low_walls + floor_items(context) + ceiling_items(context) + object_items(context, profiles)
             + bridge_items(context))
    if not low_walls:  # without specific low stretches, one low lap if few photos are low
        items += height_item(context)
    items.sort(key=lambda it: -it["priority"])
    main, more = items[:MAX_MAIN_ITEMS], items[MAX_MAIN_ITEMS:]
    for n, it in enumerate(main, 1):
        it["number"] = n
    verdict = coverage_verdict(context, items)
    return {
        "items": main,
        "more": more,
        "notes": notes(context),
        "walls": {k: {kk: vv for kk, vv in p.items() if kk != "objects"} | {"objects": [o.id for o in p["objects"]]}
                  for k, p in profiles.items()},
        "photos_suggested": int(sum(it["photos"] for it in main)),
        "photos_suggested_all": int(sum(it["photos"] for it in items)),
        "verdict": verdict,
    }


def coverage_verdict(context: dict[str, Any], items: list[dict[str, Any]]) -> dict[str, Any]:
    """Pass when every wall and the floor are mostly well covered and no object lacks a side."""
    walls = {k: g.stats() for k, g in context["walls"].items()}
    floor = context["floor"].stats()
    ceiling = context["ceiling"].stats() if context.get("ceiling") is not None else None
    checks = {
        "walls_good_pct_min": min(s["good_pct"] for s in walls.values()) if walls else 0.0,
        "floor_good_pct": floor["good_pct"],
        "ceiling_good_pct": None if ceiling is None else ceiling["good_pct"],
        "objects_needing_photos": sum(1 for it in items if it["kind"] == "object"),
    }
    passed = (checks["walls_good_pct_min"] >= 70 and checks["floor_good_pct"] >= 70
              and (checks["ceiling_good_pct"] or 0) >= 40 and checks["objects_needing_photos"] == 0)
    return {"passed": passed, "thresholds": {"walls_good_pct_min": 70, "floor_good_pct": 70, "ceiling_good_pct": 40,
                                             "objects_needing_photos": 0}, **checks}
