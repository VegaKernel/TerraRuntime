#!/usr/bin/env python3
from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one marker, got {count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


probe = "tools/ci/probe_projectile_tile_cut.py"

replace_once(
    probe,
    "def matching_lines(source: str, needle: str, limit: int = 300) -> str:\n",
    '''def extract_factory_initializer(source: str, field_name: str) -> str:\n    match = re.search(\n        rf"{re.escape(field_name)}\\s*=\\s*Factory\\.CreateCustomSet<bool\\?>\\s*\\(",\n        source)\n    if match is None:\n        raise SystemExit(f"factory initializer not found: {field_name}")\n\n    opening = source.find("(", match.start())\n    depth = 0\n    for index in range(opening, len(source)):\n        char = source[index]\n        if char == "(":\n            depth += 1\n        elif char == ")":\n            depth -= 1\n            if depth == 0:\n                return source[match.start() : index + 1]\n\n    raise SystemExit(f"unterminated factory initializer: {field_name}")\n\n\ndef extract_type_if_block(source: str, raw_type: int) -> str:\n    match = re.search(\n        rf"(?:(?:else\\s+)?if)\\s*\\(\\s*type\\s*==\\s*{raw_type}(?!\\d)\\s*\\)",\n        source)\n    if match is None:\n        raise SystemExit(f"type if block not found: {raw_type}")\n\n    opening = source.find("{", match.end())\n    if opening < 0:\n        raise SystemExit(f"type if block body not found: {raw_type}")\n\n    depth = 0\n    in_string = False\n    in_char = False\n    escaped = False\n    for index in range(opening, len(source)):\n        char = source[index]\n        if escaped:\n            escaped = False\n            continue\n        if char == "\\\\" and (in_string or in_char):\n            escaped = True\n            continue\n        if char == '"' and not in_char:\n            in_string = not in_string\n            continue\n        if char == "'" and not in_string:\n            in_char = not in_char\n            continue\n        if in_string or in_char:\n            continue\n        if char == "{":\n            depth += 1\n        elif char == "}":\n            depth -= 1\n            if depth == 0:\n                return source[match.start() : index + 1]\n\n    raise SystemExit(f"unterminated type if block: {raw_type}")\n\n\ndef count_type_comparisons(source: str, raw_type: int) -> int:\n    normalized = compact(source)\n    pattern = re.compile(\n        rf"(?<!\\d)type\\s*(?:==|!=)\\s*{raw_type}(?!\\d)|\\bcase\\s+{raw_type}\\s*:")\n    return len(pattern.findall(normalized))\n\n\ndef matching_lines(source: str, needle: str, limit: int = 300) -> str:\n''')

replace_once(
    probe,
    '    bone_defaults = around_optional(set_defaults, "type == 21", radius=1800)\n'
    '    seed_defaults = around_optional(set_defaults, "type == 51", radius=1400)\n',
    '    bone_defaults = around_optional(set_defaults, "type == 21", radius=1800)\n'
    '    bone_arrow_defaults = compact(extract_type_if_block(set_defaults, 474))\n'
    '    seed_defaults = around_optional(set_defaults, "type == 51", radius=1400)\n')

replace_once(
    probe,
    '''    wind_immunity = matching_lines(projectile_id_source, "WindPhysicsImmunity", limit=5)\n    if "public const short Seed = 51;" not in projectile_id_source:\n        raise SystemExit("ProjectileID.Seed != 51 in pinned source")\n    if "public const short BoneShard = 1124;" not in projectile_id_source:\n        raise SystemExit("ProjectileID.BoneShard != 1124 in pinned source")\n    for raw_type in (51, 1124):\n        if re.search(rf"\\(short\\){raw_type}(?!\\d)", wind_immunity):\n            raise SystemExit(f"type {raw_type} unexpectedly overrides WindPhysicsImmunity")\n''',
    '''    wind_immunity = compact(extract_factory_initializer(projectile_id_source, "WindPhysicsImmunity"))\n    if "CreateCustomSet<bool?>(null" not in wind_immunity:\n        raise SystemExit("unexpected WindPhysicsImmunity default semantics")\n\n    expected_ids = {\n        "Seed": 51,\n        "BoneArrowFromMerchant": 474,\n        "BoneShard": 1124,\n    }\n    for name, raw_type in expected_ids.items():\n        declaration = re.compile(\n            rf"public const (?:short|int)\\s+{re.escape(name)}\\s*=\\s*{raw_type}\\s*;")\n        if declaration.search(projectile_id_source) is None:\n            raise SystemExit(f"ProjectileID.{name} != {raw_type} in pinned source")\n        if re.search(rf"(?<!\\d){raw_type}(?!\\d)", wind_immunity):\n            raise SystemExit(f"type {raw_type} unexpectedly overrides WindPhysicsImmunity")\n\n    required_bone_arrow_defaults = (\n        "arrow = true;",\n        "width = 10;",\n        "height = 10;",\n        "aiStyle = 1;",\n        "friendly = true;",\n        "ranged = true;",\n        "timeLeft = 1200;",\n        "penetrate = 2;",\n    )\n    for token in required_bone_arrow_defaults:\n        if token not in bone_arrow_defaults:\n            raise SystemExit(f"type 474 default missing: {token}")\n    for forbidden in ("tileCollide = false;", "ignoreWater = true;", "extraUpdates ="):\n        if forbidden in bone_arrow_defaults:\n            raise SystemExit(f"type 474 unexpected default: {forbidden}")\n\n    for source_name, source_text in (\n        ("AI_001", arrow_ai),\n        ("AI", projectile_ai),\n        ("Update", projectile_update),\n        ("HandleMovement", handle_movement),\n        ("GetCollisionParams", collision_params),\n        ("CanCutTiles", can_cut_tiles),\n    ):\n        if count_type_comparisons(source_text, 474) != 0:\n            raise SystemExit(f"type 474 unexpectedly special in {source_name}")\n\n    if count_type_comparisons(projectile_kill, 474) != 1:\n        raise SystemExit("type 474 Kill branch count changed")\n    bone_arrow_kill = compact(extract_type_if_block(projectile_kill, 474))\n    for token in ("SoundEngine.PlaySound", "Dust.NewDust"):\n        if token not in bone_arrow_kill:\n            raise SystemExit(f"type 474 visual Kill token missing: {token}")\n    for token in ("NewProjectile(", "NewItem(", "KillTile(", "RequestNewItem("):\n        if token in bone_arrow_kill:\n            raise SystemExit(f"type 474 Kill gained authoritative side effect: {token}")\n''')

replace_once(
    probe,
    '    print("projectile_bone_defaults=" + bone_defaults)\n'
    '    print("projectile_seed_defaults=" + seed_defaults)\n',
    '    print("projectile_bone_defaults=" + bone_defaults)\n'
    '    print("projectile_bone_arrow_from_merchant_defaults=" + bone_arrow_defaults)\n'
    '    print("projectile_bone_arrow_from_merchant_kill=" + bone_arrow_kill)\n'
    '    print("projectile_seed_defaults=" + seed_defaults)\n')

replace_once(
    probe,
    '    print("projectile_seed_bone_shard_wind_immunity=" + wind_immunity)\n',
    '    print("projectile_simple_ai1_wind_immunity=" + wind_immunity)\n')
