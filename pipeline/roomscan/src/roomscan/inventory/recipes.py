"""From an inventory object to the input of a stand-in recipe (``pipeline/recipes``, brief 10).

A recipe's input is exactly ``recipe``, ``size_m`` (width, height, depth in metres, the outer box in the
object's own frame, Y up), ``colours`` (named slots, each ``#rrggbb``) and ``params``. Nothing else is
allowed in it: the recipe runner refuses unknown keys. Everything else the stand-in's builder needs
(where the object stands, which way it faces, how sure the scan is) lives beside the request in the
inventory entry, never in it.

Slot colours come from the object's own points and pixels: a couch's upholstery from its body, its
legs from the lowest band if that band looks like wood, a box's cardboard from its sides. A slot the
scan cannot see (a closed laptop's keyboard) is left out so the recipe uses its default, and the entry
says so.
"""

from __future__ import annotations

import colorsys
from dataclasses import dataclass, field
from typing import Any, Callable

import numpy as np

from ..colours import chroma, dominant_colours, hex_of, lightness, rgb_of

REQUEST_KEYS = ("recipe", "size_m", "colours", "params")
# The slots each recipe knows (brief 10's pipeline/recipes/specs.py); a request naming another is refused by the runner.
SLOTS: dict[str, tuple[str, ...]] = {
    "cardboard_box": ("cardboard", "edges"),
    "couch": ("upholstery", "legs"),
    "gaming_laptop": ("shell", "keyboard", "screen"),
    "jam_jar": ("glass", "lid"),
    "french_press": ("beaker", "frame", "handle"),
}
PARAMS: dict[str, dict[str, tuple[Any, Any, Any]]] = {  # name -> (low, high, integer?) for the parameters the inventory sets
    "cardboard_box": {"flap_angle_deg": (0, 120, False)},
    "couch": {"cushion_count": (1, 6, True)},
    "gaming_laptop": {"lid_angle_deg": (0, 135, False)},
    "jam_jar": {"lid_pattern": ("gingham", "solid", "stripes"), "lid_lift_fraction": (0, 0.3, False)},
    "french_press": {"beaker_material": ("glass", "metal")},
}


class RequestError(ValueError):
    """A recipe request breaks the recipe input rules."""


def validate_request(request: dict[str, Any]) -> None:
    """The recipe input rules of brief 10 that do not depend on a particular recipe's table."""
    extra = set(request) - set(REQUEST_KEYS)
    if extra:
        raise RequestError(f"unknown keys: {', '.join(sorted(extra))}")
    name = request.get("recipe")
    if name not in SLOTS:
        raise RequestError(f"recipe must be one of {', '.join(SLOTS)}")
    size = request.get("size_m")
    if not isinstance(size, list) or len(size) != 3 or any(type(v) not in (int, float) or not 0.02 <= v <= 20 for v in size):
        raise RequestError("size_m must be [width, height, depth], each 0.02 to 20 metres")
    if max(size) / min(size) > 100:
        raise RequestError("size_m aspect ratio must be at most 100")
    for slot, value in (request.get("colours") or {}).items():
        if slot not in SLOTS[name]:
            raise RequestError(f"colours.{slot} is not a slot of {name}")
        if not (isinstance(value, str) and len(value) == 7 and value[0] == "#" and all(c in "0123456789abcdef" for c in value[1:])):
            raise RequestError(f"colours.{slot} must be lowercase #rrggbb")
    for key, value in (request.get("params") or {}).items():
        rule = PARAMS[name].get(key)
        if rule is None:
            raise RequestError(f"params.{key} is not set by the inventory for {name}")
        if isinstance(rule[0], str):
            if value not in rule:
                raise RequestError(f"params.{key} must be one of {', '.join(rule)}")
        elif not rule[0] <= value <= rule[1] or (rule[2] and type(value) is not int):
            raise RequestError(f"params.{key} must be between {rule[0]} and {rule[1]}")


@dataclass
class Evidence:
    """What the scan holds about one object, for choosing slot colours."""
    points: np.ndarray  # (N, 3) room frame
    rgb: np.ndarray  # (N, 3) colour of each point's pixel
    box_base_y: float
    box_height: float
    pixels: np.ndarray = field(default_factory=lambda: np.zeros((0, 3)))  # colours of the object's mask pixels at full resolution


def _band(ev: Evidence, lo: float, hi: float, minimum: int = 25) -> np.ndarray:
    """Colours of the points in a band of the box's height (0 = underside, 1 = top)."""
    t = (ev.points[:, 1] - ev.box_base_y) / max(ev.box_height, 1e-6)
    sel = (t >= lo) & (t <= hi)
    return ev.rgb[sel] if int(sel.sum()) >= minimum else np.zeros((0, 3))


