"""What the inventory looks for: kinds of household object, the phrases the detector is asked, how big
each kind plausibly is, and which recipe (``pipeline/recipes``) builds a stand-in for it.

Kinds are plain English names, never text read from a photo. The detector (OWLv2) is asked for
phrases; every phrase belongs to one kind. The plausible sizes only reject impossible lifts (a
"jar" three metres wide is a wall); they never set a size. Sizes are the object's longest
horizontal or vertical extent in metres.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Kind:
    name: str
    queries: tuple[str, ...]
    longest_m: tuple[float, float]  # plausible range of the largest extent
    small: bool = False  # found by the tiled pass on close-up detail
    recipe: str | None = None  # the stand-in recipe in pipeline/recipes, when one exists
    footprint: str = "box"  # "box" or "round": a round object has no meaningful yaw
    support: str = "any"  # "floor": stands on the floor; "any": floor, a surface or a shelf


KINDS: tuple[Kind, ...] = (
    # --- the founder's five recipes -----------------------------------------------------------
    Kind("cardboard box", ("a cardboard box", "a brown cardboard shipping box"), (0.15, 1.2), recipe="cardboard_box"),
    Kind("couch", ("a couch", "a sofa", "a loveseat"), (1.0, 2.8), recipe="couch", support="floor"),
    Kind("laptop", ("a laptop computer", "a gaming laptop"), (0.22, 0.5), small=True, recipe="gaming_laptop"),
    Kind("jar", ("a glass jar", "a jam jar", "a mason jar"), (0.05, 0.22), small=True, recipe="jam_jar", footprint="round"),
    Kind("french press", ("a french press", "a coffee press", "a coffee plunger"), (0.12, 0.4), small=True,
         recipe="french_press", footprint="round"),
    # --- furniture ------------------------------------------------------------------------------
    Kind("desk", ("a desk", "a wooden work table", "a workbench"), (0.8, 2.6), support="floor"),
    Kind("table", ("a round table", "an octagonal table", "a folding table", "a coffee table"), (0.5, 2.0), support="floor"),
    Kind("shelving unit", ("a shelving unit", "a wooden bookshelf"), (0.6, 2.6), support="floor"),
    Kind("cabinet", ("a wooden cabinet", "a cupboard"), (0.5, 2.0), support="floor"),
    Kind("chest of drawers", ("a white chest of drawers", "a drawer unit"), (0.3, 1.5), support="floor"),
    Kind("office chair", ("an office chair", "a gaming chair"), (0.5, 1.4), support="floor"),
    Kind("bean bag", ("a bean bag chair",), (0.5, 1.5), support="floor", footprint="round"),
    Kind("easel", ("a wooden easel",), (0.5, 2.2), support="floor"),
    Kind("step ladder", ("a step ladder", "a ladder"), (0.6, 2.2), support="floor"),
    Kind("bicycle", ("a bicycle", "a child's bicycle"), (0.5, 2.0), support="floor"),
    # --- appliances and electronics -------------------------------------------------------------
    Kind("air conditioner", ("a portable air conditioner",), (0.4, 1.0), support="floor"),
    Kind("printer", ("a printer",), (0.3, 0.7)),
    Kind("monitor", ("a computer monitor", "a flat screen display"), (0.35, 0.9)),
    Kind("desktop computer", ("a desktop computer tower",), (0.3, 0.7)),
    Kind("speaker", ("a speaker", "a tower speaker"), (0.15, 1.2)),
    Kind("projector", ("a ceiling projector",), (0.2, 0.5)),
    # --- containers and bags --------------------------------------------------------------------
    Kind("storage tote", ("a clear plastic storage box", "a plastic storage tote"), (0.25, 0.9)),
    Kind("suitcase", ("a suitcase", "a trunk"), (0.4, 1.2), support="floor"),
    Kind("cooler", ("a cooler",), (0.3, 0.8)),
    Kind("bin", ("a trash can", "a waste bin", "a bucket"), (0.25, 0.9), support="floor", footprint="round"),
    Kind("basket", ("a basket",), (0.2, 0.8)),
    Kind("backpack", ("a backpack", "a duffel bag"), (0.3, 1.0)),
    Kind("paint can", ("a paint can", "a paint bucket"), (0.12, 0.4), small=True, footprint="round"),
    # --- small things on surfaces ---------------------------------------------------------------
    Kind("mug", ("a mug", "a coffee cup"), (0.06, 0.16), small=True, footprint="round"),
    Kind("bottle", ("a bottle", "a thermos"), (0.1, 0.4), small=True, footprint="round"),
    Kind("kettle", ("an electric kettle",), (0.15, 0.4), small=True, footprint="round"),
    Kind("lamp", ("a desk lamp", "a floor lamp"), (0.2, 1.8)),
    Kind("plant", ("a potted plant",), (0.15, 1.5), footprint="round"),
    Kind("guitar", ("a guitar",), (0.6, 1.3)),
    Kind("board game", ("a board game box",), (0.2, 0.6), small=True),
    Kind("painting", ("a framed painting", "a canvas painting"), (0.2, 1.8), support="wall"),
    Kind("window", ("a window",), (0.4, 2.0), support="wall"),
    Kind("door", ("a door",), (0.7, 2.2), support="wall"),
)

BY_NAME: dict[str, Kind] = {k.name: k for k in KINDS}
QUERY_KIND: dict[str, str] = {q: k.name for k in KINDS for q in k.queries}
SMALL_QUERIES: tuple[str, ...] = tuple(q for k in KINDS if k.small for q in k.queries)
RECIPE_KINDS: dict[str, str] = {k.name: k.recipe for k in KINDS if k.recipe}
# Kinds that are part of the shell, not objects: they feed the openings, not the inventory.
SHELL_KINDS: frozenset[str] = frozenset({"window", "door"})
# Names a detector phrase may collapse into after review (the reviewer's vocabulary): kind -> kinds it may be renamed to.
ALIASES: dict[str, tuple[str, ...]] = {
    "couch": ("couch",), "sofa": ("couch",), "settee": ("couch",), "futon": ("couch",),
}


def kind_for_query(query: str) -> str:
    return QUERY_KIND[query]


def plausible(kind: str, longest_m: float) -> bool:
    lo, hi = BY_NAME[kind].longest_m
    return lo * 0.6 <= longest_m <= hi * 1.4
