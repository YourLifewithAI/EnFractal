"""Wishes and the Gubble's words for the mock host: the kernel host's CommandHostWishes.cs, WishQueue.cs and
CommandHostSpeech.cs, in memory (the kernel saves the wish journal per room; the mock saves nothing).

- The player makes a wish (wish.submit); wishes wait their turn, first in first out, at most MAX_QUEUED; wish.stop cancels
  the active and waiting wishes, denies what they left held for a click and stops the Gubble.
- The connected AI reads them (wish.list, wish.wait), takes the next (wish.take) and reports it (wish.report: done,
  unable, unsupported). While a wish is active, the host ties every command the companion sends to it: its own record,
  never an action id the AI chose. "Granted" comes only from the host's receipts and from goal jobs it saw finish.
- Every companion result carries wishes_waiting while wishes wait (a count, never their words).
- companion.say: a curated phrase by key, or, only in the grown-ups' test mode the player turns on, the AI's own words;
  rate limited; journalled as the AI's note.

Gaps the box model cannot close: a walk with no target (go_to a place, come with no target) has no job here, so it counts
as done once accepted; the kernel keeps it pending until the body arrives.
"""
from __future__ import annotations

import base64
import secrets
from dataclasses import dataclass, field

from . import gubble_phrases
from .mock_journal import MAX_NOTES, entry_time, new_entry_id
from .refusals import HostError

PLAYER = "player:local"
COMPANION = "companion:local"
GUBBLE = "avatar:companion"
MAX_QUEUED, MAX_FINISHED, MAX_COMMANDS = 16, 64, 32  # kernel: WishQueue.MaxQueued, MaxHistory, MaxCommands
BOOKKEEPING_OPS = frozenset({"wish.take", "wish.report", "companion.say", "journal.note"})
WISH_STOP_OPS = frozenset({"goal.stop", "effect.stop", "wish.stop"})


@dataclass
class Wish:
    id: str
    action_id: str
    text: str
    aim_id: str | None
    status: str = "queued"
    result_code: str | None = None
    successes: list[str] = field(default_factory=list)
    refusals: list[tuple[str, str]] = field(default_factory=list)
    pending: list[str] = field(default_factory=list)

    @property
    def command_count(self) -> int:
        return len(self.successes) + len(self.refusals) + len(self.pending)

    def recorded(self, action_id: str) -> bool:
        return action_id in self.successes or action_id in self.pending or any(a == action_id for a, _ in self.refusals)

    @property
    def terminal(self) -> bool:
        return self.status not in ("queued", "running")


