#!/usr/bin/env bash
# Headless Linux mirror of run-engine-tests.ps1 and tools/test-room.ps1, plus the contract and companion tests.
# Run tools/linux/setup-toolchain.sh first. Exits non-zero if anything fails; stderr output from
# Godot counts as a failure, exactly as in the Windows runners.
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LINUX="$REPO/.cache/linux"
GODOT="$LINUX/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64"
[ -x "$GODOT" ] && [ -x "$LINUX/dotnet/dotnet" ] && [ -x "$LINUX/companion-venv/bin/python" ] || { echo "Run tools/linux/setup-toolchain.sh first." >&2; exit 2; }
export DOTNET_ROOT="$LINUX/dotnet" PATH="$LINUX/dotnet:$PATH" DOTNET_CLI_HOME="$LINUX/dotnet-home" NUGET_PACKAGES="$LINUX/nuget" \
       DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# Keep test saves away from any real player data.
export XDG_DATA_HOME="$LINUX/test-data" XDG_CONFIG_HOME="$LINUX/test-config"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$XDG_DATA_HOME" "$XDG_CONFIG_HOME"
P="$REPO/game"; LOG="$LINUX/test-logs"; mkdir -p "$LOG"; failed=0
# Containers and WSL often run as root, and Godot then warns on stderr, which every check here treats as a failure.
export GODOT_SILENCE_ROOT_WARNING=1

echo "== C# build"
if ! dotnet build "$P/EnFractal.csproj" -nologo -v:q -warnaserror > "$LOG/build.log" 2>&1; then cat "$LOG/build.log"; failed=1; else echo "PASS build (warnings are errors)"; fi
echo "== import"
timeout 300 "$GODOT" --headless --editor --path "$P" --import > "$LOG/import.out" 2> "$LOG/import.err" || true
if grep -qE "ERROR|SCRIPT ERROR" "$LOG/import.err"; then grep -E "ERROR" "$LOG/import.err" | head -20; failed=1; else echo "PASS import"; fi

run() { # name, expected stdout regex, godot args...
  local name="$1" expect="$2"; shift 2
  timeout 180 "$GODOT" "$@" > "$LOG/$name.out" 2> "$LOG/$name.err"; local code=$?
  if [ $code -ne 0 ] || grep -q '[^[:space:]]' "$LOG/$name.err" || ! grep -qE "$expect" "$LOG/$name.out"; then
    echo "FAIL $name (exit $code)"; tail -15 "$LOG/$name.out"; echo "--- stderr"; head -30 "$LOG/$name.err"; failed=1
  else echo "PASS $name: $(grep -E "$expect" "$LOG/$name.out" | tail -1 | cut -c1-150)"; fi
}

echo "== kernel suites (list from run-engine-tests.ps1)"
for t in $(grep -oE "'[a-z_0-9]+\.gd'" "$REPO/run-engine-tests.ps1" | tr -d "'"); do
  run "${t%.gd}" "." --headless --path "$P" --script "res://tests/$t"
