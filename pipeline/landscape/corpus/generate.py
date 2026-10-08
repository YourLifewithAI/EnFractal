"""Build invented C3/C4 fixtures and PNG plans; standard library only.

Run from the repository root: python -B -m pipeline.landscape.corpus.generate
The captured-room export guards are deliberately NOT called or changed: this is
a synthetic fixture builder, like roomscan's own tests, never a capture export.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import math
import os
from pathlib import Path
import random
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "pipeline" / "roomscan" / "src"))
# These existing C4 modules import only the standard library.
from roomscan.inventory.vocabulary import BY_NAME
from roomscan.inventory.picks import pick_five
from roomscan.jsonio import json_bytes

from .fixtures import SOURCES, layouts
from .preview import Canvas, corners, plan

OUTPUT = Path(__file__).resolve().parent / "rooms"
SEEDS = (17,73)
BOUNDS = {"size_relative_max": .15, "position_distance_max_m": .1,
          "yaw_delta_deg": 0, "colour_channel_shift_max": 24,
          "low_confidence_range": [.18,.38], "mislabel_fraction_target_max": .25,
          "mislabel_count_min": 1}
FRAME = "the shell's room frame: metres, +Y up, floor at y = 0, -Z up the page; walls A at z_min, B at x_max, C at z_max, D at x_min"
BOX_CONVENTION = "size_m is [width (local X), height, depth (local Z)]; yaw_deg turns local -Z (the front) to the direction it faces, positive turning left seen from above; placement.position_m is the bottom centre"
MISLABELS = {"couch": "table", "desk": "table", "office chair": "bean bag",
             "shelving unit": "cabinet", "cabinet": "shelving unit", "jar": "mug",
             "mug": "jar", "bottle": "jar", "paint can": "jar", "plant": "bin",
             "bean bag": "basket", "bicycle": "step ladder", "laptop": "board game"}
SLOTS = {"cardboard_box": ("cardboard","edges"), "couch": ("upholstery","legs"),
         "gaming_laptop": ("shell","keyboard","screen"), "jam_jar": ("glass","lid"),
         "french_press": ("beaker","frame","handle")}
BODY_SLOT = {"cardboard_box": "cardboard", "couch": "upholstery",
             "gaming_laptop": "shell", "jam_jar": "lid", "french_press": "frame"}


def shifted(colour, shift):
    channels = [int(colour[i:i+2],16) for i in (1,3,5)]
    return "#"+"".join(f"{max(0,min(255,c+d)):02x}" for c,d in zip(channels,shift))


def imperfect(layout, seed):
    """Bounded observations, retaining IDs for oracle comparison.

    Floor positions stay at y=0; linked tops remain linked. Parent height noise
    is capped at 4 cm so stacks fit the same 10 cm Euclidean position budget.
    Wall intrusions and box intersections can occur, as with independently fitted
    noisy observations. They are NOT edits to the authored physical truth.
    """
    rng = random.Random(f"brief14-v1:{layout['id']}:{seed}")
    observed = copy.deepcopy(layout["objects"])
    parents = {o["support_target"] for o in observed if o["support_target"]}
    shift = [rng.randint(8,24),rng.randint(-6,6),-rng.randint(8,24)] if seed == 17 else [-rng.randint(8,24),rng.randint(-6,6),rng.randint(8,24)]
    by_id = {}
    for o,truth in zip(observed,layout["objects"]):
        multipliers = [rng.uniform(.85,1.15) for _ in range(3)]
        if o["id"] in parents:
            multipliers[1] = 1+rng.uniform(-min(.15,.04/o["size_m"][1]),min(.15,.04/o["size_m"][1]))
        o["size_m"] = [round(v*m,6) for v,m in zip(o["size_m"],multipliers)]
        x,y,z = truth["position_m"]
        if o["support_target"]:
            parent = by_id[o["support_target"]]
            original = next(t for t in layout["objects"] if t["id"] == parent["id"])
            dx = parent["position_m"][0]-original["position_m"][0]
            dz = parent["position_m"][2]-original["position_m"][2]
            y = parent["position_m"][1]+parent["size_m"][1]
            radius = .015
        else:
            dx = dz = 0
            radius = .09
        angle = rng.uniform(0,2*math.pi)
        dx += math.cos(angle)*radius
        dz += math.sin(angle)*radius
        available = math.sqrt(max(0,.099**2-(y-truth["position_m"][1])**2))
        length = math.hypot(dx,dz)
        if length > available:
            dx,dz = dx*available/length,dz*available/length
        o["position_m"] = [round(x+dx,6),round(y,6),round(z+dz,6)]
        o["colour"] = shifted(o["colour"],shift)
        by_id[o["id"]] = o
    # One to a quarter are deliberately mislabeled, another subset uncertain.
    candidates = [o for o in observed if o["kind"] in MISLABELS]
    rng.shuffle(candidates)
    for o in candidates[:max(1,len(observed)//4)]:
        o["kind"] = MISLABELS[o["kind"]]
        o["confidence"] = round(rng.uniform(.18,.38),2)
    for index,o in enumerate(observed):
        if index % 4 == seed % 4:
            o["confidence"] = round(rng.uniform(.18,.38),2)
    return observed,shift


def entry(o):
    size = o["size_m"]
    position = o["position_m"]
    yaw = o["yaw_deg"]
    pts = corners(position,size,yaw)
    half = math.radians(yaw)/2
    support = {"kind": "floor", "height_m": 0.0}
    if o["support_target"]:
        support = {"kind": "object", "height_m": round(position[1],3), "target_id": o["support_target"]}
    notes = ["Synthetic observation: evidence counts are simulated; sheet is a plan, no photos or models used."]
    if o["semantic_label"] != o["kind"]:
        notes.append("Synthetic uncertain label; physical truth is in truth.json (not a converter input).")
    out = {"id": o["id"], "kind": o["kind"], "confidence": o["confidence"],
           "box": {"centre_m": [round(position[0],3),round(position[1]+size[1]/2,3),round(position[2],3)],
                   "size_m": [round(v,3) for v in size], "yaw_deg": round(yaw,1),
                   "footprint_m": [round(min(p[0] for p in pts),3),round(min(p[1] for p in pts),3),
                                   round(max(p[0] for p in pts),3),round(max(p[1] for p in pts),3)]},
           "placement": {"position_m": [round(v,3) for v in position], "yaw_deg": round(yaw,1),
                         "rotation": [0.0,round(math.sin(half),6),0.0,round(math.cos(half),6)],
                         "support": support, "front_known": BY_NAME[o["kind"]].footprint != "round"},
           "colours": [{"hex": o["colour"], "share": 1.0}],
           "evidence": {"photos_followed": 4, "masks_accepted": 4, "points": 200,
                        "detector_views": 0, "detector_mean_score": 0.0, "source": "added",
                        "also_called": {}, "sheet": "preview.png"}, "notes": notes}
    recipe = BY_NAME[o["kind"]].recipe
    if recipe:
        slot = BODY_SLOT[recipe]
        params = {}
        if recipe == "couch":
            params = {"cushion_count": min(6,max(1,round(size[0]/.6)))}
        elif recipe == "cardboard_box":
            params = {"flap_angle_deg": 0.0}
        elif recipe == "gaming_laptop":
            params = {"lid_angle_deg": 0.0}
        out["recipe"] = {"recipe": recipe, "size_m": [round(max(v,.02),3) for v in size],
                         "colours": {slot: o["colour"]}, "params": params}
        out["recipe_slots"] = {"from_scan": {slot: "synthetic broad colour"}, "defaulted": [s for s in SLOTS[recipe] if s != slot]}
    else:
        out["recipe"] = None
        out["recipe_needed"] = {"for_kind": o["kind"], "size_m": out["box"]["size_m"], "colours": [o["colour"]]}
    return out


def inventory(layout, objects, room_id):
    entries = sorted([entry(o) for o in objects],key=lambda e:(e["kind"],e["box"]["centre_m"][0],e["box"]["centre_m"][2]))
    picks = pick_five(entries)
    by_id = {e["id"]:e for e in entries}
    for rank,p in enumerate(picks,1):
        by_id[p["id"]]["pick"] = {"rank": rank, "why": p["why"]}
    frame = FRAME if layout["id"] != "awkward_l" else "the shell's room frame: metres, +Y up, floor at y = 0, -Z up the page; six wall polygons A-F in outline order, including the re-entrant corner"
    return {"schema": "enfractal.inventory", "version": 1, "room": room_id,
            "session_id": "s-synthetic-"+room_id, "frame": frame,
            "scale": {"factor": 1.0, "source": "synthetic metric truth"}, "box_convention": BOX_CONVENTION,
            "detector": {"detector": "injected", "threshold": .14, "photos": 0,
                         "queries": 0, "gpu_seconds": 0.0, "peak_allocated_mib": 0,
                         "reused_from_cache": False, "detections": 0},
            "segmenter": {"prompts": 0, "new": 0, "gpu_seconds": 0.0, "peak_allocated_mib": 0.0},
            "objects": entries, "picks": picks, "dropped": []}


def manifest(layout, room_id, observed):
    w,h,d = layout["dimensions_m"]
    outline = layout["outline_xz_m"]
    def polygon(ident,role,points,colour):
        return {"id": "shell:"+ident, "role": role,
                "geometry": {"kind": "polygon", "points_m": [[round(v,3)+0.0 for v in p] for p in points], "thickness_m": .12},
                "collides": True, "material_role": {"floor":"concrete","ceiling":"plaster","wall":"painted_wall"}[role], "base_color": colour}
    slab_outline = outline
    if len(outline) == 4:
        slab_outline = [[-w/2-.12,-d/2-.12],[w/2+.12,-d/2-.12],
                        [w/2+.12,d/2+.12],[-w/2-.12,d/2+.12]]
    floor_outline = [slab_outline[0]]+slab_outline[:0:-1]
    parts = [polygon("floor","floor",[[x,0,z] for x,z in floor_outline],layout["floor_colour"]),
             polygon("ceiling","ceiling",[[x,h,z] for x,z in slab_outline],layout["ceiling_colour"])]
    for i,(a,b) in enumerate(zip(outline,outline[1:]+outline[:1])):
        if len(outline) == 4 and i in (0,2):
            direction = 1 if i == 0 else -1
            a,b = [a[0]-.12*direction,a[1]],[b[0]+.12*direction,b[1]]
        points = [[a[0],0,a[1]],[b[0],0,b[1]],[b[0],h,b[1]],[a[0],h,a[1]]]
        parts.append(polygon("wall_"+chr(97+i),"wall",points,layout["wall_colour"]))
    # Authored closed door and fixed window, within their host polygons.
    door_width = 2.6 if layout["id"] == "garage" else .85
    openings = [{"id": "entry", "kind": "garage_door" if layout["id"] == "garage" else "door",
                 "host_part_id": "shell:wall_a", "center_m": [w/2-door_width/2-.15,1.05,-d/2],
                 "size_m": [door_width,2.1], "state": "closed", "traversable": False},
                {"id": "window", "kind": "window", "host_part_id": "shell:wall_b",
                 "center_m": [w/2,1.6,-d/4], "size_m": [1.0,1.0], "state": "fixed", "traversable": False}]
    # Clear of every observed floor footprint, 0.3 m clearance, one metre apart.
    spawns = []
    for zi in range(math.ceil((-d/2+.5)*10),math.floor((d/2-.5)*10)+1,5):
        for xi in range(math.ceil((-w/2+.5)*10),math.floor((w/2-.5)*10)+1,5):
            x,z = xi/10,zi/10
            if layout["id"] == "awkward_l" and x > -.5 and z > -.5:
                continue
            blocked = any(e["placement"]["support"]["kind"] == "floor" and
                          e["box"]["footprint_m"][0]-.3 <= x <= e["box"]["footprint_m"][2]+.3 and
                          e["box"]["footprint_m"][1]-.3 <= z <= e["box"]["footprint_m"][3]+.3
                          for e in observed["objects"])
            if not blocked and all(math.hypot(x-s["position_m"][0],z-s["position_m"][2]) >= 1 for s in spawns):
                role = "player" if not spawns else "companion"
                spawns.append({"id": role+"_start", "role": role, "position_m": [x,0.0,z], "yaw_deg": 0.0})
                if len(spawns) == 2:
                    break
        if len(spawns) == 2:
            break
    if len(spawns) != 2:
        raise ValueError(f"{room_id}: no two clear synthetic spawns")
    return {"schema": "enfractal.room", "version": 1, "room_id": room_id,
            "display_name": layout["id"].replace("_"," ").title(),
            "description": "Invented synthetic shell only. " + "; ".join(layout["tests"]),
            "created_utc": "2026-10-07T00:00:00Z", "units": "m", "axes": "y_up_neg_z_forward",
            "source": {"kind": "procedural", "pipeline": {"name": "brief14-synthetic-corpus", "version": "1"}},
            "bounds": {"min_m": [-w/2,0.0,-d/2], "max_m": [w/2,h,d/2]},
            "shell": {"parts": parts, "openings": openings}, "objects": [], "spawns": spawns,
            "light_hints": [{"id": "ceiling_light", "kind": "ceiling_lamp", "position_m": [0,h-.15,-.5],
                             "color": "#fff1d2", "relative_intensity": .8, "estimated": True}], "files": []}


def generate(output=OUTPUT):
    output = Path(output).resolve()
    os.makedirs(output,exist_ok=True)
    index = {"format": "brief14-corpus-v1", "synthetic": True, "bounds": BOUNDS,
             "sources": SOURCES, "rooms": []}
    sheet = Canvas(640*4,480*2)
    for number,layout in enumerate(layouts()):
        for seed in (None,)+SEEDS:
            variant = "nominal" if seed is None else f"scan_{seed}"
            room_id = layout["id"]+"_"+variant
            objects,shift = (copy.deepcopy(layout["objects"]),[0,0,0]) if seed is None else imperfect(layout,seed)
            inv = inventory(layout,objects,room_id)
            shell = manifest(layout,room_id,inv)
            # A separate oracle. Consumers must not use these extra semantic/void fields.
            truth = {"format": "brief14-truth-v1", "room_type": layout["id"], "variant": variant,
                     "seed": seed, "objects": layout["objects"], "neighbour_groups": layout["neighbour_groups"],
                     "tests": layout["tests"], "outline_xz_m": layout["outline_xz_m"],
                     "dimensions_m": layout["dimensions_m"], "assumptions": layout["assumptions"],
                     "lamp_channel_shift": shift, "bounds": BOUNDS}
            folder = output/room_id
            os.makedirs(folder,exist_ok=True)
            for name,document in (("room.json",shell),("inventory.json",inv),("truth.json",truth)):
                (folder/name).write_bytes(json_bytes(document))
            preview = plan(layout,inv,variant)
            picture = preview.png()
            if len(picture) >= 150_000:
                raise ValueError(f"{room_id}: preview over limit")
            (folder/"preview.png").write_bytes(picture)
            if seed is None:
                sheet.paste(preview,640*(number%4),480*(number//4))
            files = {name:hashlib.sha256((folder/name).read_bytes()).hexdigest()
                     for name in ("room.json","inventory.json","truth.json","preview.png")}
            index["rooms"].append({"id": room_id,"room_type": layout["id"],"variant": variant,"seed": seed,
                                   "object_count": len(objects),"files_sha256": files})
    picture = sheet.png()
    if len(picture) >= 1_500_000:
        raise ValueError("contact sheet over limit")
    (output/"contact-sheet.png").write_bytes(picture)
    (output/"index.json").write_bytes(json_bytes(index))
    return index


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",type=Path,default=OUTPUT)
    args = parser.parse_args()
    index = generate(args.output)
    print(f"Generated {len(index['rooms'])} rooms: 8 layouts x (nominal, scan_17, scan_73)")
    print(f"Synthetic only; standard library; GPU seconds 0; API cost $0")
    print(f"Output: {args.output.resolve()}")


if __name__ == "__main__":
    main()
