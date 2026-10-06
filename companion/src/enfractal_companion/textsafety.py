"""Untrusted world text: what may reach a reader, applied before text leaves the host.

Names, categories, labels and sign text come from captures and players, so they may try to hide
things. The contract forbids control, line-separator, zero-width and bidirectional-override
characters in single-line `display_text`, and allows only newline and tab in multi-line
`long_text`. That list misses other characters that render as nothing: every Unicode format
character (category Cf, which includes the TAG block U+E0000-U+E007F that can spell a hidden
message, the Arabic letter mark U+061C, the soft hyphen U+00AD and the interlinear annotation
marks U+FFF9-U+FFFB), the Hangul fillers and the variation selectors. The host replaces all of
these with a space and truncates, so an emitted string can never fake a new line, carry invisible
text or reorder what a reader sees, whichever view of the result (escaped text or structured
JSON) a client hands its model.

Code points are written as numbers on purpose: the source file stays plain ASCII and no invisible
character can hide in it.
"""
from __future__ import annotations

import re
import unicodedata

# Letters and symbols outside the format category that still render as nothing: Hangul fillers
# (U+115F, U+1160, U+3164, U+FFA0), the braille blank (U+2800) and the combining grapheme joiner (U+034F).
_BLANK_CODE_POINTS = frozenset({0x115F, 0x1160, 0x3164, 0xFFA0, 0x2800, 0x034F})
# Variation selectors, including the supplement used to smuggle bytes after an emoji, and the
# Mongolian free variation selectors.
_SELECTOR_RANGES = ((0xFE00, 0xFE0F), (0xE0100, 0xE01EF), (0x180B, 0x180F))
# Controls, format characters, surrogates, line and paragraph separators.
_HIDDEN_CATEGORIES = frozenset({"Cc", "Cf", "Cs", "Zl", "Zp"})
_NEWLINE_AND_TAB = frozenset({10, 9})


def is_hidden(ch: str) -> bool:
    """True for a character a reader cannot see that could still change meaning or layout."""
    code = ord(ch)
    if code in _BLANK_CODE_POINTS or any(low <= code <= high for low, high in _SELECTOR_RANGES):
        return True
    return unicodedata.category(ch) in _HIDDEN_CATEGORIES


def hidden_characters(text: str, allow_newlines: bool = False) -> list[str]:
    """The hidden characters in `text`, as U+XXXX labels. Newline and tab are allowed in long text."""
    return [f"U+{ord(ch):04X}" for ch in text
            if is_hidden(ch) and not (allow_newlines and ord(ch) in _NEWLINE_AND_TAB)]


def _clean(text: str, keep_newlines: bool) -> str:
    return "".join(" " if is_hidden(ch) and not (keep_newlines and ord(ch) in _NEWLINE_AND_TAB) else ch
                   for ch in text)


def display_text(value: object, max_length: int) -> str:
    """Single-line text: every hidden character becomes a space, runs of spaces collapse."""
    text = value if isinstance(value, str) else ""
    text = re.sub(" {2,}", " ", _clean(text, keep_newlines=False)).strip()
    return text[:max_length] or "unnamed"


def long_text(value: object, max_length: int) -> str:
    """Multi-line text: newline and tab survive, every other hidden character becomes a space."""
    text = value if isinstance(value, str) else ""
    return _clean(text, keep_newlines=True)[:max_length]


_SAFE_PATH_PART = re.compile(r"[A-Za-z0-9_.:-]{1,64}")


def field_path(parts) -> str:
    """A JSON path for error reports. A segment that is not a short plain token is shown as '?', so a
    field path never carries arbitrary text the requester chose (plain tokens such as 'principal' do echo)."""
    path = "$"
    for part in parts:
        if isinstance(part, int):
            path += f"[{part}]"
        elif isinstance(part, str) and _SAFE_PATH_PART.fullmatch(part):
            path += f".{part}"
        else:
            path += ".?"
    return path[:200]
