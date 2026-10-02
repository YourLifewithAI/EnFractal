"""Golden observations for the legacy Godot 4.7.2 spatial-pin expression.

Expected divergences document why stock Python JSON is not a wire contract.
"""

import hashlib

from tests.phase0_canonical_compare import compare


def test_legacy_godot_json_golden_observations() -> None:
    rows = compare()
    assert {case_id: (raw_same, normalized_same) for case_id, raw_same,
            normalized_same, *_ in rows} == {
        "barton_spatial": (False, True),
        "sorted_nested": (False, True),
        "numbers": (False, False),
        "strings": (False, True),
        "numeric_boundaries": (True, True),
    }
    assert rows[0][5].decode("utf-8").startswith('{"axes":')
    assert {case_id: hashlib.sha256(godot_bytes).hexdigest()
            for case_id, _, _, _, _, godot_bytes in rows} == {
        "barton_spatial": "3a8e4126ad1f7fae3f01b7cbd85c984a405a10cd69969df9d8d24886a6ba7ff8",
        "sorted_nested": "506144cda648794af680b71b78c88bf9d386f7bcafc63c267e01f5fe5edef8a2",
        "numbers": "23ff1ecfecce18ce84b9cba05c59b6044b0d18be5187d03ea2068a36a86598bf",
        "strings": "948a981e810bb22307f8a2faf80e16efec0ec9d3dd1a32cd41518bb06a202cc9",
        "numeric_boundaries": "0e9ad219b8063c60c4922c346cdaf620032411e2adbe73b681085fa7c3c84fc5",
    }
