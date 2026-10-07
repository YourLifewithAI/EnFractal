#!/usr/bin/env python3
"""Write game/rooms/test_room deterministically: the hand-built placeholder room as contract data.

Dimensions, props, spawns and the lamp started as the earlier code-built RoomTestWorld. Run 1 added a west
window, because light comes only from real sources, and a palette drawn from the founder's reference notes.
Rerun after editing; never hand-edit the output, because room.json pins every asset.json by hash.

  python tools/rooms/build_test_room.py [--out DIR]
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CREATED = "2026-10-06T00:00:00Z"
W, D, H, T = 4.0, 3.0, 2.4, 0.10  # interior width (X), depth (Z), height (Y), shell thickness
IDENTITY = [0.0, 0.0, 0.0, 1.0]
# West window (Run 1: light comes only from real sources; the founder chose west for sunset light). -Z is north and +X
# east, so the west wall is at -X. Centred 0.3 m south of the room's centre line so late sun reaches the rug and the
# spawns; 1.2 m wide, 1.5 m tall, its sill 0.5 m above the floor.
WIN_Z, WIN_W, WIN_SILL, WIN_H = 0.3, 1.2, 0.5, 1.5

# Palette from the founder's reference notes (Tiny Glade 1: soft greens, creamy whites, terracotta, pale pink;
# Titl Shift 7: warm gold with light blue), so the warm light has cool and varied surfaces to land on.
WALL_CREAM, WALL_BLUE = "#ddd5c6", "#b8c7c8"

PROPS = [
    # asset_id, instance, display, category, group, size_m (x, y, z), foot (x, y, z), role, colour, movable, mass_kg, affordances
    ("table_proxy", "table", "Table", "low table", "furniture", (1.2, 0.75, 0.6), (-0.9, 0.0, -0.9), "wood", "#93a68a", True, 25.0, ["walkable_top", "climbable"]),
    ("box_proxy", "box", "Cardboard box", "cardboard box", "container", (0.35, 0.30, 0.35), (1.1, 0.0, 0.2), "cardboard", "#b39c7c", True, 1.5, ["walkable_top", "climbable", "container"]),
    ("book_proxy", "book", "Book", "book", "stationery", (0.22, 0.04, 0.15), (0.45, 0.0, 0.1), "paper", "#7d9cc0", True, 0.6, ["walkable_top", "climbable", "readable"]),
    ("rug_proxy", "rug", "Rug", "rug", "textile", (1.6, 0.006, 1.1), (0.2, 0.0, 0.7), "fabric", "#d3a3a6", True, 2.0, ["walkable_top", "soft"]),
    ("doorstop_proxy", "doorstop", "Doorstop", "rubber doorstop", "tool", (0.12, 0.04, 0.08), (-0.3, 0.0, 0.5), "rubber", "#b86a52", True, 0.3, ["walkable_top", "climbable"]),
]


def dump(document) -> bytes:
    return (json.dumps(document, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def write(path: Path, data: bytes) -> dict:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}


def polygon(part_id, role, points, material_role, colour):
    return {
        "id": f"shell:{part_id}", "role": role,
        "geometry": {"kind": "polygon", "points_m": [list(p) for p in points], "thickness_m": T},
        "collides": True, "material_role": material_role, "base_color": colour,
    }


def build(out: Path) -> None:
    x, z, xo, zo = W / 2, D / 2, W / 2 + T, D / 2 + T
    wz0, wz1, sill, head = WIN_Z - WIN_W / 2, WIN_Z + WIN_W / 2, WIN_SILL, WIN_SILL + WIN_H
    # Winding: counter-clockwise seen from inside, so the right-hand normal faces the room.
    shell = [
        polygon("floor", "floor", [(-xo, 0, -zo), (-xo, 0, zo), (xo, 0, zo), (xo, 0, -zo)], "wood", "#9a8f7e"),
        polygon("ceiling", "ceiling", [(-xo, H, -zo), (xo, H, -zo), (xo, H, zo), (-xo, H, zo)], "plaster", "#ece8df"),
        polygon("wall_north", "wall", [(-xo, 0, -z), (xo, 0, -z), (xo, H, -z), (-xo, H, -z)], "painted_wall", WALL_CREAM),
        polygon("wall_south", "wall", [(xo, 0, z), (-xo, 0, z), (-xo, H, z), (xo, H, z)], "painted_wall", WALL_CREAM),
        polygon("wall_east", "wall", [(x, 0, -z), (x, 0, z), (x, H, z), (x, H, -z)], "painted_wall", WALL_BLUE),
        # The west wall in four parts around the window opening, so no polygon needs a hole.
        polygon("wall_west", "wall", [(-x, 0, z), (-x, 0, -z), (-x, sill, -z), (-x, sill, z)], "painted_wall", WALL_BLUE),
        polygon("wall_west_head", "wall", [(-x, head, z), (-x, head, -z), (-x, H, -z), (-x, H, z)], "painted_wall", WALL_BLUE),
        polygon("wall_west_south", "wall", [(-x, sill, z), (-x, sill, wz1), (-x, head, wz1), (-x, head, z)], "painted_wall", WALL_BLUE),
        polygon("wall_west_north", "wall", [(-x, sill, wz0), (-x, sill, -z), (-x, head, -z), (-x, head, wz0)], "painted_wall", WALL_BLUE),
    ]
    files = []
    objects = []
    for asset_id, instance, display, category, group, size, foot, role, colour, movable, mass, affordances in PROPS:
        asset = {
            "schema": "enfractal.asset", "version": 1,
            "asset_id": asset_id, "display_name": display, "category": category, "category_group": group,
            "tier": "proxy",
            "provenance": {"kind": "placeholder", "license": "Original EnFractal placeholder; CC0-1.0", "created_utc": CREATED,
                           "notes": "Primitive stand-in at real furniture size so scale reads from the small avatar."},
            "pivot": "bottom_center",
            "dimensions_m": list(size),
            "geometry": {"kind": "primitive", "primitive": "box"},
            "collision": {"kind": "primitive"},
            "physics": {"movable": movable, "mass_kg": mass},
            "materials": [{"slot": "body", "role": role, "base_color": colour}],
            "affordances": affordances,
            "review": {"status": "approved", "reviewer": "integrator", "notes": "Placeholder geometry; approved as a test fixture only."},
            "files": [],
        }
        rel = f"objects/{asset_id}/asset.json"
        files.append({"path": rel, **write(out / rel, dump(asset))})
        objects.append({
            "id": f"obj:{instance}", "asset": rel,
            "transform": {"position_m": list(foot), "rotation": IDENTITY},
            "support": {"kind": "floor", "target_id": "shell:floor"},
        })
    room = {
        "schema": "enfractal.room", "version": 1,
        "room_id": "test_room", "display_name": "Test room",
        "description": "Hand-built placeholder room: a 4 x 3 x 2.4 m box with furniture-sized proxies. Not a captured space. The Look track develops the first style here before captured assets exist.",
        "created_utc": CREATED, "units": "m", "axes": "y_up_neg_z_forward",
        "source": {"kind": "hand_built"},
        "bounds": {"min_m": [-x, 0.0, -z], "max_m": [x, H, z]},
        "shell": {"parts": shell, "openings": [
            {"id": "window_west", "kind": "window", "host_part_id": "shell:wall_west", "center_m": [-x, sill + WIN_H / 2, WIN_Z],
             "size_m": [WIN_W, WIN_H], "state": "fixed", "traversable": False},
        ]},
        "objects": objects,
        "spawns": [
            {"id": "player_start", "role": "player", "position_m": [0.0, 0.0, 0.6], "yaw_deg": 0.0},
            {"id": "companion_start", "role": "companion", "position_m": [0.45, 0.0, 0.6], "yaw_deg": 0.0},
        ],
        "light_hints": [
            {"id": "ceiling_lamp", "kind": "ceiling_lamp", "position_m": [0.0, H - 0.15, 0.0], "color": "#ffe7bf", "relative_intensity": 0.8, "estimated": False},
            # Sky light through the window: just outside the opening, aimed east into the room and a little down.
            {"id": "window_west", "kind": "window", "position_m": [-xo - 0.05, sill + WIN_H / 2, WIN_Z], "direction": [1.0, -0.3, 0.0],
             "color": "#d6e2f5", "relative_intensity": 0.7, "estimated": False},
            # Direct sun may come in through the room's windows; the look works out its direction from the clock.
            {"id": "sun", "kind": "sun", "color": "#fff1d2", "relative_intensity": 1.0, "estimated": False},
        ],
        "files": sorted(files, key=lambda entry: entry["path"]),
    }
    write(out / "room.json", dump(room))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=ROOT / "game" / "rooms" / "test_room")
    args = parser.parse_args()
    build(args.out)
    print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
