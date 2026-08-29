#!/usr/bin/env python3
from pathlib import Path

path = Path("tools/ci/probe_projectile_tile_cut.py")
text = path.read_text(encoding="utf-8")

def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one marker, got {count}: {old[:160]!r}")
    text = text.replace(old, new, 1)

replace_once(
    '    bone_arrow_defaults = compact(extract_type_if_block(set_defaults, 474))\n    seed_defaults = around_optional(set_defaults, "type == 51", radius=1400)\n',
    '    bone_arrow_defaults = compact(extract_type_if_block(set_defaults, 474))\n'
    '    sound_gun_defaults = compact(extract_type_if_block(set_defaults, 1099))\n'
    '    seed_defaults = around_optional(set_defaults, "type == 51", radius=1400)\n')

replace_once(
    '        "BoneArrowFromMerchant": 474,\n        "BoneShard": 1124,\n',
    '        "BoneArrowFromMerchant": 474,\n        "SoundGun": 1099,\n        "BoneShard": 1124,\n')

marker = '''    for token in ("NewProjectile(", "NewItem(", "KillTile(", "RequestNewItem("):\n        if token in bone_arrow_kill:\n            raise SystemExit(f"type 474 Kill gained authoritative side effect: {token}")\n\n'''
addition = marker + '''    required_sound_gun_defaults = (\n        "width = 66;",\n        "height = 66;",\n        "aiStyle = 1;",\n        "friendly = true;",\n        "penetrate = -1;",\n        "timeLeft = 600;",\n        "tileCollide = false;",\n        "magic = true;",\n    )\n    for token in required_sound_gun_defaults:\n        if token not in sound_gun_defaults:\n            raise SystemExit(f"type 1099 default missing: {token}")\n    for forbidden in ("ignoreWater = true;", "extraUpdates ="):\n        if forbidden in sound_gun_defaults:\n            raise SystemExit(f"type 1099 unexpected default: {forbidden}")\n\n    for source_name, source_text in (\n        ("AI_001", arrow_ai),\n        ("AI", projectile_ai),\n        ("Update", projectile_update),\n        ("HandleMovement", handle_movement),\n        ("GetCollisionParams", collision_params),\n        ("Kill", projectile_kill),\n        ("CanCutTiles", can_cut_tiles),\n    ):\n        if count_type_comparisons(source_text, 1099) != 0:\n            raise SystemExit(f"type 1099 unexpectedly special in {source_name}")\n\n'''
replace_once(marker, addition)

replace_once(
    '    print("projectile_bone_arrow_from_merchant_kill=" + bone_arrow_kill)\n    print("projectile_seed_defaults=" + seed_defaults)\n',
    '    print("projectile_bone_arrow_from_merchant_kill=" + bone_arrow_kill)\n'
    '    print("projectile_sound_gun_defaults=" + sound_gun_defaults)\n'
    '    print("projectile_seed_defaults=" + seed_defaults)\n')

path.write_text(text, encoding="utf-8")
print("permanent SoundGun 1099 source proof integrated")
