"""The Gubble's words (companion.say): the curated phrases and the rules for the AI's own words.

The kernel host's CommandHostSpeech.cs is the authority; this is its mirror for the mock host and the tool descriptions,
and companion/tests/test_kernel_alignment.py fails if the two drift apart. The contract lists only the keys
($defs/gubble_phrase).

- By default the AI chooses a phrase by key: short, written for an eleven-year-old, the same words every time. A phrase's
  one slot ("{name}") takes only a room object's own name from the room manifest, and only a plain one; anything else,
  a creation's name included (an AI or a player wrote it), is "thing".
- In the grown-ups' test mode, which only the player turns on in the game, the AI's own words may be shown: one line of
  at most MAX_OWN_WORDS characters, trimmed, with no link and no markup (control and invisible characters are refused
  for every request string anyway).
"""
from __future__ import annotations

import re

MAX_OWN_WORDS = 140
SPEECH_BURST = 3
SPEECH_REFILL_S = 3.0
UNNAMED_THING = "thing"
NAME_SLOT = "{name}"

PHRASES: dict[str, str] = {
    "hello": "Hello! I'm here.",
    "on_my_way": "On my way!",
    "got_it": "Got it!",
    "all_done": "All done!",
    "thinking": "Hmm, let me think.",
    "lets_try": "Let me try!",
    "cannot": "I can't do that one.",
    "not_yet": "I can't do that yet.",
    "try_again": "Let's try again.",
    "oops": "Oops!",
    "wow": "Wow!",
    "look": "Look!",
    "follow_me": "Follow me!",
    "wait_for_me": "Wait for me!",
    "thank_you": "Thank you!",
    "good_idea": "Good idea!",
    "ready": "Ready when you are!",
    "your_turn": "Your turn!",
    "help_me": "Can you help me?",
    "too_far": "That's too far for me.",
    "found_it": "I found it!",
    "here_you_go": "Here you go!",
    "wish_heard": "I heard your wish!",
    "wish_working": "Working on your wish!",
    "good_night": "Good night!",
    "look_at": "Look at the {name}!",
    "found": "I found the {name}!",
    "bring": "I'll bring you the {name}.",
    "going_to": "Going to the {name}.",
    "cannot_reach": "I can't reach the {name}.",
    "like": "I like the {name}!",
}

# The kernel's LinkLike and MarkupCharacters, character for character.
LINK_LIKE = re.compile(r"(://|www\.|[a-z0-9-]+\.[a-z]{2,}\b|@)", re.IGNORECASE)
MARKUP_CHARACTERS = "<>[]{}`\\"
# The kernel's PlainName: a letter or digit, then letters, digits, spaces, apostrophes and hyphens, 32 at most.
PLAIN_NAME = re.compile(r"[^\W_](?:[^\W_]| |'|-){0,31}")


def needs_name(key: str) -> bool:
    return NAME_SLOT in PHRASES[key]


def speakable(name: str) -> str:
    """A room object's own name as a phrase may say it: plain, or "thing"."""
    name = name.strip()
    return name if PLAIN_NAME.fullmatch(name) else UNNAMED_THING


def own_words_problem(text: str) -> str | None:
    """Why the AI's own words cannot be shown, or None. They are shown trimmed."""
    words = text.strip()
    if not words:
        return "The Gubble's own words need something to say."
    if LINK_LIKE.search(words):
        return "Links and addresses are not shown in the game."
    if any(ch in MARKUP_CHARACTERS for ch in words):
        return "Plain words only: no markup."
    return None


def describe() -> str:
    """The phrases for a tool description: key, then words."""
    return "; ".join(f"{key}: \"{words}\"" for key, words in PHRASES.items())