done
echo "== native fixtures and room boot"
run native_contract_probe "Native release probe passed" --headless --path "$P" res://scenes/native_contract_probe.tscn
run native_small_avatar "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_small_avatar.tscn
run native_room_navigation "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_room_navigation.tscn
run native_room_data "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_room_data.tscn
run native_look_preset "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_look_preset.tscn
run room_boot "ROOM_WORLD_READY" --headless --path "$P" --fixed-fps 60 --quit-after 240 res://scenes/room.tscn
echo "== C# kernel"
run native_kernel_canonical_json "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_kernel_canonical_json.tscn
HOST_DUMP="$LOG/command_host_messages"; rm -rf "$HOST_DUMP"
run native_kernel_command_host "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_kernel_command_host.tscn -- --dump="$HOST_DUMP"
if (cd "$REPO" && "$LINUX/contracts-venv/bin/python" -I contracts/validate.py "$HOST_DUMP"/*.json > "$LOG/host_messages.log" 2>&1); then
  echo "PASS command host messages validate: $(tail -1 "$LOG/host_messages.log")"
else cat "$LOG/host_messages.log"; failed=1; fi
SANDBOX_DUMP="$LOG/sandbox_messages"; rm -rf "$SANDBOX_DUMP"
run native_kernel_sandbox "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_kernel_sandbox.tscn -- --dump="$SANDBOX_DUMP"
if (cd "$REPO" && "$LINUX/contracts-venv/bin/python" -I contracts/validate.py "$SANDBOX_DUMP"/*.json > "$LOG/sandbox_messages.log" 2>&1); then
  echo "PASS sandbox messages validate: $(tail -1 "$LOG/sandbox_messages.log")"
else cat "$LOG/sandbox_messages.log"; failed=1; fi
JOURNAL_DUMP="$LOG/journal_messages"; rm -rf "$JOURNAL_DUMP"
run native_kernel_journal "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_kernel_journal.tscn -- --dump="$JOURNAL_DUMP"
if (cd "$REPO" && "$LINUX/contracts-venv/bin/python" -I contracts/validate.py "$JOURNAL_DUMP"/*.json > "$LOG/journal_messages.log" 2>&1); then
  echo "PASS journal messages validate: $(tail -1 "$LOG/journal_messages.log")"
else cat "$LOG/journal_messages.log"; failed=1; fi
REBUILD_DUMP="$LOG/rebuild_states"; rm -rf "$REBUILD_DUMP"
run native_kernel_rebuild "checks passed" --headless --path "$P" --fixed-fps 60 res://tests/native_kernel_rebuild.tscn -- --dump="$REBUILD_DUMP"
if (cd "$REPO" && for s in room_state_before room_state_after; do "$LINUX/contracts-venv/bin/python" -I contracts/validate.py --state "$REBUILD_DUMP/$s.json" --room game/rooms/test_room || exit 1; done > "$LOG/rebuild_states.log" 2>&1); then
  echo "PASS rebuilt room states validate against the test room: $(tail -1 "$LOG/rebuild_states.log")"
else cat "$LOG/rebuild_states.log"; failed=1; fi
echo "== kernel canonical JSON (Python reference)"
if (cd "$REPO" && "$LINUX/contracts-venv/bin/python" tools/kernel/canonical_json.py --check game/tests/fixtures/kernel > "$LOG/canonical.log" 2>&1 \
    && "$LINUX/contracts-venv/bin/python" -m unittest discover -s tools/kernel >> "$LOG/canonical.log" 2>&1); then
  echo "PASS $(head -1 "$LOG/canonical.log" | cut -c1-150)"
else cat "$LOG/canonical.log"; failed=1; fi
echo "== contracts"
if (cd "$REPO" && "$LINUX/contracts-venv/bin/python" -m unittest discover -s contracts/tests > "$LOG/contracts.log" 2>&1); then
  echo "PASS contracts: $(grep -E '^Ran' "$LOG/contracts.log")"
else cat "$LOG/contracts.log"; failed=1; fi
if (cd "$REPO" && "$LINUX/contracts-venv/bin/python" -I contracts/validate.py game/rooms/test_room game/styles/*/v*.json > "$LOG/validate.log" 2>&1); then
  echo "PASS shipped rooms and presets validate"
else cat "$LOG/validate.log"; failed=1; fi
echo "== companion"
if (cd "$REPO" && PYTHONPATH="$REPO/companion/src" "$LINUX/companion-venv/bin/python" -m unittest discover -s companion/tests > "$LOG/companion.log" 2>&1); then
  echo "PASS companion: $(grep -E '^Ran' "$LOG/companion.log")"
else cat "$LOG/companion.log"; failed=1; fi
echo "== roomscan (capture pipeline, CPU only)"
if (cd "$REPO/pipeline/roomscan" && uv run --locked pytest -q > "$LOG/roomscan.log" 2>&1); then
  echo "PASS roomscan: $(tail -n 1 "$LOG/roomscan.log")"
else cat "$LOG/roomscan.log"; failed=1; fi
echo "== landscape corpus (synthetic rooms, read through roomscan)"
if (cd "$REPO/pipeline/roomscan" && uv run --locked python -B -m unittest discover -s "$REPO/pipeline/landscape/corpus/tests" -t "$REPO" > "$LOG/corpus.log" 2>&1); then
  echo "PASS landscape corpus: $(grep -E '^Ran' "$LOG/corpus.log")"
else cat "$LOG/corpus.log"; failed=1; fi

echo "== $([ $failed -eq 0 ] && echo GREEN || echo RED)"
exit $failed
