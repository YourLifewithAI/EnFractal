"""Stable geographic addresses and bounded local render-tile indices.

The geographic address names an area on Earth; local tiles are derivatives of
one regional map package. Neither address represents ownership or authority.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

from .geo import MapConfig


GEOGRAPHIC_TILE_SCHEME = "enfractal-geographic-quadtree-v1"
LOCAL_TILE_SCHEME = "enfractal-local-grid-v1"
MAX_GEOGRAPHIC_LEVEL = 24


@dataclass(frozen=True, order=True)
class GeographicTile:
    level: int
    column: int
    row: int

    @property
    def key(self) -> str:
        return f"geo-v1/{self.level}/{self.column}/{self.row}"


def geographic_tile(lon: float, lat: float, level: int) -> GeographicTile:
    """Address WGS84 lon/lat in a north-first, 2:1 geographic quadtree.

    +180 and -180 name the same meridian. A point on an internal boundary
    belongs to its east/south child. The south pole belongs to the final row.
    This is indexing only; it does not choose a polar local-frame orientation.
    """
    if isinstance(level, bool) or not isinstance(level, int) or not 0 <= level <= MAX_GEOGRAPHIC_LEVEL:
        raise ValueError("Unsupported geographic tile level")
    if not math.isfinite(lon) or not math.isfinite(lat) or not -180 <= lon <= 180 or not -90 <= lat <= 90:
        raise ValueError("Invalid geographic coordinate")
    count = 1 << level
    wrapped_lon = ((lon + 180.0) % 360.0) - 180.0
    column = min(count - 1, int(math.floor((wrapped_lon + 180.0) / 360.0 * count)))
    row = min(count - 1, int(math.floor((90.0 - lat) / 180.0 * count)))
    return GeographicTile(level, column, row)


def geographic_tile_bounds(tile: GeographicTile) -> tuple[float, float, float, float]:
    """Return west, south, east, north degrees for a valid geographic tile."""
    if not 0 <= tile.level <= MAX_GEOGRAPHIC_LEVEL:
        raise ValueError("Unsupported geographic tile level")
    count = 1 << tile.level
    if not 0 <= tile.column < count or not 0 <= tile.row < count:
        raise ValueError("Geographic tile outside level")
    west = -180.0 + 360.0 * tile.column / count
    east = -180.0 + 360.0 * (tile.column + 1) / count
    north = 90.0 - 180.0 * tile.row / count
    south = 90.0 - 180.0 * (tile.row + 1) / count
    return west, south, east, north


def local_tile(config: MapConfig, x: float, z: float) -> tuple[int, int] | None:
    """Address projected-grid +X easting/+Z southward tile; edge inclusive."""
    if not math.isfinite(x) or not math.isfinite(z):
        return None
    half = config.side_m / 2.0
    if not -half <= x <= half or not -half <= z <= half:
        return None
    count = config.side_m // config.tile_side_m
    return (
        min(count - 1, int(math.floor((x + half) / config.tile_side_m))),
        min(count - 1, int(math.floor((z + half) / config.tile_side_m))),
    )


def local_tile_bounds(config: MapConfig, column: int, row: int) -> tuple[float, float, float, float]:
    """Return west, north, east, south in local x/z metres."""
    count = config.side_m // config.tile_side_m
    if not 0 <= column < count or not 0 <= row < count:
        raise ValueError("Local tile outside region")
    half = config.side_m / 2.0
    west = -half + column * config.tile_side_m
    north = -half + row * config.tile_side_m
    return west, north, west + config.tile_side_m, north + config.tile_side_m
