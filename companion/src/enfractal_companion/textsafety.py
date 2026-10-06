"""Untrusted world text: the character rules from `common.schema.json`, applied before text leaves the host.

Names, categories, labels and sign text come from captures and players. The contract forbids
control, line-separator, zero-width and bidirectional-override characters in single-line
`display_text`, and allows only newline and tab in multi-line `long_text`. The host replaces
anything forbidden with a space and truncates, so an emitted result is always contract-valid and
a name can never fake a new line, hide characters or reorder what a reader sees.
"""
from __future__ import annotations

import re

# Mirrors common.schema.json $defs/display_text and $defs/long_text.
_DISPLAY_FORBIDDEN = re.compile("[\u0000-\u001f\u007f-\u009f​-‏  ‪-‮⁠-⁩﻿]")
_LONG_FORBIDDEN = re.compile("[\u0000-\u0008\u000b-\u001f\u007f-\u009f​-‏  ‪-‮⁠-⁩﻿]")
# Lone surrogates cannot be encoded as UTF-8 and would break the transport.
_SURROGATE = re.compile("[\ud800-\udfff]")


def display_text(value: object, max_length: int) -> str:
    text = value if isinstance(value, str) else ""
    text = _SURROGATE.sub(" ", _DISPLAY_FORBIDDEN.sub(" ", text))
    text = " ".join(text.split(" ")).strip()
    return text[:max_length] or "unnamed"


def long_text(value: object, max_length: int) -> str:
    text = value if isinstance(value, str) else ""
    text = _SURROGATE.sub(" ", _LONG_FORBIDDEN.sub(" ", text))
    return text[:max_length]


_SAFE_PATH_PART = re.compile(r"^[A-Za-z0-9_.:-]{1,64}$")


def field_path(parts) -> str:
    """A JSON path for error reports built only from safe segments: requester-chosen keys never echo."""
    path = "$"
    for part in parts:
        if isinstance(part, int):
            path += f"[{part}]"
        elif isinstance(part, str) and _SAFE_PATH_PART.match(part):
            path += f".{part}"
        else:
            path += ".?"
    return path[:200]
