"""Game-owned Glow trial progress; never accepted in a companion message or returned as a recipe list.

The mock keeps this profile in memory across room loads. Native play owns its durable profile. Only
Lantern has an implemented discovery condition today; the other IDs are reserved for later behaviours
and allow trusted game-side fixtures to exercise the fourth and fifth light limits.
"""
from __future__ import annotations

from collections.abc import Iterable


class GlowProgress:
    MAX_SUCCESSFUL_USES = 1_000_000
    HINT_AFTER_USES = 10
    _DISCOVERIES = frozenset({"lantern", "torch", "bioluminesce", "constellation", "skylight"})
    _HINTS = frozenset({"lantern_self"})

    def __init__(self, successful_uses: int = 0, discoveries: Iterable[str] = (), delivered_hints: Iterable[str] = ()):
        if type(successful_uses) is not int or not 0 <= successful_uses <= self.MAX_SUCCESSFUL_USES:
            raise ValueError("Glow use count must be a bounded nonnegative integer.")
        known = self._validated_ids(discoveries, self._DISCOVERIES)
        hints = self._validated_ids(delivered_hints, self._HINTS)
        if successful_uses < len(known) or (hints and successful_uses < self.HINT_AFTER_USES):
            raise ValueError("Glow progress has inconsistent use records.")
        self._successful_uses = successful_uses
        self._discoveries = known
        self._delivered_hints = hints

    @staticmethod
    def _validated_ids(values: Iterable[str], allowed: frozenset[str]) -> frozenset[str]:
        sequence = tuple(values)
        if any(type(value) is not str or value not in allowed for value in sequence) or len(set(sequence)) != len(sequence):
            raise ValueError("Glow progress has invalid or duplicate IDs.")
        return frozenset(sequence)

    @property
    def successful_uses(self) -> int:
        return self._successful_uses

    @property
    def discoveries(self) -> frozenset[str]:
        return self._discoveries

    @property
    def delivered_hints(self) -> frozenset[str]:
        return self._delivered_hints

    @property
    def earned_light_limit(self) -> int:
        return 5 if len(self._discoveries) >= 5 else 4 if len(self._discoveries) >= 2 else 3

    def record_success(self, self_target: bool) -> None:
        """Called by the host only after a new Glow effect starts successfully."""
        self._successful_uses = min(self._successful_uses + 1, self.MAX_SUCCESSFUL_USES)
        if self_target:
            self._discoveries = self._discoveries | {"lantern"}
        if self._successful_uses >= self.HINT_AFTER_USES and "lantern" not in self._discoveries:
            self._delivered_hints = self._delivered_hints | {"lantern_self"}
