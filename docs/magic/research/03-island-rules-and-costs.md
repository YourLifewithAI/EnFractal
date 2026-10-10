# Report 3: per-island rule packs over one engine (condensed by the integrator; the schema sketch is kept)

The agent worked from search summaries. Its data shapes were fetched raw from GitHub:
- Factorio's planet and recipe Lua;
- Minecraft's 26.4 snapshot data (6 Oct 2026, from mcmeta);
- Bedrock's samples.

It marked anything from memory. Its cost was $0.

## What a world pack contains
1. Identity, version, dependencies and a minimum engine version (the Bedrock manifest).
2. **World laws as typed values.**
   - Minecraft's dimension_type: the Nether has `water_evaporates`, `fast_lava`, `bed_rule`.
   - Factorio's planets have `surface_properties` (gravity, pressure, magnetic field, solar power, day length).
3. **Content that tests the laws.** A Factorio recipe declares `surface_conditions` (pressure min and max) and the engine checks them against the planet. The recipe runs no code. That is "abilities work through the island's laws".
4. **Cost and scaling data.**
   - Minecraft enchantments: `base` plus `per_level_above_first`.
   - Bedrock `tameable`: probability, items and an event.
5. **The vocabulary.** Fate picks its own skills, Cypher renames its types, and a PbtA playbook adds its own moves.

**Fate's Bronze Rule (the Fate Fractal):** anything can be a character. The island, a region and a dragon share one component shape. Only things that act carry abilities (Macklin's limit on the rule).

## Layering
- **Knobs on a fixed core, for a strong shared feel.**
  - Fortnite's Island Settings and Class Designer default every field to "Don't Override". The Energy pool settings are max, recharge amount and recharge delay. The core movement is Epic's.
  - Mario Maker: each style carries its own abilities.
- **Data with patching, for a moderate feel.** RimWorld XPath patches, Starbound RFC 6902 JSON Patch, Minecraft's higher-pack-wins with add-only tags, and Factorio's data stages, frozen before runtime.
- **Code override, for a weak feel.** Garry's Mod and Roblox scripts.
- **What a platform keeps whatever the world does.**
  - Fortnite: no island exceeds its content ceiling. An IARC rating is shown before entry, outfits auto-swap for lower-rated islands, and a memory cap of 100k units blocks publishing.
  - Roblox: its own text filter and system menu.
- **Lesson:** the feel survives where worlds change values and content, not code. **Three merge modes:** engine rules FIXED, engine tunables NARROW-ONLY, content ADD-ONLY.

## Re-skinning a verb by theme
- **Savage Worlds:** five arcane backgrounds share one power list. Trappings are flavour plus a small rule, so *bolt* can be fire, ice or bees.
- **GURPS:** special effects are free; the *source* (magic, chi, psi) changes the price and the counters.
- **Mario Maker** swaps an item slot by style: the Weird Mushroom becomes a Leaf, a Feather or a Propeller.
- **RimWorld "stuff":** one wall definition, with the material setting its label, colour and stats.
- **The Strange:** each recursion runs under a law (Standard Physics, Magic, Mad Science). A visitor keeps abilities only where the local law supports them. That is the model for visiting islands.
- **PbtA:** fixed basic moves; playbooks only add.
- **Sanderson's Third Law:** expand what you have before you add. One `light.emit` is a firefly lantern on one island and a floodlight on another. The command and physics are the same; the name, asset and VFX change, the bounds may only narrow, and the island sets the price.

## Costs that splitting can't evade
- **Sanderson's Second Law:** limitations > powers. His First Law: show cost and bounds before acting.
1. **Price the resolved effect, not the command.** The engine measures it: water m³ added, terrain m³ moved, kg moved, lit m²·s, creature tier, entities created, thrust N, damage. An island sets rates on these measures and cannot invent new ones.
2. **Per command: `base_fee + rate × measure` only (subadditive).** Escalation applies only to a cumulative total in a declared scope (team × ability family × island × window), paid at the margin: f(after) − f(before). Precedent: the Minecraft anvil's prior-work penalty, and Ethereum's 21k base gas.
3. **Charge capabilities when they emerge.** A ship pays for flight or thrust when its assembly gains it. A dragon is priced by tier, with a cap on how many are alive at once and optional upkeep.
4. **One wallet per team** (player plus Gubble), so routing work through the AI evades nothing.
5. **Caps per window, and a floor on discounts.** Minecraft refuses anvil jobs of 40+ levels in Survival but not Creative. GURPS caps total limitations at −80%.
6. **Undo refunds no more than was paid,** only within its window, and reclaims what the action produced.
7. **Meter kinds belong to the engine** (pool, stock, bond, cooldown, consequence); **names belong to the island.** In Mage, vulgar magic draws Paradox. That gives a rule for visitors: an off-theme ability is translated, refused or charged a dissonance multiplier.
8. **Scaling:** linear, or base plus per-level, within an ability; a table across tiers. The validator checks curves are monotone.

