"""Reject incomplete DEM inputs before they become playable terrain."""

from __future__ import annotations

import numpy as np
import rasterio

from .geo import MapConfig


def read_complete_dem(dataset: rasterio.io.DatasetReader, config: MapConfig) -> np.ndarray:
    """Read one aligned DEM band, rejecting every nodata or non-finite cell.

    A partially valid source needs an explicit, separately attributed repair
    recipe. The regional builder must never turn its missing cells into ground.
    """
    side = config.grid_side - 1
    if dataset.count != 1 or dataset.width != side or dataset.height != side:
        raise ValueError("DEM band count or dimensions differ from region configuration")
    if dataset.crs is None or dataset.crs.to_epsg() != config.crs_epsg:
        raise ValueError("DEM CRS differs from region configuration")
    if max(abs(actual - expected) for actual, expected in zip(dataset.bounds, config.bounds_xy)) > 0.01:
        raise ValueError("DEM bounds differ from requested region")
    masked = dataset.read(1, masked=True)
    values = np.asarray(masked.data, dtype=np.float32)
    bad = np.ma.getmaskarray(masked) | ~np.isfinite(values)
    invalid_count = int(np.count_nonzero(bad))
    if invalid_count:
        raise ValueError(f"DEM contains {invalid_count} missing or invalid source cells")
    minimum, maximum = float(values.min()), float(values.max())
    if not -500 <= minimum <= maximum <= 9000:
        raise ValueError(f"Implausible DEM elevation range: {minimum}, {maximum}")
    return values
