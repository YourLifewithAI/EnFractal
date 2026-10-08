"""Authored truth, never captured data. All unreferenced dimensions are assumed.

Sizes are local [width, height, depth], positions bottom-centre, yaw Godot +Y.
Unsupported semantic labels stay here, outside the C4 inventory vocabulary.
"""

SOURCES = {
    "assumed": {"status": "assumed", "note": "Invented room sizes, placements, palettes and all furniture not explicitly sourced; no real room was consulted."},
    "micke": {"status": "verified", "url": "https://www.ikea.com/gb/en/p/micke-desk-white-80213074/", "size_whd_m": [1.05, 0.75, 0.5]},
    "billy": {"status": "verified", "url": "https://www.ikea.com/gb/en/p/billy-bookcase-white-00263850/", "size_whd_m": [0.8, 2.02, 0.28]},
    "kivik": {"status": "verified", "url": "https://www.ikea.com/es/en/p/kivik-3-seat-sofa-frame-00519361/", "size_whd_m": [2.28, 0.83, 0.95]},
    "malm": {"status": "verified", "url": "https://www.ikea.com/be/en/p/malm-bed-frame-high-white-stained-oak-veneer-s19022549/", "size_whd_m": [1.76, 1.0, 2.09], "underbed_height_m": 0.21},
    "nkba": {"status": "verified", "url": "https://nkba-ps.com/images/downloads/Awards/nkba_kitchen_planning_guidelines_pre_2023.pdf", "note": "Pages 4 and 6: work aisle 42 inches (1.0668 m) for one cook, walkway 36 inches (0.9144 m). Guidance, not a compliance claim."},
}


def obj(ident, kind, size, x, z, colour, *, y=0, yaw=0, on=None,
        label=None, confidence=0.86, basis="assumed", void=None):
    out = {"id": ident, "kind": kind, "size_m": list(size),
           "position_m": [x, y, z], "yaw_deg": yaw, "colour": colour,
           "semantic_label": label or kind, "confidence": confidence,
           "size_basis": basis, "support_target": on}
    if void:
        # Local bottom-centre and local box size: oracle only, not scan output.
        out["void"] = {"position_local_m": list(void[0]), "size_m": list(void[1]),
                       "basis": "malm" if basis == "malm" else "assumed"}
    return out


def room(ident, dims, objects, tests, *, group=None, l_shape=False):
    w, h, d = dims
    outline = [[-w/2, -d/2], [w/2, -d/2], [w/2, d/2], [-w/2, d/2]]
    if l_shape:
        outline = [[-w/2, -d/2], [w/2, -d/2], [w/2, 0], [0, 0], [0, d/2], [-w/2, d/2]]
    return {"id": ident, "dimensions_m": list(dims), "outline_xz_m": outline,
            "objects": objects, "tests": tests, "neighbour_groups": [group] if group else [],
            "floor_colour": "#a28a71" if ident != "garage" else "#96938c",
            "wall_colour": "#ded8cb", "ceiling_colour": "#ece8df",
            "assumptions": "Room dimensions, palette, placement and unspecified sizes/clearances are assumed."}


