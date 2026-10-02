# Travel menu

The travel menu presents the local Phase 4 loop: create a free sandbox, review its different rules, visit, and return to Home Earth. The runtime and persistent host service own every state change. The panel does not grant access, copy inventory, write saves, or move an avatar directly.

## Presentation

The menu continues the invention workshop's palette: deep water `#0d2024`, pine `#172e2a`, panel green `#203c38`, stone `#e9e6d8`, sage `#acc8b3`, and copper `#d9a879`. It uses Godot's existing project font with a 27-pixel menu title, 24-pixel destination name, and 13–17-pixel supporting text. Left-aligned destinations and rules carry the hierarchy; the copper primary action means “review this visit.” No animated preview or extra world instance is created.

```text
Worlds & journeys                                      Close
You are in Home Earth

Destinations              Selected world
Home Earth                Ownership and separate saves
Your free sandbox         Gravity and behavior rules
                          Inventory separation
                          Availability and occupants
                          Sandbox invitations / local guest disclosure
Create free sandbox

Save or recovery state
Action result                         Return Home   Review visit
```

The footer stays visible at 1280×720 and 900×600. At the smaller size, destination details scroll; Return Home, Review visit, the create control, and action feedback remain outside that scroll. Buttons have visible keyboard focus and selected destinations have dark text on sage.

The sandbox summary explicitly describes quarter gravity, independently saved builds and equipment, and a fresh geographic base. Visit review explains that Home possessions stay at Home and sandbox powers do not become Home powers. Invitations are labeled as belonging to the sandbox even when the selected destination is Home. The guest is explicitly a local test identity; this menu does not represent remote accounts or an online invitation system.

## Runtime boundary

[`travel_panel.gd`](../../../game/scripts/travel_panel.gd) is a `CanvasLayer`. Assign its `runtime` before adding it to the scene tree. `open_panel()` shows it and calls `runtime.set_panel_open(true)`; `close_panel()` releases that modal gate. Closing is temporarily blocked during an active operation. Escape first dismisses an open visit review, then closes the menu. The map's earlier input handler must route Escape to the open panel before its own game controls.

`runtime.travel_status()` provides:

```text
ok: bool
current_world: { id, name, gravity_scale, owner_id }
worlds: [ { id, name, gravity_scale, owner_id, status, can_visit, occupants } ]
presence: { world_id, epoch }
pending: Dictionary
message: String
```

Home has the canonical ID `home`. An owned sandbox has another ID and `owner_id == "local_player"`. `occupants` may be a count or an array of identities. A nonempty `pending` means a journey needs recovery. A failed ordinary status blocks new visits, while Return Home stays available so the runtime can reconnect and recover.

Actions return dictionaries containing `ok` and a user-readable `message`:

- `create_sandbox()`
- `visit_world(world_id)`
- `return_home()`
- `invite_guest()`
- `revoke_guest()`

The panel marks an operation busy immediately, disables repeated actions, and gives the saving message a frame to draw before calling the runtime. A synchronous result and an asynchronous Godot method are both supported. The returned message conveys the actual save outcome; merely obtaining a healthy status does not produce an “all changes saved” claim.

Before a visit, a confirmation dialog displays rules, saving behavior, and inventory adaptation. Confirming re-reads status and compares the displayed destination ID, name, gravity, owner, lifecycle status, and admission flag. A change requires another review. This is a presentation check: the host must independently validate the rules, admission, invitation, epoch, inventory adaptation, and commit at the actual transfer boundary.

The selected destination and last action failure survive polling and closing/reopening. Failed visits can be retried. An interrupted return exposes both recovery feedback and the always-available Return Home action; it does not trap recovery behind portal geometry. Closed menus stop polling and allocate no rendering viewport or physics world.

## Verification and limits

[`travel_panel_smoke.gd`](../../../game/tests/travel_panel_smoke.gd) runs 32 checks against a deliberately small runtime fixture. It exercises Home-only and sandbox states, repeated create/confirm signals, changed-rule reconfirmation, failed visits, pending and failed returns, invitation/revocation delegation, Escape, and modal input release. Its resize checks disable project canvas scaling so 900×600 tests the actual smaller layout rather than a scaled 1280×720 image.

The fixture passed headless and on the Compatibility renderer with an RTX 2070 Super. The actual 900×600 and 1280×720 menu captures were inspected. These checks establish presentation behavior only. Persistent host fault tests, runtime transfers, real player movement, restart recovery, and real map screenshots belong to separate integration checks. No claim of remote multiplayer, external authentication, target-laptop performance, hosted storage, or off-host backup follows from this UI fixture.
