"""Deterministic JSON and text as bytes: UTF-8, LF line endings, sorted keys, no NaN."""

from __future__ import annotations

import hashlib
import json
from typing import Any


def json_bytes(obj: Any) -> bytes:
    text = json.dumps(obj, indent=2, sort_keys=True, ensure_ascii=False, allow_nan=False)
    return (text + "\n").encode("utf-8")


def canonical_bytes(obj: Any) -> bytes:
    """Compact canonical form used for content identifiers."""
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode("utf-8")


def text_bytes(text: str) -> bytes:
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    if not text.endswith("\n"):
        text += "\n"
    return text.encode("utf-8")


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()
