"""Plain text that came from outside (file names, EXIF lens text, notes) made safe for Markdown.

File names and EXIF strings are data, never markup: a photo called ``[click](http://x)`` or a lens
string with ``<script>`` in it must show up as that text and nothing more. The HTML report
escapes with ``html.escape``; Markdown has no single escape function, so this is it.
"""

from __future__ import annotations

import re

_CONTROL = re.compile(r"[\x00-\x1f\x7f]+")
# Every character that can start emphasis, a link, an image, an autolink, raw HTML, an entity,
# a table cell or a heading. Backslash-escaping any ASCII punctuation is valid CommonMark.
_MARKDOWN_SPECIAL = re.compile(r"([\\`*_\[\]<>|~&#!])")


def md_text(value: object) -> str:
    """``value`` as one line of Markdown text with every special character escaped."""
    text = _CONTROL.sub(" ", str(value)).strip()
    return _MARKDOWN_SPECIAL.sub(r"\\\1", text)
