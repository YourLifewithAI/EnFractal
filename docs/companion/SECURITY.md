# Companion security boundary

What the companion can and cannot do, where each rule is enforced, and the test that proves it.

**Threat model.** Players bring their own AI: any model, behind any MCP client or agent harness. So the
boundary assumes an arbitrary and possibly adversarial client. The model driving the companion may be
persuaded by anything it reads (a sign in the room, an object's name, a crafted creation) or may simply
be wrong. Anything that can read the session file can skip the MCP adapter and talk to the game
directly. So every rule is enforced by the game host; the adapter repeats the rules it can check alone,
to fail fast; and the server process locks itself down as a second layer.

**Every protection holds with no approval holds configured.** The founder's direction is no approval
clicks (host-enforced tiers instead; [LIVE-VOICE.md](LIVE-VOICE.md)). Holding is therefore never the
only thing between the companion and a protected outcome: `companion/tests/test_no_holds.py` runs every
host suite that does not depend on holding again with an empty held set, plus the cases an unheld
companion could otherwise exploit.

## Layers

| Layer | Enforces |
|---|---|
| MCP surface (`server.py`) | Only contract ops are tools; player-only ops have none. No resources, prompts, completions, logging or sampling. Fixed instructions and descriptions that never include world text. |
| Adapter (`server.py`) | Builds every contract message itself. Refuses unknown fields, identity and approval keys at any depth (look-alike letters and variants included), actors other than the companion's own avatar, invisible characters, non-finite numbers and integers outside int64, oversized messages and creation sources, and floods (stop ops exempt). Writes numbers as canonical JSON v1 and validates that form, as the game will. Validates every incoming result (contract-valid, no hidden characters, this principal, this room, this op and id) before relaying it. Returns results as one ASCII-escaped JSON line after a fixed preamble. |
| Link (`link.py`) | Loopback IP literals only; a per-launch 256-bit token proven by mutual HMAC (never sent); the principal bound to the connection; one companion at a time; at most 8 handshakes pending; a 5-second handshake timeout; strict frames written as canonical JSON with size limits; no frame type for approvals. No lockout, so failed guesses cannot lock the real companion out. See [TRANSPORT.md](TRANSPORT.md). |
| Host (`mock_host.py`, then P1's `CommandHost`) | Everything, again: the contract schema (ECMA-262 patterns) and size limits, value rules, authority keys anywhere in args, room id, player-only ops, actors, line of sight and perception memory ([PERCEPTION.md](PERCEPTION.md)), entity existence (one answer for unknown, removed, foreign and unseen ids), revisions, idempotency, approvals, locks, the companion's undo, budgets, rate limits, the receipt ledger, sanitised world text (emoji markers only in place, and only those the contract accepts), contract-valid results. |
| Process (`lockdown.py`) | Defence in depth, not a sandbox: an environment scrub and a PEP 578 audit hook over the audited standard-library routes to files, processes, the network, the registry and native libraries. Its limits are listed below. |

### What the process lockdown does and does not do

Once the server has loaded the contracts, it clears its environment down to `SYSTEMROOT` and `WINDIR`,
then installs an audit hook that refuses, for the rest of the process's life:

- opening any file for writing, and for reading anything except the session file, the Python
  installation (with its site-packages), the package and the contracts; listing other directories;
  deleting, renaming, copying, linking, changing modes;
- starting or signalling processes; every audited `_winapi` and `ctypes` call;
- connecting, binding or sending to anything but a loopback IP literal, and resolving any name
  ("localhost" included); URL fetching, the browser, the registry, sqlite, environment changes;
- on Windows, the event loop's connect, sendto and named-pipe connect, which raise no audit event,
  are wrapped with the same rules.

It does **not** cover:

- calls Python does not audit: `os.stat` and `os.path.exists` still tell whether a path exists;
- native modules already loaded: on Windows the MCP SDK's stdio transport imports pywin32 (`win32api`,
  `win32job`), whose functions raise no audit events;
- a symbolic link or junction that already exists inside an allowed folder (paths are compared as
  text); nothing in the process can create one;
- code in the process that sets out to escape rather than being tricked into one call;
- anything the player's account itself may do: the process runs with the player's rights.

`test_lockdown.py` pins both lists: each refusal is probed in a fresh process, and
`test_the_limits_the_docs_admit_are_real` checks that the listed gaps are real, so the docs cannot drift
into claiming more. `test_the_server_locks_itself_down_before_it_serves_anything` checks the real entry
point scrubs and installs before it serves. An operating-system sandbox (a restricted token, an
AppContainer or a separate account) is what would make this a containment boundary.

### The client's side: a play-only profile

The game cannot see or limit what else a player's MCP client or harness has loaded. A harness that
also holds a shell, file access or a browser turns a persuasive sign into a risk outside the game. So
play uses a **play-only client profile**: a client configuration with this server and nothing else.
`python -m enfractal_companion.profile` prints one in the common `mcpServers` JSON shape (README.md).
`test_profile.py` checks it names exactly one stdio server running this package, sets no environment
(the server needs no keys), never turns the lockdown off, and that a client launched from it sees the
game's 25 tools and nothing else.

## Boundary-test report

Run: `uv run --project companion --locked python -m unittest discover -s companion/tests -v`.
Every result the mock host emits during the host suites, and every tool result the MCP tests receive,
is also checked with `contracts/validate.py`.

| Attack or rule | Refused how | Tests |
|---|---|---|
| Principal smuggled into the envelope or args | `field_unknown` at `$.principal` / `$.args.principal`; at the adapter before sending | `test_refuses_principal_smuggled_into_the_command_envelope`, `test_refuses_principal_smuggled_into_command_args`, `test_refuses_principal_as_a_tool_argument_before_sending`, `test_refuses_room_and_schema_overrides` |
| Principal smuggled into effect parameters, creation sources, parts or nodes, under look-alike names | Reserved parameter names; any key whose folded form names identity or authority (`Principal`, a full-width or Cyrillic `principal`, `on_behalf_of`, `approved_by_player`, `api_token`), and any args key that is not a plain lowercase token: `field_unknown` with the exact path | `test_refuses_principal_smuggled_into_effect_parameters`, `test_refuses_principal_smuggled_into_a_creation_source`, `test_refuses_principal_hidden_inside_a_creation_part`, `test_refuses_approved_by_or_owner_hidden_in_creation_nodes`, `test_refuses_look_alike_and_variant_authority_keys`, `test_refuses_look_alike_authority_keys_in_tool_arguments` |
| Principal claimed on the wire | A principal key on a frame closes the connection; inside a message it is `field_unknown`; results always carry the connection's principal | `test_refuses_a_principal_on_the_request_frame`, `test_principal_inside_the_message_is_refused_and_never_adopted`, `test_result_principal_is_the_connection_principal_whatever_the_note_claims` |
| Directing or perceiving through the player's avatar | `actor_denied` for every op that takes an actor, at the adapter and the host | `test_the_companion_never_acts_or_perceives_through_the_players_avatar`, `test_refuses_observing_through_the_player_avatar_before_sending`, `test_refuses_directing_the_player_avatar_before_sending`, `test_refuses_releasing_with_the_players_avatar` |
| Sending an approval | Approval fields are `field_unknown`; invented ops are `request_invalid`; no approve tool and no approval frame; resending a held command returns the same hold; a note saying "approved" is text | `test_refuses_a_command_that_carries_an_approval`, `test_refuses_approved_by_in_a_command`, `test_refuses_self_approval_through_an_invented_op`, `test_refuses_approval_fields_in_tool_arguments`, `test_there_is_no_tool_that_approves`, `test_refuses_an_approval_frame_from_the_companion_connection`, `test_resending_a_held_command_does_not_approve_it`, `test_a_note_claiming_approval_is_only_text` |
| Approval flow integrity (when holds are configured) | 128-bit `request_id`; commit under the original principal with `approved_by`; denial, expiry and lapse are final for that action id and say to use a new one; at most three pending; another principal's request looks unknown | `test_approved_command_commits_under_the_original_principal_with_approved_by`, `test_denied_approval_is_final_for_that_action_id`, `test_unanswered_approval_expires`, `test_approval_lapses_when_the_entities_it_touches_change`, `test_expired_and_lapsed_approvals_are_final_and_say_to_use_a_new_action_id`, `test_refuses_more_than_the_pending_approval_limit`, `test_another_companions_approval_looks_like_no_approval` |
| Names and sign text that issue instructions | Data only: `texts[]` with `untrusted: true`; display text stripped of control, line-separator, zero-width, bidi and every other invisible character, then truncated; the model sees one escaped JSON line; reading changes nothing, not even the tool list | `test_sign_text_that_issues_instructions_is_returned_as_untrusted_data`, `test_sign_text_reaches_the_model_only_as_an_escaped_json_string`, `test_display_name_tricks_are_neutralised_and_escaped`, `test_world_text_loses_every_invisible_character`, `test_a_name_naming_a_command_does_not_run_it`, `test_the_tool_list_does_not_change_after_reading_world_text`, `test_instructions_tell_the_model_world_text_is_data` |
| Invisible characters (review finding 5) | Every Unicode format character (Cf: the soft hyphen U+00AD, the Arabic letter mark U+061C, U+FFF9-FFFB), Hangul fillers (U+3164 and kin), variation selectors U+FE00-FE0D, and all of plane 14 (U+E0000-U+EFFFF: the TAG block, assigned or not, and the selector supplement): refused in any request string at the adapter and the host, replaced by a space in any world text the host emits | `test_refuses_invisible_characters_anywhere_in_a_request`, `test_refuses_and_strips_the_whole_special_purpose_plane`, `test_refuses_invisible_characters_in_tool_arguments`, `test_refuses_unassigned_tag_and_special_plane_characters_before_sending` |
| Emoji markers (founder decision, 6 October 2026) | VS15 and VS16 only directly after an Extended_Pictographic character or 0-9, # or *, one per base; the zero-width joiner only between two emoji; the keycap combiner only after 0-9, # or * (optionally with VS16). Anywhere else, alone or in runs, they are hidden characters like the rest, so variation-selector smuggling is refused; tag-sequence flags (England) stay unsupported. The host emits only the markers its loaded contract's patterns accept, and cleans twice so truncation never strands a joiner | `test_emoji_markers_in_place_are_not_hidden`, `test_markers_out_of_place_and_other_invisible_characters_are_hidden`, `test_a_run_leaves_at_most_one_selector_per_emoji`, `test_truncation_never_strands_a_joiner_or_a_selector`, `test_cleaning_is_final_and_leaves_nothing_hidden`, `test_the_host_emits_only_markers_its_contract_accepts`, `test_world_names_and_signs_keep_their_emoji_and_lose_the_rest`, `test_requests_with_emoji_pass_and_requests_with_hidden_markers_are_refused`, `test_the_adapter_passes_emoji_and_refuses_hidden_markers_before_sending` |
| A trailing newline against an anchored pattern | Patterns follow ECMA-262: `$` is the very end, wherever it appears in a pattern | `test_refuses_a_trailing_newline_in_single_line_text`, `test_a_trailing_newline_never_satisfies_an_anchored_pattern`, `test_ecma_dollar_means_end_of_input_wherever_it_appears`, `test_a_session_token_with_a_trailing_newline_is_refused` |
| A game result that carries injected text or another principal | The adapter refuses to relay it and returns `internal_error` | `test_refuses_to_relay_a_result_that_claims_another_principal_or_breaks_the_contract` |
| Entity ids that do not exist, are not in this room, or are out of sight | `target_not_found`, byte-identical for unknown, other-room, removed, other-namespace and unseen ids; nothing leaks through any query; another room id is `room_mismatch` | `test_unknown_foreign_and_removed_ids_are_indistinguishable`, `test_inspect_of_a_missing_entity_leaks_nothing`, `test_no_query_result_mentions_anything_out_of_sight`, `test_no_command_can_name_anything_out_of_sight`, `test_room_describe_counts_only_what_is_in_sight`, `test_refuses_a_command_for_another_room` |
| Perception memory leaking hidden state | Memory is filled only from the companion's own avatar's line of sight, never through the player's avatar, another companion or the true room. A change out of sight shows only as `may_be_stale`, one bit, identical for moved, removed, locked, picked up, renamed or a part changed; a thing removed out of sight stays remembered as it was until its place is seen; never-seen things never appear | `test_every_kind_of_change_out_of_sight_looks_the_same`, `test_nothing_never_seen_appears_through_memory`, `test_memory_never_includes_what_only_the_players_avatar_saw`, `test_another_companions_sight_never_fills_this_companions_memory`, `test_a_thing_removed_out_of_sight_stays_remembered_until_its_place_is_seen`, `test_a_fetch_at_a_remembered_thing_is_judged_on_the_memory_alone` |
| Acting on remembered things | Only `go_to`, `look_at`, `point_at`, `come` and `fetch` may aim at a remembered entity, judged on the memory alone, re-checked on arrival with the same failure for moved-out-of-sight and gone. Every command that changes an entity, and `expected_entities`, need it in sight now: `target_not_found` with the unknown-id message, nothing changed, nothing held | `test_nothing_that_changes_a_remembered_thing_is_allowed_without_seeing_it`, `test_following_staying_or_wandering_at_something_out_of_sight_is_refused`, `test_arriving_where_it_is_gone_fails_the_same_way_whether_moved_or_removed`, `test_arriving_to_see_it_elsewhere_says_it_moved`, `test_another_principals_job_looks_like_no_job` |
| Memory staleness and bounds | `may_be_stale` after 60 s or any change, sticky until seen again; at most 256 entries, least recently seen forgotten first; cleared when a link session starts or ends and when the room changes; never in a snapshot, receipt or save; can be turned off | `test_an_old_memory_is_marked_may_be_stale`, `test_a_change_out_of_sight_marks_it_may_be_stale_without_saying_what`, `test_the_flag_stays_up_until_the_companion_sees_it_again`, `test_memory_holds_at_most_the_policy_number_of_entities_nearest_kept`, `test_the_least_recently_seen_is_forgotten_first`, `test_a_link_session_clears_the_memory_when_it_ends`, `test_memory_is_cleared_when_the_room_changes`, `test_memory_is_never_saved`, `test_memory_can_be_turned_off` |
| Stale revisions | `revision_conflict`; unrelated changes do not conflict under `expected_entities`; destructive ops must cover every target; stops never fail on revisions | `test_refuses_a_stale_expected_revision`, `test_refuses_stale_expected_entities`, `test_unrelated_changes_do_not_conflict_with_expected_entities`, `test_refuses_a_destructive_command_whose_expectations_skip_a_target`, `test_stop_ops_never_fail_on_revisions`, `test_a_stop_applies_whatever_expectations_come_with_it` |
| Replay and conflicting replay | Canonical JSON v1 fingerprints (Lane P's golden fixture reproduced byte for byte); same content replays with `replayed: true`; different content is `action_id_conflict`; failures and previews record nothing; `"preview": false` is the same command | `test_reproduces_the_golden_fixture_byte_for_byte`, `test_identical_replay_returns_the_original_receipt_without_reapplying`, `test_refuses_a_conflicting_replay`, `test_a_failed_command_records_no_receipt_so_a_corrected_retry_is_not_a_conflict`, `test_a_preview_cannot_reuse_a_committed_action_id_for_other_content`, `test_an_explicit_preview_false_is_the_same_command` |
| `protect.unlock` and locks | Not listed; calling it is an unknown tool; the host answers `permission_denied` to a companion, for its own lock and as a preview too; locks resist grab, place, remove, transform, fetch and effects; a held thing cannot be locked; relocking keeps the first owner | `test_protect_unlock_is_not_listed`, `test_refuses_calling_protect_unlock_as_an_unknown_tool`, `test_refuses_protect_unlock_from_the_companion`, `test_refuses_protect_unlock_even_of_the_companions_own_lock`, `test_refuses_protect_unlock_as_a_preview_too`, `test_locks_resist_every_changing_command`, `test_refuses_locking_an_entity_someone_is_holding`, `test_relocking_keeps_the_original_lock_and_its_owner` |
| A companion's `room.undo` (review finding 1) | It may not change anything protected or any protection, in either direction (its own lock included): `target_protected`. It may step back only over revisions its own commands made; undoing the player's changes is the player's: `permission_denied` | `test_companion_undo_cannot_unlock_the_players_lock`, `test_companion_undo_cannot_bring_back_a_lock_the_player_removed`, `test_companion_undo_cannot_move_a_protected_entity_back`, `test_companion_undo_cannot_remove_even_its_own_lock`, `test_companion_undo_cannot_undo_the_players_changes`, `test_companion_undo_cannot_take_away_the_players_creation`, `test_companion_undo_steps_back_over_its_own_changes_only`, `test_the_players_own_undo_is_unrestricted` |
| The player's stop (review finding 2) | The player's `goal.stop` with no actor stops every avatar the player may direct, their goals and their effects; naming the companion stops the companion's; `effect.stop` from the player stops companion effects by id or all; a companion cannot stop the player's effects | `test_the_players_stop_stops_the_companions_goals_and_effects`, `test_the_players_stop_naming_the_companion_stops_its_goals_and_effects`, `test_the_players_effect_stop_stops_companion_effects_all_or_by_id`, `test_the_companion_cannot_stop_the_players_effects`, `test_the_players_stop_of_their_own_avatar_leaves_the_companion_alone` |
| Stops and capacity (review finding 3) | Stops never fail for capacity: they take no durable receipt, skip the rate limits at both layers, reuse any action id, and need no action id from the model; transient receipts are bounded per principal and never fill the durable ledger; a checkpoint compacts the ledger; the last 256 slots are the player's, so a companion cannot block the player's own lock | `test_stops_apply_with_the_ledger_full_and_every_rate_bucket_empty`, `test_a_full_ledger_refuses_durable_commands_but_never_stops_or_checkpoints`, `test_the_companion_cannot_spend_the_players_share_of_the_ledger`, `test_transient_receipts_never_fill_the_durable_ledger`, `test_a_stop_applies_again_when_its_action_id_is_reused`, `test_a_stop_needs_no_action_id_and_always_applies`, `test_stop_ops_are_never_rate_limited` |
| Messages over the size limits | Command or query over 65,536 bytes: `request_invalid`; creation source over 32,768: `budget_exceeded`; nesting deeper than canonical JSON allows; duplicate keys, non-finite numbers, integers outside int64, integer literals Python cannot read: refused, never raised | `test_refuses_a_command_over_65536_bytes`, `test_refuses_a_creation_source_over_32768_bytes`, `test_refuses_an_oversized_query`, `test_refuses_duplicate_keys_and_non_finite_numbers`, `test_refuses_integers_outside_int64`, `test_raw_requests_with_absurd_numbers_or_nesting_are_refused_not_raised`, `test_refuses_non_finite_and_oversized_numbers_before_spending_a_rate_token` |
| Frames over the link limits (review finding 4) | Frames are canonical JSON v1, so a frame is the canonical message plus an envelope of under 64 bytes, whatever characters or number spellings it carries; frames over the limit are refused before their body is read | `test_a_frame_is_the_canonical_message_plus_a_short_envelope`, `test_a_contract_sized_message_crosses_a_link_sized_to_the_contract`, `test_a_large_non_ascii_result_fits_its_frame`, `test_refuses_an_oversized_frame_without_reading_it`, `test_client_refuses_to_send_a_frame_over_the_limit` |
| Rate limits | Token buckets per principal at the host and in the adapter: `rate_limited`, retryable; invalid messages count; stops are never limited | `test_refuses_a_command_flood`, `test_refuses_a_query_flood`, `test_refuses_floods_of_invalid_messages_too`, `test_rate_limits_are_per_principal`, `test_rate_limits_commands_before_they_reach_the_game` |
| Budgets with nothing held | At most 4 effects per principal, effects expire, at most 32 creations per room; seed and draft presets cannot be pinned | `test_effect_and_creation_budgets_hold`, `test_effects_expire_after_their_duration`, `test_refuses_a_fifth_running_effect_until_one_ends`, `test_style_set_still_refuses_an_unpinnable_preset` |
| Anything outside the game: files, shell, URLs, credentials | No such tools or fields; no resources or prompts; the process lockdown (above) | `test_refuses_file_shell_url_and_credential_tools`, `test_no_tool_or_field_reaches_files_shell_urls_saves_or_credentials`, `test_offers_no_resources_prompts_completions_or_logging`, `test_lockdown.py` |
| The link itself | Wrong token refused; the companion refuses a listener that cannot prove the session and sends it nothing; the token never appears on the wire or in errors; second companion `busy`; failed proofs never lock the real companion out; at most 8 pending handshakes; malformed proofs and hellos answered, never left hanging; loopback literals only; session files that point elsewhere, are malformed or are over 4 KiB refused | `test_refuses_a_client_that_does_not_know_the_token`, `test_client_refuses_a_listener_that_cannot_prove_the_session`, `test_the_token_never_crosses_the_socket`, `test_link_errors_never_contain_the_token`, `test_refuses_a_second_companion_while_one_is_connected`, `test_failed_proofs_never_lock_out_the_real_companion`, `test_caps_unauthenticated_connections`, `test_a_proof_that_is_not_hex_is_refused_not_left_hanging`, `test_the_two_loopback_literals_are_the_only_hosts`, `test_refuses_a_session_file_pointing_off_this_computer`, `test_refuses_an_oversized_session_file`, `test_refuses_a_malformed_pid_or_creation_time` |
| Vendor and harness neutrality | Tool names match `^[a-z][a-z0-9_]{0,63}$`; no vendor name in the surface, the package or the play-only profile; no experimental capabilities; both MCP protocol eras; a minimal schema profile for strict function-calling validators | `test_tool_names_are_portable_across_model_vendors`, `test_nothing_in_the_surface_names_a_model_vendor`, `test_offers_nothing_but_the_game_and_names_no_vendor`, `test_handshake_era_client_lists_tools_observes_and_sets_a_goal`, `test_2026_era_client_lists_tools_observes_and_sets_a_goal`, `test_minimal_profile_uses_only_widely_supported_keywords_and_is_smaller` |

### Mutation check

Each protection above was also broken on purpose, one at a time, in a scratch copy of the package,
tests, contracts and room, and the whole suite run against it (6 October 2026). 84 mutations: every
review-finding fix reverted (undo protection and authorship, lock checks, the player's stop, stop
paths, the ledger and its reserve, canonical frames, each invisible-character rule, each lockdown rule,
the server's own lockdown call), the minor items (ECMA `$`, int64, non-finite numbers, unreadable
numbers, every link check), and the rest of the boundary (perception at every surface, actors,
player-only ops, authority keys, result relaying, revisions, approvals, replay, rate and size limits,
budgets, the play-only profile). The first pass caught 82; the two survivors, a write to an allowed
file and a policy radius below the contract's maximum, now have tests
(`test_even_the_session_file_is_read_only`, `test_the_policy_radius_caps_whatever_radius_observe_asks_for`),
and all 84 are caught.

The founder's answers round (6 October 2026, evening) checked its five most important protections the
same way, each against the module that guards it, and all five were caught: the player's sight filling
the companion's memory (2 failures in `test_perception_memory.py`); a removal out of sight revealed at
once by dropping the memory (3); commands, destructive ones included, naming remembered things (30); the
memory's size bound removed (2); and runs of variation selectors let through by dropping "one per base"
(9 in `test_text_rules.py`). Staleness itself, the session and room clearing, and the arrival re-check
are covered by tests but were not mutation-checked this round.

## Policy numbers (founder decisions; current values are recommendations)

| Setting | Recommended default | Where |
|---|---|---|
| Companion commands held for the player | `entity.remove`, `entity.transform`, `creation.revise`, `room.undo`, `style.set`. The founder's direction is none (tiers instead); every protection is tested with none | `HostPolicy.companion_approval_ops`, `NO_HOLDS` |
| Approval lifetime | 5 minutes; lapses when a touched entity changes | `approval_ttl_s` |
| Pending approvals per companion | 3 | `max_pending_approvals` |
| Command rate | 2 per second sustained, bursts of 10 (stops exempt) | host `HostPolicy`, adapter `Adapter` |
| Query rate | 10 per second sustained, bursts of 30 | same |
| What the companion perceives | Line of sight from its avatar (eye 0.087 m, the 10 cm body), for every query and command; `observe` within 20 m. The four choices are in [PERCEPTION.md](PERCEPTION.md) | `observe_max_radius_m` and the perception flags |
| Perception memory (founder: yes) | 256 entities per companion; `may_be_stale` after 60 s or any change; goals that only move or turn the companion may aim at remembered things | `perception_memory_entries`, `perception_memory_stale_after_s`, `REMEMBERED_TARGET_GOALS` |
| A companion's undo (founder: confirmed 6 October 2026) | Its own changes only, never anything protected | `_op_room_undo` |
| Receipt ledger | 4,096 durable receipts per room; the last 256 only for the player | `max_durable_receipts`, `player_receipt_reserve` |
| Effects | At most 4 running per principal; wind up to 5 m/s, glow intensity up to 1 | `max_effects_per_principal`, `CAPABILITIES` |
| Carry limits | Companion 2 kg, player 0.5 kg. Unchanged when the companion shrank to 10 cm; an open question | `carry_limit_kg` |
