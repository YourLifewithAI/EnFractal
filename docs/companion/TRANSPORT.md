# The companion link

How the companion's MCP server reaches the running game. Implemented in
`companion/src/enfractal_companion/link.py` (both ends, Python); the frame shapes are in
`companion/schemas/companion-link.schema.json`, proposed for `contracts/`. Run 2 implements the game end
in C# in front of P1's `CommandHost`; it must pass the cases in `companion/tests/test_link.py` and
`test_link_schema.py`.

## Goals

- Only processes running as the player on this computer can reach the command surface.
- Whoever connects gets exactly one principal, `companion:local`, chosen by the game. Nothing a
  connection sends can name another principal or carry an approval.
- The secret never travels, so a process squatting the port or reading the traffic learns nothing
  reusable, and a stale session file cannot make the companion talk to someone else's listener.
- A misbehaving peer costs a closed connection, never a crash or a half-applied command.

## Session file

At startup the game listens on `127.0.0.1` (or `::1`) on an ephemeral port and writes
`user://companion/session.json` (on Windows `%APPDATA%\Godot\app_userdata\EnFractal\companion\session.json`):

```json
{
  "schema": "enfractal.companion_session",
  "version": 1,
  "host": "127.0.0.1",
  "port": 53817,
  "token": "<64 lowercase hex digits: 256 bits from a CSPRNG, new every launch>",
  "room_id": "test_room",
  "pid": 12345,
  "created_utc": "2026-10-06T18:00:00Z"
}
```

- Written atomically (temporary file in the same folder, then rename), UTF-8, LF, no byte-order mark;
  on POSIX with mode `0600`. On Windows the user's `AppData\Roaming` ACL already limits it to the
  player's account. The game deletes it on exit.
- The companion re-reads the file on every reconnect, so a restarted game (new port, new token) is
  picked up without reconfiguring the MCP client.
- The companion refuses a file whose `host` is not the literal `127.0.0.1` or `::1` (no names, no
  `0.0.0.0`, no mapped addresses), a token that is not 64 lowercase hex digits, unknown fields,
  duplicate keys, or any other schema or version.

## Frames

Every frame is a 4-byte big-endian unsigned length followed by that many bytes of UTF-8 JSON holding
one object. Duplicate keys, `NaN`, `Infinity` and overflowing numbers are refused. Limits: 1,024 bytes
during the handshake; 131,072 bytes for a request (a contract message is at most 65,536 bytes of
canonical JSON, and the host still checks that); 266,240 bytes for a response (a result is at most
262,144). A frame over its limit is refused before its body is read.

## Handshake

```
companion                                             game
   | hello {protocol, version: 1, client_nonce}  ->     |
   |   <- challenge {protocol, version, server_nonce,   |
   |                 server_proof}                      |
   | (verify server_proof; abort if wrong)              |
   | auth {client_proof}                          ->     |
   |   <- ready {session_id, principal, avatar,         |   or refused {code} and close
   |             room_id, limits}                       |
```

- Nonces are 256 bits from a CSPRNG, lowercase hex.
- `server_proof = hex(HMAC-SHA256(key = bytes.fromhex(token), msg = "enfractal.companion_link/1|server|" + client_nonce + "|" + server_nonce))`
- `client_proof = hex(HMAC-SHA256(key = bytes.fromhex(token), msg = "enfractal.companion_link/1|client|" + client_nonce + "|" + server_nonce))`
- Both sides compare proofs in constant time. The companion checks the game's proof before it sends
  its own, so a listener that does not hold the token never receives anything it could replay.
- The whole handshake must finish within 5 seconds (`handshake_timeout`).
- Five failed proofs within 60 seconds lock the listener for 60 seconds (`locked`), which makes
  guessing pointless even before the 256-bit token makes it impossible.
- One authenticated companion at a time; a second gets `busy`. The first one's connection closing
  (for example when the MCP client restarts the server) frees the slot.
- `ready.principal` and `ready.avatar` are the game's assignment, reported for the adapter's own
  bookkeeping. The game never reads a principal from anything the connection sends.

Refusal codes: `hello_invalid`, `handshake_invalid`, `handshake_timeout`, `auth_failed`, `locked`,
`busy`, `frame_invalid`.

## Requests

```
companion: {"type": "request", "seq": 1, "message": <enfractal.command or enfractal.query>}
game:      {"type": "response", "seq": 1, "message": <enfractal.result>}
```

- `seq` is a positive integer the companion increments; the response echoes it. One request is in
  flight at a time.
- `type`, `seq` and `message` are the only keys. Any other frame type (there is no approval frame, no
  principal frame, no token frame) or any extra key closes the connection with `frame_invalid`.
- The game answers every request with an `enfractal.result`, including malformed messages
  (`request_invalid`, `op: "invalid"`), so the companion never has to guess.
- If the connection drops or a response does not arrive within 10 seconds, the companion closes the
  connection and reports an unknown outcome; the model is told to call `receipt_lookup` before
  retrying, which the contract's idempotency rules make safe.

## Contract proposal

Promote `companion/schemas/companion-link.schema.json` to `contracts/companion-link.schema.json` (its `$id`
already uses the contracts base). The ready-to-apply patch, together with the other Run 1 contract
proposals from this lane, is `docs/companion/proposals/contracts-run1.diff`. Apply it from the stored
blob so Windows line-ending conversion cannot touch it:

```sh
git show run1/companion:docs/companion/proposals/contracts-run1.diff | git apply
```

It adds the schema, lists it in `contracts/README.md`, and raises the schema count in
`contracts/tests/test_contracts.py` from 6 to 7. `contracts/validate.py` needs no change: it registers
every `*.schema.json`. The patch was checked by applying it to a copy of `contracts/`, regenerating the
examples and running the contract tests (34 pass) and the companion suite against that copy
(`ENFRACTAL_CONTRACTS_DIR=<copy>`; 141 pass).
