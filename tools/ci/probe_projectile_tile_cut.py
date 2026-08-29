#!/usr/bin/env python3
"""Extract narrow, reviewable projectile/world contracts from ILSpy C# output.

The script deliberately does not persist or print complete decompiled Terraria types. It emits only the
small methods and call-site contexts needed to validate TerraRuntime's source-backed projectile behavior.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


CUTTABLE_TILE_TYPES = (
    3, 24, 28, 32, 51, 52, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 115, 184, 201,
    205, 231, 236, 254, 352, 382, 444, 454, 484, 485, 518, 519, 528, 529, 549, 636, 637, 638,
    654, 655, 711,
)


def extract_method(source: str, method_name: str) -> str:
    signature = re.compile(
        rf"(?m)^[ \t]*(?:public|private|protected|internal)\b[^\n;{{]*\b{re.escape(method_name)}\s*\([^\n)]*\)[^\n;{{]*$"
    )
    match = signature.search(source)
    if match is None:
        candidates = [" ".join(line.split()) for line in source.splitlines() if method_name in line][:20]
        detail = " | ".join(candidates) if candidates else "<none>"
        raise SystemExit(f"method not found: {method_name}; candidates: {detail}")

    opening = source.find("{", match.end())
    if opening < 0 or source[match.end() : opening].strip():
        raise SystemExit(f"method body not found after declaration: {method_name}")

    depth = 0
    in_string = False
    in_char = False
    escaped = False

    for index in range(opening, len(source)):
        char = source[index]
        if escaped:
            escaped = False
            continue

        if char == "\\" and (in_string or in_char):
            escaped = True
            continue

        if char == '"' and not in_char:
            in_string = not in_string
            continue

        if char == "'" and not in_string:
            in_char = not in_char
            continue

        if in_string or in_char:
            continue

        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[match.start() : index + 1]

    raise SystemExit(f"unterminated method: {method_name}")


def compact(text: str) -> str:
    return " ".join(text.split())


def around_first(source: str, needle: str, radius: int = 360) -> str:
    normalized = compact(source)
    index = normalized.find(needle)
    if index < 0:
        raise SystemExit(f"call-site token not found: {needle}")
    start = max(0, index - radius)
    end = min(len(normalized), index + len(needle) + radius)
    return normalized[start:end]


def around_optional(source: str, needle: str, radius: int = 520) -> str:
    normalized = compact(source)
    index = normalized.find(needle)
    if index < 0:
        return "<none>"
    start = max(0, index - radius)
    end = min(len(normalized), index + len(needle) + radius)
    return normalized[start:end]


def around_last(source: str, needle: str, radius: int = 700) -> str:
    normalized = compact(source)
    index = normalized.rfind(needle)
    if index < 0:
        return "<none>"
    start = max(0, index - radius)
    end = min(len(normalized), index + len(needle) + radius)
    return normalized[start:end]


def all_contexts(source: str, needle: str, radius: int = 2200, limit: int = 20) -> str:
    normalized = compact(source)
    contexts: list[str] = []
    start_at = 0
    while len(contexts) < limit:
        index = normalized.find(needle, start_at)
        if index < 0:
            break
        start = max(0, index - radius)
        end = min(len(normalized), index + len(needle) + radius)
        contexts.append(f"#{len(contexts) + 1}:{normalized[start:end]}")
        start_at = index + len(needle)
    return " | ".join(contexts) if contexts else "<none>"


def all_type_comparison_contexts(source: str, raw_type: int, radius: int = 2200, limit: int = 20) -> str:
    normalized = compact(source)
    pattern = re.compile(rf"(?<!\d)type\s*==\s*{raw_type}(?!\d)")
    contexts: list[str] = []
    for match in pattern.finditer(normalized):
        if len(contexts) >= limit:
            break
        start = max(0, match.start() - radius)
        end = min(len(normalized), match.end() + radius)
        contexts.append(f"#{len(contexts) + 1}:{normalized[start:end]}")
    return " | ".join(contexts) if contexts else "<none>"


def extract_factory_initializer(source: str, field_name: str) -> str:
    match = re.search(
        rf"{re.escape(field_name)}\s*=\s*Factory\.CreateCustomSet<bool\?>\s*\(",
        source)
    if match is None:
        raise SystemExit(f"factory initializer not found: {field_name}")

    opening = source.find("(", match.start())
    depth = 0
    for index in range(opening, len(source)):
        char = source[index]
        if char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return source[match.start() : index + 1]

    raise SystemExit(f"unterminated factory initializer: {field_name}")


def extract_type_if_block(source: str, raw_type: int) -> str:
    match = re.search(
        rf"(?:(?:else\s+)?if)\s*\(\s*type\s*==\s*{raw_type}(?!\d)\s*\)",
        source)
    if match is None:
        raise SystemExit(f"type if block not found: {raw_type}")

    opening = source.find("{", match.end())
    if opening < 0:
        raise SystemExit(f"type if block body not found: {raw_type}")

    depth = 0
    in_string = False
    in_char = False
    escaped = False
    for index in range(opening, len(source)):
        char = source[index]
        if escaped:
            escaped = False
            continue
        if char == "\\" and (in_string or in_char):
            escaped = True
            continue
        if char == '"' and not in_char:
            in_string = not in_string
            continue
        if char == "'" and not in_string:
            in_char = not in_char
            continue
        if in_string or in_char:
            continue
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[match.start() : index + 1]

    raise SystemExit(f"unterminated type if block: {raw_type}")


def count_type_comparisons(source: str, raw_type: int) -> int:
    normalized = compact(source)
    pattern = re.compile(
        rf"(?<!\d)type\s*(?:==|!=)\s*{raw_type}(?!\d)|\bcase\s+{raw_type}\s*:")
    return len(pattern.findall(normalized))


def matching_lines(source: str, needle: str, limit: int = 300) -> str:
    matches = [compact(line) for line in source.splitlines() if needle in line]
    if not matches:
        return "<none>"
    return " | ".join(matches[:limit])


def called_helpers(source: str, prefix: str) -> str:
    pattern = re.compile(rf"\b({re.escape(prefix)}[A-Za-z0-9_]+)\s*\(")
    calls = pattern.findall(source)
    return " -> ".join(calls) if calls else "<none>"


def relevant_drop_contexts(source: str) -> str:
    normalized = compact(source)
    contexts: list[str] = []
    for tile_type in CUTTABLE_TILE_TYPES:
        patterns = (
            f"case {tile_type}:",
            f"type == {tile_type}",
            f"type != {tile_type}",
            f"tile.type == {tile_type}",
            f"tile.type != {tile_type}",
        )
        for pattern in patterns:
            index = normalized.find(pattern)
            if index < 0:
                continue
            start = max(0, index - 180)
            end = min(len(normalized), index + len(pattern) + 260)
            contexts.append(f"{tile_type}:{normalized[start:end]}")
            break
    return " | ".join(contexts) if contexts else "<none>"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--projectile", required=True, type=Path)
    parser.add_argument("--delegate-methods", required=True, type=Path)
    parser.add_argument("--player", required=True, type=Path)
    parser.add_argument("--worldgen", required=True, type=Path)
    parser.add_argument("--main", required=True, type=Path)
    parser.add_argument("--tile-id", required=True, type=Path)
    parser.add_argument("--projectile-id", required=True, type=Path)
    args = parser.parse_args()

    projectile_source = args.projectile.read_text(encoding="utf-8")
    delegate_source = args.delegate_methods.read_text(encoding="utf-8")
    player_source = args.player.read_text(encoding="utf-8")
    worldgen_source = args.worldgen.read_text(encoding="utf-8")
    main_source = args.main.read_text(encoding="utf-8")
    tile_id_source = args.tile_id.read_text(encoding="utf-8")
    projectile_id_source = args.projectile_id.read_text(encoding="utf-8")

    can_cut_tiles = compact(extract_method(projectile_source, "CanCutTiles"))
    cut_tiles = compact(extract_method(projectile_source, "CutTiles"))
    cut_tiles_at = compact(extract_method(projectile_source, "CutTilesAt"))
    delegate_cut_tiles = compact(extract_method(delegate_source, "CutTiles"))
    tile_cut_ignorance = compact(extract_method(player_source, "GetTileCutIgnorance"))
    can_cut_tile = compact(extract_method(worldgen_source, "CanCutTile"))
    kill_tile = extract_method(worldgen_source, "KillTile")
    kill_tile_drops = extract_method(worldgen_source, "KillTile_GetItemDrops")

    set_defaults = extract_method(projectile_source, "SetDefaults")
    wooden_arrow_defaults = around_optional(set_defaults, "type == 1", radius=1400)
    fire_arrow_defaults = around_optional(set_defaults, "type == 2", radius=1400)
    unholy_arrow_defaults = around_optional(set_defaults, "type == 4", radius=1800)
    jesters_arrow_defaults = around_optional(set_defaults, "type == 5", radius=2200)
    bullet_defaults = around_optional(set_defaults, "type == 14", radius=1800)
    green_laser_defaults = around_optional(set_defaults, "type == 20", radius=1800)
    bone_defaults = around_optional(set_defaults, "type == 21", radius=1800)
    confetti_gun_defaults = compact(extract_type_if_block(set_defaults, 178))
    confetti_melee_defaults = compact(extract_type_if_block(set_defaults, 289))
    bone_arrow_defaults = compact(extract_type_if_block(set_defaults, 474))
    sound_gun_defaults = compact(extract_type_if_block(set_defaults, 1099))
    seed_defaults = around_optional(set_defaults, "type == 51", radius=1400)
    bone_shard_defaults = around_optional(set_defaults, "type == 1124", radius=1800)
    arrow_ai = extract_method(projectile_source, "AI_001")
    should_use_wind = compact(extract_method(projectile_source, "ShouldUseWindPhysics"))
    transform_type = compact(extract_method(projectile_source, "TransformType"))
    projectile_update = extract_method(projectile_source, "Update")
    projectile_ai = extract_method(projectile_source, "AI")
    collision_params = extract_method(projectile_source, "GetCollisionParams")
    handle_movement = extract_method(projectile_source, "HandleMovement")
    projectile_kill = extract_method(projectile_source, "Kill")

    print("projectile_wooden_arrow_defaults=" + wooden_arrow_defaults)
    print("projectile_fire_arrow_defaults=" + fire_arrow_defaults)
    print("projectile_unholy_arrow_defaults=" + unholy_arrow_defaults)
    print("projectile_jesters_arrow_defaults=" + jesters_arrow_defaults)
    print("projectile_bullet_defaults=" + bullet_defaults)
    print("projectile_green_laser_defaults=" + green_laser_defaults)
    wind_immunity = compact(extract_factory_initializer(projectile_id_source, "WindPhysicsImmunity"))
    if "CreateCustomSet<bool?>(null" not in wind_immunity:
        raise SystemExit("unexpected WindPhysicsImmunity default semantics")

    expected_ids = {
        "Seed": 51,
        "ConfettiGun": 178,
        "ConfettiMelee": 289,
        "BoneArrowFromMerchant": 474,
        "SoundGun": 1099,
        "BoneShard": 1124,
    }
    for name, raw_type in expected_ids.items():
        declaration = re.compile(
            rf"public const (?:short|int)\s+{re.escape(name)}\s*=\s*{raw_type}\s*;")
        if declaration.search(projectile_id_source) is None:
            raise SystemExit(f"ProjectileID.{name} != {raw_type} in pinned source")
        if re.search(rf"(?<!\d){raw_type}(?!\d)", wind_immunity):
            raise SystemExit(f"type {raw_type} unexpectedly overrides WindPhysicsImmunity")

    required_bone_arrow_defaults = (
        "arrow = true;",
        "width = 10;",
        "height = 10;",
        "aiStyle = 1;",
        "friendly = true;",
        "ranged = true;",
        "timeLeft = 1200;",
        "penetrate = 2;",
    )
    for token in required_bone_arrow_defaults:
        if token not in bone_arrow_defaults:
            raise SystemExit(f"type 474 default missing: {token}")
    for forbidden in ("tileCollide = false;", "ignoreWater = true;", "extraUpdates ="):
        if forbidden in bone_arrow_defaults:
            raise SystemExit(f"type 474 unexpected default: {forbidden}")

    for source_name, source_text in (
        ("AI_001", arrow_ai),
        ("AI", projectile_ai),
        ("Update", projectile_update),
        ("HandleMovement", handle_movement),
        ("GetCollisionParams", collision_params),
        ("CanCutTiles", can_cut_tiles),
    ):
        if count_type_comparisons(source_text, 474) != 0:
            raise SystemExit(f"type 474 unexpectedly special in {source_name}")

    if count_type_comparisons(projectile_kill, 474) != 1:
        raise SystemExit("type 474 Kill branch count changed")
    bone_arrow_kill = compact(extract_type_if_block(projectile_kill, 474))
    for token in ("SoundEngine.PlaySound", "Dust.NewDust"):
        if token not in bone_arrow_kill:
            raise SystemExit(f"type 474 visual Kill token missing: {token}")
    for token in ("NewProjectile(", "NewItem(", "KillTile(", "RequestNewItem("):
        if token in bone_arrow_kill:
            raise SystemExit(f"type 474 Kill gained authoritative side effect: {token}")

    required_sound_gun_defaults = (
        "width = 66;",
        "height = 66;",
        "aiStyle = 1;",
        "friendly = true;",
        "penetrate = -1;",
        "timeLeft = 600;",
        "tileCollide = false;",
        "magic = true;",
    )
    for token in required_sound_gun_defaults:
        if token not in sound_gun_defaults:
            raise SystemExit(f"type 1099 default missing: {token}")
    for forbidden in ("ignoreWater = true;", "extraUpdates ="):
        if forbidden in sound_gun_defaults:
            raise SystemExit(f"type 1099 unexpected default: {forbidden}")

    for source_name, source_text in (
        ("AI_001", arrow_ai),
        ("AI", projectile_ai),
        ("Update", projectile_update),
        ("HandleMovement", handle_movement),
        ("GetCollisionParams", collision_params),
        ("Kill", projectile_kill),
        ("CanCutTiles", can_cut_tiles),
    ):
        if count_type_comparisons(source_text, 1099) != 0:
            raise SystemExit(f"type 1099 unexpectedly special in {source_name}")

    for raw_type, name, defaults in (
        (178, "ConfettiGun", confetti_gun_defaults),
        (289, "ConfettiMelee", confetti_melee_defaults),
    ):
        for token in (
            "width = 10;",
            "height = 10;",
            "aiStyle = 1;",
            "alpha = 255;",
            "penetrate = -1;",
            "timeLeft = 2;",
        ):
            if token not in defaults:
                raise SystemExit(f"{name} default missing: {token}")
        for forbidden in ("tileCollide = false;", "ignoreWater = true;", "extraUpdates ="):
            if forbidden in defaults:
                raise SystemExit(f"{name} unexpected default: {forbidden}")

        for source_name, source_text in (
            ("AI_001", arrow_ai),
            ("AI", projectile_ai),
            ("Update", projectile_update),
            ("HandleMovement", handle_movement),
            ("GetCollisionParams", collision_params),
            ("CanCutTiles", can_cut_tiles),
        ):
            if count_type_comparisons(source_text, raw_type) != 0:
                raise SystemExit(f"{name} unexpectedly special in {source_name}")

        if count_type_comparisons(projectile_kill, raw_type) != 1:
            raise SystemExit(f"{name} Kill branch count changed")
        kill_block = compact(extract_type_if_block(projectile_kill, raw_type))
        for token in ("Dust.NewDust", "Gore.NewGore"):
            if token not in kill_block:
                raise SystemExit(f"{name} visual Kill token missing: {token}")
        for token in ("NewProjectile(", "NewItem(", "KillTile(", "RequestNewItem("):
            if token in kill_block:
                raise SystemExit(f"{name} Kill gained authoritative side effect: {token}")

    print("projectile_bone_defaults=" + bone_defaults)
    print("projectile_confetti_gun_defaults=" + confetti_gun_defaults)
    print("projectile_confetti_melee_defaults=" + confetti_melee_defaults)
    print("projectile_bone_arrow_from_merchant_defaults=" + bone_arrow_defaults)
    print("projectile_bone_arrow_from_merchant_kill=" + bone_arrow_kill)
    print("projectile_sound_gun_defaults=" + sound_gun_defaults)
    print("projectile_seed_defaults=" + seed_defaults)
    print("projectile_bone_shard_defaults=" + bone_shard_defaults)
    print("projectile_seed_ai001_contexts=" + all_type_comparison_contexts(arrow_ai, 51, radius=1800, limit=20))
    print("projectile_bone_shard_ai001_contexts=" + all_type_comparison_contexts(arrow_ai, 1124, radius=2600, limit=20))
    print("projectile_seed_kill_contexts=" + all_type_comparison_contexts(projectile_kill, 51, radius=1800, limit=20))
    print("projectile_bone_shard_kill_contexts=" + all_type_comparison_contexts(projectile_kill, 1124, radius=1800, limit=20))
    print("projectile_simple_ai1_wind_immunity=" + wind_immunity)
    print("projectile_ai_type21_contexts=" + all_type_comparison_contexts(extract_method(projectile_source, "AI"), 21, radius=2600, limit=20))
    print("projectile_collision_params_type21_contexts=" + all_type_comparison_contexts(extract_method(projectile_source, "GetCollisionParams"), 21, radius=1800, limit=20))
    print("projectile_handle_movement_type21_contexts=" + all_type_comparison_contexts(handle_movement, 21, radius=2200, limit=20))
    print("projectile_kill_type21_contexts=" + all_type_comparison_contexts(projectile_kill, 21, radius=2600, limit=20))
    for simple_ai2_type in (318, 330, 583, 589, 1012, 1111):
        print(f"projectile_ai2_type{simple_ai2_type}_defaults=" + around_optional(set_defaults, f"type == {simple_ai2_type}", radius=1600))
        print(f"projectile_ai2_type{simple_ai2_type}_ai_contexts=" + all_type_comparison_contexts(projectile_ai, simple_ai2_type, radius=1800, limit=20))
        print(f"projectile_ai2_type{simple_ai2_type}_collision_contexts=" + all_type_comparison_contexts(collision_params, simple_ai2_type, radius=1600, limit=20))
        print(f"projectile_ai2_type{simple_ai2_type}_movement_contexts=" + all_type_comparison_contexts(handle_movement, simple_ai2_type, radius=1600, limit=20))
        print(f"projectile_ai2_type{simple_ai2_type}_kill_contexts=" + all_type_comparison_contexts(projectile_kill, simple_ai2_type, radius=2200, limit=20))
    print("projectile_moon_globe_type996_kill_contexts=" + all_type_comparison_contexts(projectile_kill, 996, radius=2600, limit=20))
    print("projectile_ai001_ai0_increment=" + around_optional(arrow_ai, "ai[0]++;", radius=1200))
    print("projectile_ai001_gravity=" + around_optional(arrow_ai, "ai[0] >= 15f", radius=1500))
    print("projectile_ai001_fall_cap=" + around_last(arrow_ai, "velocity.Y > 16f", radius=900))
    print("projectile_ai001_type4_contexts=" + all_type_comparison_contexts(arrow_ai, 4, radius=2600, limit=20))
    print("projectile_ai001_type5_contexts=" + all_type_comparison_contexts(arrow_ai, 5, radius=3200, limit=20))
    print("projectile_ai001_type14_contexts=" + all_type_comparison_contexts(arrow_ai, 14, radius=2600, limit=20))
    print("projectile_ai001_type20_contexts=" + all_type_comparison_contexts(arrow_ai, 20, radius=3000, limit=20))
    print("projectile_update_type5_contexts=" + all_type_comparison_contexts(projectile_update, 5, radius=2600, limit=20))
    print("projectile_update_type14_contexts=" + all_type_comparison_contexts(projectile_update, 14, radius=2600, limit=20))
    print("projectile_update_type20_contexts=" + all_type_comparison_contexts(projectile_update, 20, radius=2600, limit=20))
    print("projectile_should_use_wind_physics=" + should_use_wind)
    print("projectile_wind_speed_context=" + around_optional(projectile_source, "ShouldUseWindPhysics() &&", radius=2200))
    print("projectile_transform_type=" + transform_type)
    print("projectile_fire_arrow_wet_transform=" + around_optional(
        projectile_source,
        "if (type == 2) { TransformType(1);",
        radius=2600))

    print(f"projectile_handle_movement_length={len(compact(handle_movement))}")
    print("projectile_handle_movement_ai1_contexts=" + all_contexts(handle_movement, "aiStyle == 1", radius=2600, limit=20))
    print("projectile_handle_movement_type20_contexts=" + all_type_comparison_contexts(handle_movement, 20, radius=2600, limit=20))
    print("projectile_handle_movement_last_velocity_contexts=" + all_contexts(handle_movement, "lastVelocity", radius=1800, limit=20))
    print("projectile_handle_movement_kill_contexts=" + all_contexts(handle_movement, "Kill();", radius=2000, limit=20))

    print(f"projectile_kill_length={len(compact(projectile_kill))}")
    print("projectile_kill_helpers=" + called_helpers(projectile_kill, "Kill_"))
    print("projectile_kill_type1_effect=" + around_optional(
        projectile_kill,
        "if (type == 1 || type == 81 || type == 98 || type == 980 || type == 1073)",
        radius=4200))
    print("projectile_kill_type2_contexts=" + all_contexts(projectile_kill, "type == 2", radius=3000, limit=20))
    print("projectile_kill_type4_contexts=" + all_type_comparison_contexts(projectile_kill, 4, radius=3000, limit=20))
    print("projectile_kill_type5_contexts=" + all_type_comparison_contexts(projectile_kill, 5, radius=3600, limit=20))
    print("projectile_kill_type14_contexts=" + all_type_comparison_contexts(projectile_kill, 14, radius=3000, limit=20))
    print("projectile_kill_type20_contexts=" + all_type_comparison_contexts(projectile_kill, 20, radius=3200, limit=20))
    print("projectile_kill_request_new_item=" + around_optional(
        projectile_kill,
        "Item.RequestNewItem(GetItemSource_DropAsItem()",
        radius=5200))
    print("projectile_kill_no_drop_item=" + around_optional(projectile_kill, "noDropItem", radius=4200))
    print("projectile_kill_new_item_mentions=" + matching_lines(projectile_kill, "NewItem", limit=80))

    print("projectile_can_cut_tiles=" + can_cut_tiles)
    print("projectile_cut_tiles=" + cut_tiles)
    print("projectile_cut_tiles_at=" + cut_tiles_at)
    print("projectile_cut_tiles_callsite=" + around_first(projectile_source, "CutTiles();"))
    print("delegate_methods_cut_tiles=" + delegate_cut_tiles)
    print("player_get_tile_cut_ignorance=" + tile_cut_ignorance)
    print("worldgen_can_cut_tile=" + can_cut_tile)
    print("main_tile_cut_mentions=" + matching_lines(main_source, "tileCut"))
    print("tile_id_cut_ignore_context=" + around_first(tile_id_source, "TileCutIgnore", radius=2600))

    compact_kill_tile = compact(kill_tile)
    compact_drops = compact(kill_tile_drops)
    print(f"worldgen_kill_tile_length={len(compact_kill_tile)}")
    print("worldgen_kill_tile_helpers=" + called_helpers(kill_tile, "KillTile_"))
    print("worldgen_kill_tile_active_false_last=" + around_last(kill_tile, "active(active: false)", radius=1200))
    print("worldgen_kill_tile_type_zero_last=" + around_last(kill_tile, "type = 0", radius=1200))
    print("worldgen_kill_tile_square_frame_last=" + around_last(kill_tile, "SquareTileFrame", radius=1200))
    print(f"worldgen_kill_tile_get_item_drops_length={len(compact_drops)}")
    print("worldgen_cuttable_drop_contexts=" + relevant_drop_contexts(kill_tile_drops))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