def layouts():
    garage = room("garage", (6, 2.7, 7), [
        obj("couch_1", "couch", (2.28,.83,.95), -1.5,-2.95,"#50757b", yaw=180,basis="kivik"),
        obj("bean_bag_1", "bean bag", (.9,.65,.9), -.9,-1.35,"#a76249"),
        obj("bean_bag_2", "bean bag", (.85,.6,.85), .25,-1.2,"#606485"),
        obj("bicycle_1", "bicycle", (1.75,1.05,.5), 2.65,-1.9,"#39444d",yaw=90),
        obj("shelving_unit_1", "shelving unit", (.8,2.02,.28), 2.53,2.95,"#957c60",basis="billy"),
        obj("desk_1", "desk", (1.05,.75,.5), 1.35,3.2,"#a58159",basis="micke",void=((0,0,0),(.6,.61,.38))),
        obj("office_chair_1", "office chair", (.6,1.05,.6), .35,3.12,"#303842"),
        obj("laptop_1", "laptop", (.36,.04,.26), 1.25,3.2,"#343842",y=.75,on="desk_1"),
        obj("monitor_1", "monitor", (.52,.42,.12), 1.58,3.35,"#272b33",y=.75,on="desk_1"),
        obj("jar_1", "jar", (.09,.12,.09), 2.53,2.95,"#b5c8c8",y=2.02,on="shelving_unit_1"),
        obj("table_1", "table", (1.1,.72,.7), -1.65,1.35,"#98734d",void=((0,0,0),(.85,.65,.5))),
        obj("french_press_1", "french press", (.17,.245,.11), -1.4,1.35,"#b0b6bd",y=.72,on="table_1"),
        obj("cardboard_box_1", "cardboard box", (.45,.3,.36), -2.5,.25,"#b38a55"),
        obj("storage_tote_1", "storage tote", (.6,.38,.4), 2.45,.15,"#73828b"),
        obj("easel_1", "easel", (.65,1.65,.6), -2.55,2.6,"#b09573"),
        obj("bin_1", "bin", (.35,.5,.35), 2.55,1.15,"#454d45"),
    ], ["cluttered mixed scales", "wall/corner objects", "all five existing recipes", "desk-chair-bookcase group"],
        group=["office_chair_1", "desk_1", "shelving_unit_1"])
    bedroom = room("bedroom", (4.6,2.5,4.8), [
        obj("couch_1", "couch", (1.76,1,2.09), -.7,-1.3,"#8d9ca7",label="bed",confidence=.28,basis="malm",void=((0,0,0),(1.55,.21,1.8))),
        obj("cabinet_1", "cabinet", (.45,.55,.4), .75,-1.85,"#bd9c72",label="bedside cabinet"),
        obj("lamp_1", "lamp", (.2,.35,.2), .75,-1.85,"#e0bd81",y=.55,on="cabinet_1"),
        obj("cabinet_2", "cabinet", (1.05,2,.6), 1.65,1.85,"#c0b2a2",label="wardrobe"),
        obj("chest_of_drawers_1", "chest of drawers", (1.1,.85,.45), -1.65,2,"#a38669"),
        obj("basket_1", "basket", (.4,.35,.4), -1.8,.35,"#b79b72"),
    ], ["bed absent from C4 vocabulary: low confidence fallback", "underbed void", "stacked lamp", "large open floor"])
    kitchen = room("kitchen", (4.8,2.6,5), [
        obj("cabinet_1", "cabinet", (1.8,.9,.6), -1.2,-2.15,"#d4cbbb",label="counter"),
        obj("cabinet_2", "cabinet", (.7,1.85,.7), 1.85,-2.05,"#b9bdc2",label="refrigerator",confidence=.3),
        obj("cabinet_3", "cabinet", (.6,.9,.6), .25,-2.15,"#414751",label="oven",confidence=.32),
        obj("table_1", "table", (1.4,.76,.8), -.45,.2,"#ad885c",void=((0,0,0),(1.15,.68,.6))),
        obj("office_chair_1", "office chair", (.5,.9,.5), -1.5,.2,"#9c8770",label="dining chair"),
        obj("office_chair_2", "office chair", (.5,.9,.5), .6,.2,"#9c8770",label="dining chair",yaw=180),
        obj("kettle_1", "kettle", (.2,.25,.2), -1.7,-2.15,"#c2c9ca",y=.9,on="cabinet_1"),
        obj("jar_1", "jar", (.09,.14,.09), -1.25,-2.15,"#96b3a6",y=.9,on="cabinet_1"),
        obj("mug_1", "mug", (.1,.11,.09), -.8,-2.15,"#ad6d54",y=.9,on="cabinet_1"),
        obj("french_press_1", "french press", (.17,.245,.11), -.7,.2,"#676e72",y=.76,on="table_1"),
        obj("bin_1", "bin", (.35,.55,.35), -2.12,2.15,"#505655"),
    ], ["counter-supported vessels", "missing appliance labels", "1.65 m counter/table work aisle (NKBA >=1.0668 m)", "table void"])
    living = room("living_room", (5.5,2.7,5.8), [
        obj("couch_1", "couch", (2.28,.83,.95), -.65,-2.375,"#b78376",yaw=180,basis="kivik"),
        obj("table_1", "table", (1.1,.42,.6), -.65,-.95,"#ab8b63",void=((0,0,0),(.85,.34,.42))),
        obj("shelving_unit_1", "shelving unit", (.8,2.02,.28), 2.35,2.71,"#a89983",basis="billy",void=((0,.15,0),(.66,.3,.24))),
        obj("speaker_1", "speaker", (.2,.7,.25), 2.4,-1.9,"#303844"),
        obj("bean_bag_1", "bean bag", (1,.7,1), -2,1.7,"#748a74"),
        obj("plant_1", "plant", (.4,.85,.4), -2.3,-2.25,"#587655"),
        obj("board_game_1", "board game", (.3,.07,.25), -.65,-.95,"#b87e48",y=.42,on="table_1"),
        obj("lamp_1", "lamp", (.35,1.5,.35), 2.25,1.4,"#dcc8a1"),
    ], ["cohesive warm palette", "low table versus high shelves", "shelf interior void absent from box", "separated seating islands"])
    office = room("home_office", (4.4,2.5,4.4), [
        obj("office_chair_1", "office chair", (.6,1.1,.6), -1.6,-1.78,"#354450"),
        obj("desk_1", "desk", (1.05,.75,.5), -.6,-1.9,"#bbac94",basis="micke",void=((0,0,0),(.6,.61,.38))),
        obj("shelving_unit_1", "shelving unit", (.8,2.02,.28), .52,-2.01,"#c1b398",basis="billy",void=((0,.15,0),(.66,.3,.24))),
        obj("laptop_1", "laptop", (.36,.04,.26), -.75,-1.88,"#3a444e",y=.75,on="desk_1"),
        obj("mug_1", "mug", (.1,.11,.09), -.25,-1.88,"#699089",y=.75,on="desk_1"),
        obj("cabinet_1", "cabinet", (1.1,.8,.55), 1.6,1.7,"#ac9c86"),
        obj("printer_1", "printer", (.4,.23,.3), 1.6,1.7,"#dee0dc",y=.8,on="cabinet_1"),
        obj("cardboard_box_1", "cardboard box", (.45,.3,.35), -.9,1.65,"#b28b57"),
        obj("cardboard_box_2", "cardboard box", (.32,.2,.25), -.9,1.65,"#aa804c",y=.3,on="cardboard_box_1"),
    ], ["chair-desk-bookshelf side by side", "two boxes stacked", "surface electronics", "desk and shelf voids"],
        group=["office_chair_1", "desk_1", "shelving_unit_1"])
    workshop_objects = [
        obj("desk_1", "desk", (2,.85,.85), 0,-2.3,"#af8a5d",label="workbench",void=((0,0,0),(1.6,.73,.65))),
        obj("shelving_unit_1", "shelving unit", (1.2,1.8,.45), -2.3,1.8,"#6b7b80",yaw=90,void=((0,.18,0),(.95,.35,.38))),
        obj("storage_tote_1", "storage tote", (.55,.35,.4), 1.85,1.4,"#708c82"),
        obj("step_ladder_1", "step ladder", (.55,1.5,.6), 2.1,-1.8,"#9daba9"),
        obj("office_chair_1", "office chair", (.55,.95,.55), 0,-1.25,"#535e65"),
    ]
    # 36 small objects: repeated kinds and colours, separated and nested heights.
    for i in range(36):
        kind, size = (("paint can", (.14,.18,.14)), ("jar", (.085,.12,.085)),
                      ("mug", (.1,.11,.09)), ("bottle", (.09,.25,.09)))[i % 4]
        if i < 18:
            x, z, y, on = -.78 + (i % 6)*.30, -2.57 + (i//6)*.26, .85, "desk_1"
        else:
            j = i-18
            x, z, y, on = -.9 + (j%6)*.34, .4 + (j//6)*.38, 0, None
        workshop_objects.append(obj(f"{kind.replace(' ', '_')}_{i+1}",kind,size,x,z,
                                    ("#b25f44","#809f9b","#c9b073","#738ab0")[i%4],y=y,on=on))
    workshop = room("workshop", (5.2,2.6,5.6), workshop_objects,
                    ["41 objects, 36 small", "repeated labels and colours", "workbench supports", "small floor clutter"])
    empty = room("near_empty", (5,2.8,6), [
        obj("office_chair_1", "office chair", (.6,1,.6), -1.9,-2.55,"#507b91",yaw=25),
        obj("plant_1", "plant", (.35,.6,.35), 2.1,2.6,"#5c7a4c"),
    ], ["sparse input: only two picks", "dominant empty floor", "non-cardinal yaw"])
    awkward = room("awkward_l", (6,2.4,6), [
        obj("desk_1", "desk", (1.05,.75,.5), 1.3,-2.7,"#bca080",basis="micke",void=((0,0,0),(.6,.61,.38))),
        obj("office_chair_1", "office chair", (.6,1,.6), 1.3,-1.6,"#657982"),
        obj("shelving_unit_1", "shelving unit", (.8,2.02,.28), -2.79,2.2,"#9b8261",yaw=90,basis="billy"),
        obj("cabinet_1", "cabinet", (.8,.9,.5), -2.5,-.5,"#bdab94"),
        obj("suitcase_1", "suitcase", (.45,.65,.3), -1.3,1.5,"#695269",yaw=35),
    ], ["concave floor", "re-entrant corner", "AABB includes absent quadrant", "contract topology beyond current C3 box fitter"],l_shape=True)
    return [garage, bedroom, kitchen, living, office, workshop, empty, awkward]
