#!/usr/bin/env bash
# Install the pinned Linux toolchain for cloud sessions: Godot .NET 4.7.2, .NET SDK 8.0.425 and the
# contract validator's Python environment. Checksum-verified against tools/native-toolchain.lock.json,
# installed under .cache/linux/ (ignored by Git), idempotent. Needs curl, unzip, tar, sha512sum, python3.
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOCK="$REPO/tools/native-toolchain.lock.json"
DEST="$REPO/.cache/linux"
mkdir -p "$DEST/downloads"

field() { python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]][sys.argv[3]])' "$LOCK" "$1" "$2"; }

install() { # lock-key, extractor
  local key="$1" kind="$2" url sha marker archive
  url="$(field "$key" url)"; sha="$(field "$key" sha512)"; marker="$REPO/$(field "$key" marker)"
  if [ -e "$marker" ]; then echo "ok  $key already installed"; return; fi
  archive="$DEST/downloads/$(basename "$url")"
  if [ ! -f "$archive" ] || ! echo "$sha  $archive" | sha512sum -c --status -; then
    echo "get $key"; curl -fsSL --retry 3 --max-time 1200 -o "$archive" "$url"
  fi
  echo "$sha  $archive" | sha512sum -c --status - || { echo "checksum mismatch for $key" >&2; rm -f "$archive"; exit 1; }
  local target="$REPO/$(field "$key" destination)"; mkdir -p "$target"
  if [ "$kind" = zip ]; then unzip -q -o "$archive" -d "$target"; else tar -xzf "$archive" -C "$target"; fi
  [ -e "$marker" ] || { echo "install of $key did not produce $marker" >&2; exit 1; }
  echo "ok  $key installed"
}

install engine_linux zip
install sdk_linux tar
if [ ! -x "$DEST/contracts-venv/bin/python" ]; then python3 -m venv "$DEST/contracts-venv"; fi
"$DEST/contracts-venv/bin/pip" install -q -r "$REPO/contracts/requirements.txt"
echo "ok  contract validator environment"
"$REPO/$(field engine_linux marker)" --version
"$DEST/dotnet/dotnet" --list-sdks
