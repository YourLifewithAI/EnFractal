"""Choosing the objects that become stand-ins: the founder's kinds where the room has them, varied otherwise.

The founder's first recipes are a cardboard box, a couch, a gaming laptop, an empty jam jar and a metal
French press. Where the scan found one of those kinds, the best sighting of it is picked (an object on
the floor wins over one on a shelf when they are close, because a stand-in on the floor needs nothing
under it). Where it did not, the pick is the best-evidenced object of a kind not yet picked, from a
different kind of thing than the others (a furniture, a container, a device, a vessel, an appliance),
and the entry names the recipe it still needs.
"""

from __future__ import annotations

from typing import Any

PREFERRED = ("cardboard box", "couch", "laptop", "jar", "french press")
# How a kind groups for variety; kinds not listed are their own group.
VARIETY = {
    "cardboard box": "container", "storage tote": "container", "suitcase": "container", "cooler": "container", "bin": "container",
    "basket": "container", "couch": "furniture", "desk": "furniture", "table": "furniture", "shelving unit": "furniture",
    "cabinet": "furniture", "chest of drawers": "furniture", "office chair": "furniture", "bean bag": "soft furnishing",
    "easel": "furniture", "step ladder": "tool", "bicycle": "vehicle", "laptop": "device", "monitor": "device",
    "desktop computer": "device", "printer": "device", "speaker": "device", "projector": "device", "air conditioner": "appliance",
    "jar": "vessel", "mug": "vessel", "bottle": "vessel", "kettle": "vessel", "french press": "vessel", "paint can": "vessel",
}
MIN_CONFIDENCE = 0.4


def _floor_bonus(entry: dict[str, Any]) -> float:
    return 0.15 if entry["placement"]["support"]["kind"] == "floor" else 0.0


def _usable(entry: dict[str, Any]) -> bool:
    return entry["confidence"] >= MIN_CONFIDENCE and entry["evidence"]["masks_accepted"] >= 2


def pick_five(entries: list[dict[str, Any]], *, count: int = 5, preferred: tuple[str, ...] = PREFERRED) -> list[dict[str, Any]]:
    """The picks in order, each ``{"id", "kind", "why"}``; fewer than ``count`` only if the room holds fewer usable objects."""
    usable = [e for e in entries if _usable(e)]
    picks: list[dict[str, Any]] = []
    chosen: set[str] = set()

    def best(candidates: list[dict[str, Any]]) -> dict[str, Any]:
        return max(candidates, key=lambda e: (e["confidence"] + _floor_bonus(e), e["id"]))

    for kind in preferred:
        found = [e for e in usable if e["kind"] == kind and e["id"] not in chosen]
        if found:
            e = best(found)
            picks.append({"id": e["id"], "kind": kind, "why": f"the room holds a {kind}; the best-evidenced one"})
            chosen.add(e["id"])
    missing = [k for k in preferred if k not in {p["kind"] for p in picks}]
    groups = {VARIETY.get(p["kind"], p["kind"]) for p in picks}
    while len(picks) < count:
        rest = [e for e in usable if e["id"] not in chosen and e["kind"] not in {p["kind"] for p in picks}]
        if not rest:
            break
        fresh = [e for e in rest if VARIETY.get(e["kind"], e["kind"]) not in groups] or rest
        e = best(fresh)
        gap = f"the room has no {missing[0]}; " if missing else ""
        picks.append({"id": e["id"], "kind": e["kind"], "why": f"{gap}the best-evidenced {VARIETY.get(e['kind'], e['kind'])} not yet picked, which needs a recipe of its own"})
        chosen.add(e["id"])
        groups.add(VARIETY.get(e["kind"], e["kind"]))
        if missing:
            missing.pop(0)
    return picks[:count]
