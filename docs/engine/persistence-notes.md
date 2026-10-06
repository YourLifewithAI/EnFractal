# Persistence rules carried forward from the save/travel service

The PostgreSQL save/travel service was removed in the room-scale cleanup (restorable from the `geography-era-final` tag). Its storage engine does not fit a single-player room game, but the rules it enforced do. File-based room saves must honor them from the start so that a later multiplayer service can adopt the same semantics instead of retrofitting them.

## Rules

- **Every mutation carries a unique action ID and yields a durable receipt.** Repeating an action ID with identical content returns the original receipt. Repeating it with different content is refused. This is already how `creation_authority.gd` and `world_state.gd` behave; room saves must persist those receipts with the state.
- **Writes are atomic and fenced.** A save is written to a temporary file and renamed into place; a reader never sees a partial file. Each save records a store revision and the session that wrote it; a stale session (an older process, a crashed run) cannot overwrite a newer revision.
- **A checkpoint precedes any transfer.** Before leaving a room (and later, before crossing a portal), the source room is saved and that checkpoint is referenced by the transfer. Arrival is acknowledged only after the destination has been materialized; until then the player can be returned to the source checkpoint.
- **Uncertain results are reconciled, not retried blindly.** If a write's outcome is unknown (crash mid-save, interrupted rename), the next start reads the authoritative state and the receipt ledger and resolves the action from them. Old commands are never replayed into a new session.
- **Bounded quotas with a reserved recovery path.** Receipts, history and save sizes have caps. A small fixed set of recovery operations (load, return to a safe spawn, restore a checkpoint) must remain available even when ordinary quotas are exhausted.
- **Movement and effect state are never persisted.** Saves hold entities, creations, locks, identities, pins and receipts; a loaded room restores a checked spawn point and resets consent for active effects.
- **The trusted save path is not the AI's path.** The companion adapter never receives save-file access or raw envelopes; all its changes go through the same command path as manual edits.

## Shape of a room save (proposal for R0)

```text
user://rooms/<room_id>/
  state.json         schema, version, room manifest pin, store revision, session, entities, locks, receipts
  state.json.tmp     atomic write staging
  checkpoints/       dated snapshots referenced by transfers and undo
```

Size caps and quota numbers are set in R0 from measured room content, not copied from the old service.
