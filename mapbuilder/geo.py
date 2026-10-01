"""Coordinate and package geometry shared by fetching and building."""

from __future__ import annotations

import json
from dataclasses import dataclass
from functools import cached_property
from pathlib import Path

from pyproj import Transformer


@dataclass(frozen=True)
class MapConfig:
    map_id: str
    display_name: str
    center_lat: float
    center_lon: float
    crs_epsg: int
    side_m: int
    sample_spacing_m: int
    tile_side_m: int
    vegetation_seed: int
    vegetation_target: int
    description: str

    @classmethod
    def load(cls, path: Path) -> "MapConfig":
        config = cls(**json.loads(path.read_text(encoding="utf-8")))
        if config.side_m <= 0 or config.sample_spacing_m <= 0 or config.tile_side_m <= 0:
            raise ValueError("Map dimensions must be positive")
        if config.side_m % config.sample_spacing_m or config.side_m % config.tile_side_m:
            raise ValueError("Map side must be divisible by sample and tile spacing")
        if config.tile_side_m % config.sample_spacing_m:
            raise ValueError("Tile side must be divisible by sample spacing")
        if not (-90 <= config.center_lat <= 90 and -180 <= config.center_lon <= 180):
            raise ValueError("Invalid center coordinate")
        return config

    @cached_property
    def to_projected(self) -> Transformer:
        return Transformer.from_crs(4326, self.crs_epsg, always_xy=True)

    @cached_property
    def to_geographic(self) -> Transformer:
        return Transformer.from_crs(self.crs_epsg, 4326, always_xy=True)

    @cached_property
    def center_xy(self) -> tuple[float, float]:
        return self.to_projected.transform(self.center_lon, self.center_lat)

    @property
    def bounds_xy(self) -> tuple[float, float, float, float]:
        east, north = self.center_xy
        half = self.side_m / 2
        return east - half, north - half, east + half, north + half

    @property
    def grid_side(self) -> int:
        return self.side_m // self.sample_spacing_m + 1

    def to_local(self, lon: float, lat: float) -> tuple[float, float]:
        east, north = self.to_projected.transform(lon, lat)
        center_east, center_north = self.center_xy
        return east - center_east, center_north - north

    def local_to_geographic(self, x: float, z: float) -> tuple[float, float]:
        center_east, center_north = self.center_xy
        return self.to_geographic.transform(center_east + x, center_north - z)

    def geographic_bbox_for_projected(self, bounds: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
        west, south, east, north = bounds
        corners = [self.to_geographic.transform(x, y) for x in (west, east) for y in (south, north)]
        return min(c[0] for c in corners), min(c[1] for c in corners), max(c[0] for c in corners), max(c[1] for c in corners)
