# Local world-physics rule fixture

The Barton Creek test controller now accepts a validated, revisioned physics profile. The default reproduces its original movement: 22 m/s² gravity, 4 m/s² gliding gravity, and no wind. Two developer fixtures exercise lighter gravity and a 4 m/s eastward breeze. These are test profiles, not claims about real Barton Creek weather or authorized sandbox rules.

The profile bounds gravity to 4–30 m/s², gliding gravity to 0.5 m/s² through the normal gravity, and horizontal wind to 8 m/s. A non-finite, oversized, malformed, or stale revision is rejected without changing the active profile. Wind affects airborne target velocity only; the existing horizontal acceleration and terminal fall-speed limits still apply. The [world physics smoke test](../../../game/tests/world_physics_smoke.gd) checks invalid input, revision ordering, different fall rates, and bounded airborne drift.

This is an E07 **adapter seam**, not the sandbox physics system. The local viewer does not expose profile editing to players. A future authoritative world transition must assign the destination's rule revision, validate it with the same game rule owner, and activate it in step with collision and portal state. Visual effects, network prediction, and return-home restoration remain open.