class WishMixin:
    """Mixed into MockHost: the wishes live with the room; the grown-ups' setting and the AI's sessions with the host."""

    def _reset_wishes(self) -> None:
        self.wishes: list[Wish] = []
        self._wish_of_held: dict[str, str] = {}
        self.gubble_said: tuple[str, bool, int] | None = None
        self._speech_tokens = float(gubble_phrases.SPEECH_BURST)
        self._speech_at: float | None = None
        # The room's own object names (the manifest), the only names a curated phrase may say.
        self._manifest_names = {e.id: e.display_name for e in self.entities.values() if e.kind == "object"}

    # ------------------------------------------------------------------ the host's state, for tests and the console

    @property
    def active_wish(self) -> Wish | None:
        return next((w for w in self.wishes if w.status == "running"), None)

    @property
    def wishes_waiting(self) -> int:
        return sum(1 for w in self.wishes if w.status == "queued")

    @property
    def companion_connected(self) -> bool:
        return self._companion_sessions > 0

    def player_set_companion_own_words(self, on: bool) -> None:
        """The player's own setting in the game (the HUD's grown-ups' test mode). No request reaches this."""
        with self._lock:
            self.companion_own_words = bool(on)

    def _with_wish_notice(self, principal: str, result: dict) -> dict:
        if principal == COMPANION and self.wishes_waiting:
            result = dict(result, wishes_waiting=self.wishes_waiting)
        return result

    # ------------------------------------------------------------------ the queue

    def _trim_wishes(self) -> None:
        finished = [w for w in self.wishes if w.terminal]
        drop = {w.id for w in finished[:max(0, len(finished) - MAX_FINISHED)]}
        self.wishes = [w for w in self.wishes if w.id not in drop]

    def _complete(self, wish: Wish, code: str) -> None:
        self._unfinished(wish)
        if code == "cancelled":
            wish.status = "cancelled"
        elif code == "completed" and wish.successes and not wish.refusals:
            wish.status = "granted"
        elif wish.successes:
            wish.status = "partial"
        elif code == "unsupported":
            wish.status = "unsupported"
        else:
            wish.status = "failed"
        wish.result_code = code
        self._trim_wishes()

    @staticmethod
    def _unfinished(wish: Wish) -> None:
        wish.refusals += [(action_id, "cancelled") for action_id in wish.pending]
        wish.pending = []

    @staticmethod
    def _companion_wish(wish: Wish) -> dict:
        row = {"wish_id": wish.id, "text": wish.text, "status": wish.status, "action_count": wish.command_count,
               "pending_count": len(wish.pending), "refusal_codes": [code for _, code in wish.refusals]}
        if wish.aim_id is not None:
            row["aim_id"] = wish.aim_id
        return row

    @staticmethod
    def _wish_summary(wish: Wish) -> dict:
        row = {"wish_id": wish.id, "text": wish.text, "status": wish.status, "action_count": wish.command_count,
               "refusal_codes": [code for _, code in wish.refusals]}
        if wish.aim_id is not None:
            row["aim_id"] = wish.aim_id
        if wish.result_code is not None:
            row["result_code"] = wish.result_code
        return row

    # ------------------------------------------------------------------ the player's side

    def _op_wish_submit(self, principal, message, apply, new_revision):
        if principal != PLAYER:
            raise HostError("permission_denied", "Only the player can make a wish.")
        args = message["args"]
        if not args["text"].strip():
            raise HostError("request_invalid", "A wish is one to 280 characters.", field_path="$.args.text")
        aim = args.get("aim_id")
        if aim is not None and not self._known_to_team(aim):
            raise HostError("target_not_found", "Neither of you knows that target.", field_path="$.args.aim_id")
        if self.wishes_waiting >= MAX_QUEUED:
            raise HostError("budget_exceeded", "The wish queue is full. Let a wish finish or stop the queue first.")
        if not apply:
            return {}
        wish = Wish("wish:" + base64.b32encode(secrets.token_bytes(20)).decode("ascii").lower()[:26], message["action_id"], args["text"], aim)
        self.wishes.append(wish)
        return {"data": {"wish_id": wish.id, "status": "queued"}}

    def _op_wish_stop(self, principal, message, apply, new_revision):
        if principal != PLAYER:
            raise HostError("permission_denied", "Only the player can stop the wish queue.")
        if not apply:
            return {}
        ending = [w for w in self.wishes if not w.terminal]
        for wish in ending:
            self._unfinished(wish)
            wish.status, wish.result_code = "cancelled", "cancelled"
        self._trim_wishes()
        # Revocation is immediate, including a command waiting for a click; then the Gubble stops (the player's goal.stop).
        self._deny_wish_held({w.id for w in ending}.__contains__)
        self._op_goal_stop(PLAYER, {"op": "goal.stop", "args": {"actor": GUBBLE}}, True, None)
        return {"data": {"cancelled": len(ending)}}

    def _wishes_read(self, principal: str) -> dict:
        if principal != PLAYER:
            raise HostError("permission_denied", "The wish journal belongs to the player.")
        return {"items": [self._wish_summary(w) for w in self.wishes]}

    # ------------------------------------------------------------------ the connected AI's side

    def _wish_query(self, op: str) -> dict:
        active = self.active_wish
        if op == "wish.list":
            data = {"waiting": [self._companion_wish(w) for w in self.wishes if w.status == "queued"]}
            if active is not None:
                data["active"] = self._companion_wish(active)
            return data
        data = {"waiting": self.wishes_waiting}
        if active is not None:
            data["active_wish_id"] = active.id
        return data

    def _op_wish_take(self, principal, message, apply, new_revision):
        if principal != COMPANION:
            raise HostError("permission_denied", "Wishes are taken by the player's AI; the player makes them in the wish box.",
                            field_path="$.op")
        if not apply:
            return {}
        wish = self.active_wish
        if wish is None:
            wish = next((w for w in self.wishes if w.status == "queued"), None)
            if wish is not None:
                wish.status = "running"
        data = {"waiting": self.wishes_waiting}
        if wish is not None:
            data["wish"] = self._companion_wish(wish)
        return {"data": data}

    def _op_wish_report(self, principal, message, apply, new_revision):
        if principal != COMPANION:
            raise HostError("permission_denied", "Only the player's AI reports the wish it took.", field_path="$.op")
        args = message["args"]
        code = {"done": "completed", "unsupported": "unsupported"}.get(args["outcome"], "failed")
        wish = self.active_wish
        if wish is None or wish.id != args["wish_id"]:
            raise HostError("target_not_found", "That is not the wish you are working on.", field_path="$.args.wish_id")
        if code == "completed" and wish.pending:
            raise HostError("not_ready", "This wish's actions are still running: follow them with jobs_status, then report again.",
                            retryable=True)
        if not apply:
            return {}
        self._deny_wish_held(lambda wish_id: wish_id == wish.id)
        if self.active_wish is wish:
            self._stop_wish_goal(wish)
        if self.active_wish is wish:
            self._complete(wish, code)
        return {"data": {"wish_id": wish.id, "status": wish.status}}

    # ------------------------------------------------------------------ the host's own record of a wish's actions

    def _check_wish_budget(self, principal: str, message: dict) -> None:
        """Before a companion command runs while a wish is active (the kernel's CheckWishCommandBudget)."""
        wish = self.active_wish
        if principal != COMPANION or wish is None or message["op"] in WISH_STOP_OPS or message["op"] in BOOKKEEPING_OPS \
                or message.get("preview") is True:
            return
        if wish.recorded(message["action_id"]):
            raise HostError("action_id_conflict", "That action already has a receipt for this wish; look it up.")
        if wish.command_count >= MAX_COMMANDS:
            raise HostError("budget_exceeded", "This wish has reached its action limit. Report it with wish_report.")

    def _held_for_wish(self, principal: str, action_id: str) -> None:
        wish = self.active_wish
        if principal == COMPANION and wish is not None:
            self._wish_of_held[action_id] = wish.id

    def _observe_wish_command(self, principal: str, message: dict, result: dict, *, held: bool = False) -> None:
        """A companion command's result, recorded against the wish that was active when it was sent (the kernel's
        ObserveWishCommand). A held command's final result counts only for the wish it was held under, if still active."""
        wish = self.active_wish
        if principal != COMPANION or wish is None or not isinstance(message, dict) or message.get("preview") is True \
                or result.get("preview") or result.get("replayed"):
            return
        op = message.get("op")
        if op in WISH_STOP_OPS or op in BOOKKEEPING_OPS:
            return
        action_id = message.get("action_id", "")
        code = "completed" if result["ok"] else result["error"]["code"]
        if code == "approval_required":
            return
        if held and self._wish_of_held.pop(action_id, None) != wish.id:
            return
        if wish.recorded(action_id):
            return
        job = self.jobs.get(result.get("job_id", ""))
        pending = result["ok"] and op == "goal.set" and job is not None and job["state"] == "running" and job["goal"] != "follow"
        if wish.command_count >= MAX_COMMANDS:
            self._stop_wish_goal(wish)
            self._complete(wish, "failed")
        elif pending:
            wish.pending.append(action_id)
        elif result["ok"]:
            wish.successes.append(action_id)
        else:
            wish.refusals.append((action_id, code))

    def _wish_goal_ended(self, job: dict, state: str, code: str) -> None:
        """A goal job ended: a wish's pending goal becomes its success or refusal (the kernel's WishGoalEnded)."""
        wish = self.active_wish
        if job["principal"] != COMPANION or wish is None or job["action_id"] not in wish.pending:
            return
        wish.pending.remove(job["action_id"])
        if state == "succeeded":
            wish.successes.append(job["action_id"])
        else:
            wish.refusals.append((job["action_id"], code))

    def _stop_wish_goal(self, wish: Wish) -> None:
        goal = self.goals.get(GUBBLE)
        job = self.jobs.get(goal.get("job_id", "")) if goal else None
        if job is not None and job["state"] == "running" and job["action_id"] in wish.pending:
            self._cancel_job(GUBBLE)
            self.goals.pop(GUBBLE, None)

    def _deny_wish_held(self, for_wish) -> None:
        for request_id, approval in list(self.approvals.items()):
            if approval.state == "pending" and approval.principal == COMPANION and for_wish(self._wish_of_held.get(approval.action_id, "")):
                self.player_decide(request_id, False)
        for action_id in [a for a, w in self._wish_of_held.items() if for_wish(w)]:
            del self._wish_of_held[action_id]

    def _wish_session(self, principal: str, event: str) -> None:
        if principal != COMPANION:
            return
        self._companion_sessions = max(0, self._companion_sessions + {"start": 1, "end": -1}.get(event, 0))
        wish = self.active_wish
        if self._companion_sessions == 0 and wish is not None:
            # The AI left in the middle of a wish: it ends interrupted, with what it held for a click denied.
            self._deny_wish_held(lambda wish_id: wish_id == wish.id)
            self._stop_wish_goal(wish)
            if self.active_wish is wish:
                self._complete(wish, "interrupted")

    # ------------------------------------------------------------------ companion.say

    def _op_companion_say(self, principal, message, apply, new_revision):
        if principal != COMPANION:
            raise HostError("permission_denied", "Only the player's AI speaks as the Gubble.", field_path="$.op")
        args = message["args"]
        if "phrase" in args:
            key, about = args["phrase"], args.get("about")
            if gubble_phrases.needs_name(key) != (about is not None):
                raise HostError("invalid_args", "This phrase names a thing: give about." if about is None
                                else "This phrase names nothing: leave about out.", field_path="$.args.about")
            if about is not None and not self._known_to_team(about):
                raise HostError("target_not_found", "That is not in this room.", field_path="$.args.about")
            words = gubble_phrases.PHRASES[key]
            if about is not None:
                name = self._manifest_names.get(about)
                spoken = gubble_phrases.speakable(self.text.display_text(name, 32)) if name is not None else gubble_phrases.UNNAMED_THING
                words = words.replace(gubble_phrases.NAME_SLOT, spoken)
            own = False
        else:
            if not self.companion_own_words:
                raise HostError("permission_denied", "Choose one of the Gubble's phrases by key. The AI's own words are shown only in "
                                "the grown-ups' test mode, which only the player can turn on.", field_path="$.args.text")
            problem = gubble_phrases.own_words_problem(args["text"])
            if problem is not None:
                raise HostError("invalid_args", problem, field_path="$.args.text")
            words, own = args["text"].strip(), True
        if not self._speech_token(take=apply):
            raise HostError("rate_limited", "The Gubble is still speaking; wait a moment.", retryable=True)
        if not apply:
            return {}
        entry = {"entry_id": new_entry_id(), "kind": "note", "at_utc": entry_time(self.clock.now()),
                 "revision": self.revision, "author": principal, "text": words, "untrusted": True}
        self.notes.append(entry)
        del self.notes[:max(0, len(self.notes) - MAX_NOTES)]
        self.gubble_said = (words, own, (self.gubble_said[2] if self.gubble_said else 0) + 1)
        return {"data": {"text": words, "entry_id": entry["entry_id"], "own_words": own}}

    def _speech_token(self, *, take: bool) -> bool:
        now = self.clock.now()
        if self._speech_at is not None:
            self._speech_tokens = min(float(gubble_phrases.SPEECH_BURST),
                                      self._speech_tokens + max(0.0, now - self._speech_at) / gubble_phrases.SPEECH_REFILL_S)
        self._speech_at = now
        if self._speech_tokens < 1:
            return False
        if take:
            self._speech_tokens -= 1
        return True
