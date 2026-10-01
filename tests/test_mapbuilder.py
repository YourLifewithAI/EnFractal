from pathlib import Path

import numpy as np

from mapbuilder.build import sample_height, vertex_grid_from_dem
from mapbuilder.geo import MapConfig


CONFIG = MapConfig.load(Path("maps/barton_creek/config.json"))


def test_center_projection_round_trip() -> None:
    x, z = CONFIG.to_local(CONFIG.center_lon, CONFIG.center_lat)
    assert abs(x) < 0.001 and abs(z) < 0.001
    lon, lat = CONFIG.local_to_geographic(1000, -500)
    x, z = CONFIG.to_local(lon, lat)
    assert abs(x - 1000) < 0.001 and abs(z + 500) < 0.001


def test_pixel_center_to_vertex_grid_is_continuous() -> None:
    source = np.array([[10, 20], [30, 40]], dtype=np.float32)
    grid = vertex_grid_from_dem(source)
    assert grid.shape == (3, 3)
    assert grid[1, 1] == 25
    assert grid[0, 0] == 10
    assert grid[-1, -1] == 40
    assert np.array_equal(grid[:, 1], np.array([15, 25, 35], dtype=np.float32))


def test_local_grid_sampling_has_east_south_orientation() -> None:
    config = MapConfig(
        map_id="test", display_name="test", center_lat=30, center_lon=-97, crs_epsg=32614,
        side_m=4, sample_spacing_m=2, tile_side_m=2,
        vegetation_seed=1, vegetation_target=0, description="test",
    )
    grid = np.array([[0, 10, 20], [30, 40, 50], [60, 70, 80]], dtype=np.float32)
    assert sample_height(grid, config, -2, -2) == 0
    assert sample_height(grid, config, 2, -2) == 20
    assert sample_height(grid, config, -2, 2) == 60
    assert sample_height(grid, config, 0, 0) == 40