## The schema sketch (kept nearly verbatim)
```jsonc
// LAYER 1 ENGINE: ships with the build, never in a pack
"engine": {
  "fixed": {
    "avatar": {"height_m": 0.10, "locomotion": "walk.v1", "camera": "follow.v1"},
    "physics": {"gravity_mps2": 9.81, "water": "flows_downhill", "solver": "engine"},
    "core_verbs": ["walk","jump","grab","release","place","push","stack","observe","undo","stop","checkpoint"],
    "command_path": "enfractal.command",
    "player_only_ops": ["protect.unlock","world.set_physics"],
    "safety": {"content_ceiling": "everyone_10", "pack_text": "untrusted display_text", "pack_code": "none"},
    "perf_caps": {"entities_max": 2000, "water_sources_max": 16}
  },
  "tunables": { // NARROW-ONLY
    "jump_height_m": {"min": 0.02, "max": 0.06, "default": 0.05},
    "fall_damage": {"allowed": ["off","soft"], "default": "off"},
    "carry_kg": {"player": {"max": 0.5}, "companion": {"max": 2.0}}
  },
  "measures": ["water_m3_added","terrain_m3_moved","mass_kg_moved","light_m2s","entities_created","creature_tier","thrust_n","damage_hp"],
  "primitives": {
    "light.emit": {"radius_m": [0.05, 3], "lux": [1, 2000], "kelvin": [1500, 9000], "duration_s": [1, 3600]},
    "water.source": {"flow_lps": [0.001, 0.5], "duration_s": [1, 600]},
    "creature.summon": {"tier": [1, 5]},
    "assembly.enable": {"capabilities": ["float","fly","thrust","emit_projectile"]},
    "force.project": {"impulse_ns": [0, 0.5], "range_m": [0, 2]}
  },
  "meter_kinds": ["pool","stock","bond","cooldown","consequence"],
  "agent_defaults": {"needs_click": ["creature.summon","assembly.enable"]}
}
// LAYER 2 ISLAND RULES: a pinned, hashed file, like a style preset
"island_rules": {
  "id": "dragon_isle", "version": 3, "engine_min": "1.0.0",
  "theme": {"name": "Dragon Isle", "style_preset": "storybook_painterly/v2"},
  "laws": {"magic_source": "draconic", "water_evaporates": false, "fire_spreads": "slow"},
  "tunables": {"jump_height_m": 0.04, "fall_damage": "soft"},            // NARROW-ONLY
  "meters": [                                                            // ADD-ONLY
    {"id": "ember", "kind": "pool", "label": "Ember", "max": 100, "regen_per_s": 0.5, "regen_delay_s": 3},
    {"id": "scales", "kind": "stock", "label": "Dragon scales"},
    {"id": "trust", "kind": "bond", "per": "creature", "label": "Trust", "range": [0, 10]}
  ],
  "materials": [{"id": "basalt", "physics_material": "stone", "label": "Basalt"}],
  "creatures": [{"id": "dragon", "tier": 4, "verbs": ["feed","groom","ride","spar"]}],
  "abilities": ["ember_lantern", "call_spring", "summon_dragon"],
  "modes": {"creative": {"prices": "off"}, "challenge": {"prices": "on"}, "coop": {"prices": "on", "wallet": "shared"}, "versus": {"prices": "on", "combat": true}},
  "visitors": {"foreign_abilities": "translate_by_primitive_or_refuse", "dissonance_multiplier": 2.0},
  "agent_rules": {"auto_ok": ["ember_lantern"], "needs_click": ["call_spring", "summon_dragon"], "max_spend_per_min": {"ember": 30}}  // LAYER 4, NARROW-ONLY
}
// An island may NOT: carry scripts, URLs or paths; add primitives, measures or meter kinds; change the
// avatar, locomotion, physics, camera, core verbs, undo or stop; widen a tunable, envelope or agent
// default; expose player-only ops; or relax perf caps or the content ceiling.
// LAYER 3 ABILITY
"ability": {
  "id": "call_spring", "label": "Call a spring",
  "primitive": "water.source",
  "bounds": {"flow_lps": [0.001, 0.05], "duration_s": [1, 120]},       // inside the envelope
  "requires_laws": [{"law": "water_evaporates", "eq": false}],          // like Factorio surface_conditions
  "targets": {"on": ["terrain"], "not_on": ["avatar:*", "protected"], "in_sight": true},
  "counters": ["drought_ward"],
  "skin": {"vfx": "fx/spring_bubble", "sfx": "sfx/burble", "asset": null},
  "cost": "cost.call_spring"
}
// COST
"cost": {
  "id": "cost.summon_dragon",
  "base_fee": {"ember": 5},
  "terms": [{"meter": "ember", "measure": "creature_tier", "table": [0, 10, 25, 60, 150, 400]},
            {"meter": "scales", "measure": "entities_created", "rate": 3}],
  "gates": [{"meter": "trust", "of": "dragon", "min": 3}],
  "escalation": {"scope": ["team", "family:summon", "island"], "window_s": 900, "curve": {"type": "power", "exp": 1.5}, "pricing": "marginal"},
  "caps": {"per_window": {"ember": 300}, "concurrent": {"creature:dragon": 2}},
  "refund": {"on_undo": "paid_only", "within_s": 120, "reclaims_outputs": true},
  "capability_charge": null
}
```

**The pack validator checks:**
- each ability binds one primitive, with bounds inside its envelope;
- tunables and agent rules are equal to or narrower than the engine's;
- fees are ≥ 0 and tables are monotone;
- escalation declares a scope;
- labels pass the display_text rules, with no unknown fields;
- the pack is pinned by its byte hash.

**Integrator's note:** the sketch's `needs_click` predates the founder's no-click decision. Map it to the T2 and T3 tiers (preview-then-commit, a keyed "yes").

**Not covered:** Dreams and LittleBigPlanet. Fortnite and Verse movement limits and the RimWorld def fields are from memory and need checking.

**Key sources:**
- fate-srd.com (Fate Fractal, Bronze Rule);
- brandonsanderson.com (Sanderson's Second Law);
- raw.githubusercontent.com/wube/factorio-data (Space Age planet.lua, recipe.lua);
- misode/mcmeta (dimension_type, enchantment);
- Mojang/bedrock-samples;
- dev.epicgames.com (player settings, Class Designer, memory management, IARC);
- create.roblox.com (text filtering);
- rimworldwiki PatchOperations;
- starbounder (JSON Patch);
- EIP-2780.
