"""Sightings of the same kind of thing at the same place, in several photos, are one object.

The detector is wrong often and wrong differently in each photo; an object is something it keeps
finding in one place from different spots. Clusters need sightings in at least three photos (two
when the detector was sure), they are linked by the distance between their lifted centres relative
to their size, and two clusters that share most of their volume are one object called two names:
the better-evidenced name wins.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .lift import Sighting
from .vocabulary import plausible


@dataclass
class Cluster:
    kind: str
    sightings: list[Sighting]
    lo: np.ndarray = field(default_factory=lambda: np.zeros(3))
    hi: np.ndarray = field(default_factory=lambda: np.zeros(3))
    aliases: dict[str, float] = field(default_factory=dict)  # other kinds the same place was called, with their evidence

    @property
    def views(self) -> list[int]:
        return sorted({s.view for s in self.sightings})

    @property
    def mean_score(self) -> float:
        return float(np.mean([s.score for s in self.sightings]))

    @property
    def best_score(self) -> float:
        return max(s.score for s in self.sightings)

    @property
    def evidence(self) -> float:
        """Photos times mean score: how sure we are the cluster is a real object of this kind."""
        return len(self.views) * self.mean_score

    @property
    def centre(self) -> np.ndarray:
        return (self.lo + self.hi) / 2

    @property
    def size(self) -> np.ndarray:
        return self.hi - self.lo


def _link_radius(a: Sighting, b: Sighting, floor_m: float) -> float:
    return max(floor_m, 0.45 * max(a.extent_m, b.extent_m))


def group_by_kind(sightings: list[Sighting], *, floor_m: float = 0.07) -> list[list[Sighting]]:
    """Connected groups of same-kind sightings whose centres are close for their size."""
    groups: list[list[Sighting]] = []
    by_kind: dict[str, list[Sighting]] = {}
    for s in sightings:
        by_kind.setdefault(s.kind, []).append(s)
    for items in by_kind.values():
        parent = list(range(len(items)))

        def find(i: int) -> int:
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i

        centres = np.stack([s.centre for s in items])
        for i in range(len(items)):
            d = np.linalg.norm(centres[i + 1:] - centres[i], axis=1)
            for off in np.nonzero(d < 4.0)[0]:
                j = i + 1 + int(off)
                if d[off] < _link_radius(items[i], items[j], floor_m) and abs(centres[i][1] - centres[j][1]) < max(0.15, 0.6 * max(items[i].extent_m, items[j].extent_m)):
                    parent[find(j)] = find(i)
        sets: dict[int, list[Sighting]] = {}
        for i, s in enumerate(items):
            sets.setdefault(find(i), []).append(s)
        groups.extend(sets.values())
    return groups


def _volume(lo: np.ndarray, hi: np.ndarray) -> float:
    return float(np.prod(np.clip(hi - lo, 0.02, None)))


def overlap_of_smaller(a_lo, a_hi, b_lo, b_hi) -> float:
    inter = np.clip(np.minimum(a_hi, b_hi) - np.maximum(a_lo, b_lo), 0, None)
    return float(np.prod(inter)) / min(_volume(a_lo, a_hi), _volume(b_lo, b_hi))


def build_clusters(groups: list[list[Sighting]], *, min_views: int = 3, strong_score: float = 0.45,
                   max_overlap: float = 0.55) -> list[Cluster]:
    """Well-evidenced clusters with a plausible size for their kind, best evidence first, duplicates folded."""
    candidates: list[Cluster] = []
    for g in groups:
        views = {s.view for s in g}
        best = max(s.score for s in g)
        if not (len(views) >= min_views or (len(views) >= 2 and best >= strong_score)):
            continue
        lo = np.percentile(np.stack([s.lo for s in g]), 25, axis=0)
        hi = np.percentile(np.stack([s.hi for s in g]), 75, axis=0)
        longest = float(max(hi - lo))
        if not plausible(g[0].kind, longest):
            # A sighting's box is rough: judge the size by the cluster's pooled points instead.
            pts = np.concatenate([s.points for s in g])
            span = np.percentile(pts, 97, axis=0) - np.percentile(pts, 3, axis=0)
            if not plausible(g[0].kind, float(max(span))):
                continue
        candidates.append(Cluster(g[0].kind, g, lo, hi))
    candidates.sort(key=lambda c: (-c.evidence, c.kind))
    kept: list[Cluster] = []
    for c in candidates:
        twin = next((k for k in kept if overlap_of_smaller(c.lo, c.hi, k.lo, k.hi) > max_overlap), None)
        if twin is None:
            kept.append(c)
        else:
            twin.aliases[c.kind] = max(twin.aliases.get(c.kind, 0.0), c.evidence)
    return kept