def _main_colour(rgb: np.ndarray, fallback: np.ndarray | None = None, *, chromatic: bool = False) -> str | None:
    """The dominant colour; with ``chromatic`` the most colourful of the clusters that hold at least an eighth of the pixels
    (a brown box in a dark corner is mostly shadow, and the brown is what it is made of)."""
    if len(rgb) == 0:
        if fallback is None or len(fallback) == 0:
            return None
        rgb = fallback
    clusters = dominant_colours(rgb, 3, min_fraction=0.12)
    if chromatic:
        lit = [(h, s) for h, s in clusters if lightness(h) > 22]
        if lit:
            return max(lit, key=lambda c: chroma(c[0]))[0]
    return clusters[0][0]


def _is_woodlike(hex_colour: str) -> bool:
    r, g, b = (v / 255 for v in rgb_of(hex_colour))
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    return 10 / 360 <= h <= 55 / 360 and s >= 0.25 and 0.2 <= v <= 0.85


def _lighten(hex_colour: str, amount: float) -> str:
    rgb = rgb_of(hex_colour)
    return hex_of(rgb + (255 - rgb) * amount)


def slots_for(recipe: str, ev: Evidence) -> tuple[dict[str, str], dict[str, str]]:
    """Slot colours for a recipe and where each came from ("scan: ..."), leaving out slots the scan cannot show."""
    colours: dict[str, str] = {}
    source: dict[str, str] = {}
    body = ev.rgb if len(ev.rgb) else ev.pixels
    if recipe == "cardboard_box":
        main = _main_colour(ev.pixels if len(ev.pixels) >= 50 else body, body, chromatic=True)
        if main:
            colours["cardboard"] = main
            source["cardboard"] = "scan: the most colourful of the box's main colours (the rest is shadow)"
            colours["edges"] = _lighten(main, 0.12)
            source["edges"] = "derived: the cardboard colour lightened 12 per cent"
    elif recipe == "couch":
        # The mask's own pixels, at full resolution, say what the cloth is; the points' pixels are a coarser second opinion.
        upper = _main_colour(ev.pixels, None) if len(ev.pixels) >= 100 else _main_colour(_band(ev, 0.25, 1.0), body)
        if upper:
            colours["upholstery"] = upper
            source["upholstery"] = "scan: dominant colour above the lowest quarter of its height"
        low = _main_colour(_band(ev, 0.0, 0.12))
        if low and _is_woodlike(low) and lightness(low) > 15:
            colours["legs"] = low
            source["legs"] = "scan: lowest band looks like wood"
    elif recipe == "gaming_laptop":
        shell = _main_colour(ev.pixels if len(ev.pixels) >= 50 else body, body)
        if shell:
            colours["shell"] = shell
            source["shell"] = "scan: dominant colour of the lid and sides"
    elif recipe == "jam_jar":
        top = _main_colour(_band(ev, 0.78, 1.0, minimum=8))
        if top and (chroma(top) > 18 or lightness(top) < 35):
            colours["lid"] = top
            source["lid"] = "scan: the top band"
    elif recipe == "french_press":
        main = _main_colour(body)
        if main:
            colours["frame"] = main
            source["frame"] = "scan: dominant colour"
    return colours, source


def cushion_count(width_m: float) -> int:
    """One seat cushion per roughly 0.6 m of couch, between 1 and 6."""
    return int(min(6, max(1, round(width_m / 0.6))))


def request_for(kind_recipe: str, size_m: tuple[float, float, float], colours: dict[str, str],
                *, state: dict[str, Any] | None = None) -> dict[str, Any]:
    """The recipe input for an object: size in the object's own frame, the slot colours found, and the few params the scan sets."""
    state = state or {}
    params: dict[str, Any] = {}
    if kind_recipe == "couch":
        params["cushion_count"] = cushion_count(size_m[0])
    elif kind_recipe == "gaming_laptop":
        params["lid_angle_deg"] = float(state.get("lid_angle_deg", 0.0))
    elif kind_recipe == "cardboard_box":
        params["flap_angle_deg"] = float(state.get("flap_angle_deg", 0.0))
    size = [round(max(float(v), 0.02), 3) for v in size_m]
    request = {"recipe": kind_recipe, "size_m": size, "colours": dict(sorted(colours.items())), "params": params}
    validate_request(request)
    return request


SlotRule = Callable[[Evidence], tuple[dict[str, str], dict[str, str]]]
