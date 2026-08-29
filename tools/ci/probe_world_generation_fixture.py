#!/usr/bin/env python3
import argparse
import hashlib
import json
import struct
from pathlib import Path

EXPECTED_VERSION = 326
EXPECTED_MAGIC = b"relogic"
EXPECTED_FILE_TYPE = 2
EXPECTED_SECTION_COUNT = 11
EXPECTED_FRAME_IMPORTANCE_COUNT = 754
EXPECTED_FRAME_IMPORTANCE_BYTES = (EXPECTED_FRAME_IMPORTANCE_COUNT + 7) // 8
EXPECTED_ENVELOPE_LENGTH = (
    4 + len(EXPECTED_MAGIC) + 1 + 4 + 8 + 2 + EXPECTED_SECTION_COUNT * 4 + 2 + EXPECTED_FRAME_IMPORTANCE_BYTES
)


def read_exact(data: bytes, offset: int, length: int) -> tuple[bytes, int]:
    end = offset + length
    if end > len(data):
        raise SystemExit(f"World file truncated at offset {offset}: need {length} bytes, have {len(data) - offset}.")
    return data[offset:end], end


def unpack(fmt: str, data: bytes, offset: int) -> tuple[object, int]:
    size = struct.calcsize(fmt)
    raw, offset = read_exact(data, offset, size)
    return struct.unpack(fmt, raw)[0], offset


def read_7bit_int(data: bytes, offset: int) -> tuple[int, int]:
    value = 0
    shift = 0
    for _ in range(5):
        byte, offset = unpack("<B", data, offset)
        value |= (int(byte) & 0x7F) << shift
        if (int(byte) & 0x80) == 0:
            return value, offset
        shift += 7
    raise SystemExit("Invalid BinaryReader 7-bit encoded string length.")


def read_string(data: bytes, offset: int) -> tuple[str, int]:
    length, offset = read_7bit_int(data, offset)
    raw, offset = read_exact(data, offset, length)
    try:
        return raw.decode("utf-8"), offset
    except UnicodeDecodeError as exc:
        raise SystemExit(f"Invalid UTF-8 string at offset {offset - length}: {exc}") from exc


def read_bool(data: bytes, offset: int) -> tuple[bool, int]:
    raw, offset = unpack("<B", data, offset)
    if raw not in (0, 1):
        raise SystemExit(f"Invalid Boolean byte {raw} at offset {offset - 1}.")
    return raw == 1, offset


def read_many(fmt: str, count: int, data: bytes, offset: int) -> tuple[list[object], int]:
    values: list[object] = []
    for _ in range(count):
        value, offset = unpack(fmt, data, offset)
        values.append(value)
    return values, offset


def read_bools(count: int, data: bytes, offset: int) -> tuple[list[bool], int]:
    values: list[bool] = []
    for _ in range(count):
        value, offset = read_bool(data, offset)
        values.append(value)
    return values, offset


def read_fresh_header_and_flags(data: bytes, start: int, end: int) -> dict[str, object]:
    offset = start
    name, offset = read_string(data, offset)
    seed, offset = read_string(data, offset)
    generator_version, offset = unpack("<Q", data, offset)
    unique_id, offset = read_exact(data, offset, 16)
    world_id, offset = unpack("<i", data, offset)
    bounds, offset = read_many("<i", 4, data, offset)
    height, offset = unpack("<i", data, offset)
    width, offset = unpack("<i", data, offset)

    game_mode, offset = unpack("<i", data, offset)
    special_world_flags, offset = read_bools(9, data, offset)
    creation_time_binary, offset = unpack("<q", data, offset)
    last_played_binary, offset = unpack("<q", data, offset)
    moon_type, offset = unpack("<B", data, offset)
    tree_x, offset = read_many("<i", 3, data, offset)
    tree_styles, offset = read_many("<i", 4, data, offset)
    cave_back_x, offset = read_many("<i", 3, data, offset)
    cave_back_styles, offset = read_many("<i", 4, data, offset)
    other_back_styles, offset = read_many("<i", 3, data, offset)
    spawn, offset = read_many("<i", 2, data, offset)
    world_surface, offset = unpack("<d", data, offset)
    rock_layer, offset = unpack("<d", data, offset)
    time, offset = unpack("<d", data, offset)
    day_time, offset = read_bool(data, offset)
    moon_phase, offset = unpack("<i", data, offset)
    blood_moon, offset = read_bool(data, offset)
    eclipse, offset = read_bool(data, offset)
    dungeon, offset = read_many("<i", 2, data, offset)
    crimson, offset = read_bool(data, offset)

    progression, offset = read_bools(11, data, offset)
    rescued_town_npcs, offset = read_bools(3, data, offset)
    invasion_progression, offset = read_bools(4, data, offset)
    shadow_orb_smashed, offset = read_bool(data, offset)
    spawn_meteor, offset = read_bool(data, offset)
    shadow_orb_count, offset = unpack("<B", data, offset)
    altar_count, offset = unpack("<i", data, offset)
    hard_mode, offset = read_bool(data, offset)
    after_party_of_doom, offset = read_bool(data, offset)
    invasion_delay, offset = unpack("<i", data, offset)
    invasion_size, offset = unpack("<i", data, offset)
    invasion_type, offset = unpack("<i", data, offset)
    invasion_x, offset = unpack("<d", data, offset)
    slime_rain_time, offset = unpack("<d", data, offset)
    sundial_cooldown, offset = unpack("<B", data, offset)
    raining, offset = read_bool(data, offset)
    rain_time, offset = unpack("<i", data, offset)
    max_rain, offset = unpack("<f", data, offset)
    hardmode_ore_tiers, offset = read_many("<i", 3, data, offset)
    backgrounds, offset = read_many("<B", 8, data, offset)
    cloud_background, offset = unpack("<i", data, offset)
    cloud_count, offset = unpack("<h", data, offset)
    wind_speed, offset = unpack("<f", data, offset)

    angler_count, offset = unpack("<i", data, offset)
    angler_names: list[str] = []
    for _ in range(int(angler_count)):
        value, offset = read_string(data, offset)
        angler_names.append(value)
    saved_angler, offset = read_bool(data, offset)
    angler_quest, offset = unpack("<i", data, offset)
    saved_stylist, offset = read_bool(data, offset)
    saved_tax_collector, offset = read_bool(data, offset)
    saved_golfer, offset = read_bool(data, offset)
    invasion_size_start, offset = unpack("<i", data, offset)
    cultist_delay, offset = unpack("<i", data, offset)

    banner_kill_count, offset = unpack("<h", data, offset)
    banner_kills, offset = read_many("<i", int(banner_kill_count), data, offset)
    banner_claim_count, offset = unpack("<h", data, offset)
    banner_claims, offset = read_many("<H", int(banner_claim_count), data, offset)

    fast_forward_dawn, offset = read_bool(data, offset)
    late_progression, offset = read_bools(13, data, offset)
    lunar_active, offset = read_bools(5, data, offset)
    party_manual, offset = read_bool(data, offset)
    party_genuine, offset = read_bool(data, offset)
    party_cooldown, offset = unpack("<i", data, offset)
    party_npc_count, offset = unpack("<i", data, offset)
    party_npcs, offset = read_many("<i", int(party_npc_count), data, offset)

    sandstorm_happening, offset = read_bool(data, offset)
    sandstorm_time_left, offset = unpack("<i", data, offset)
    sandstorm_severity, offset = unpack("<f", data, offset)
    sandstorm_intended_severity, offset = unpack("<f", data, offset)
    saved_bartender, offset = read_bool(data, offset)
    dd2_progress, offset = read_bools(3, data, offset)
    later_backgrounds, offset = read_many("<B", 5, data, offset)
    combat_book_used, offset = read_bool(data, offset)
    lantern_cooldown, offset = unpack("<i", data, offset)
    lantern_flags, offset = read_bools(3, data, offset)

    tree_top_count, offset = unpack("<i", data, offset)
    tree_top_variations, offset = read_many("<i", int(tree_top_count), data, offset)
    force_halloween_today, offset = read_bool(data, offset)
    force_xmas_today, offset = read_bool(data, offset)
    prehardmode_ore_tiers, offset = read_many("<i", 4, data, offset)
    pet_and_boss_flags, offset = read_bools(7, data, offset)
    unlock_group_one, offset = read_bools(4, data, offset)
    unlocked_truffle, offset = read_bool(data, offset)
    unlock_group_two, offset = read_bools(3, data, offset)
    combat_book_two, offset = read_bool(data, offset)
    peddlers_satchel, offset = read_bool(data, offset)
    slime_unlocks, offset = read_bools(7, data, offset)
    fast_forward_dusk, offset = read_bool(data, offset)
    moondial_cooldown, offset = unpack("<B", data, offset)
    holiday_forever, offset = read_bools(2, data, offset)
    vampire_seed, offset = read_bool(data, offset)
    infected_seed, offset = read_bool(data, offset)
    meteor_shower_count, offset = unpack("<i", data, offset)
    coin_rain, offset = unpack("<i", data, offset)
    team_based_spawns_seed, offset = read_bool(data, offset)
    extra_spawn_count, offset = unpack("<B", data, offset)
    extra_spawns: list[list[int]] = []
    for _ in range(int(extra_spawn_count)):
        x, offset = unpack("<h", data, offset)
        y, offset = unpack("<h", data, offset)
        extra_spawns.append([int(x), int(y)])
    seed_tail_flags, offset = read_bools(3, data, offset)
    manifest, offset = read_string(data, offset)

    if offset != end:
        raise SystemExit(f"SaveWorldFlags length mismatch: parsed to {offset}, section 1 starts at {end}.")

    return {
        "name": name,
        "seed": seed,
        "world_generator_version": int(generator_version),
        "unique_id_hex": unique_id.hex(),
        "world_id": int(world_id),
        "bounds": [int(value) for value in bounds],
        "height_tiles": int(height),
        "width_tiles": int(width),
        "game_mode": int(game_mode),
        "special_world_flags": special_world_flags,
        "creation_time_binary": int(creation_time_binary),
        "last_played_binary": int(last_played_binary),
        "moon_type": int(moon_type),
        "tree_x": [int(value) for value in tree_x],
        "tree_styles": [int(value) for value in tree_styles],
        "cave_back_x": [int(value) for value in cave_back_x],
        "cave_back_styles": [int(value) for value in cave_back_styles],
        "ice_jungle_hell_back_styles": [int(value) for value in other_back_styles],
        "spawn": [int(value) for value in spawn],
        "world_surface": world_surface,
        "rock_layer": rock_layer,
        "time": time,
        "day_time": day_time,
        "moon_phase": int(moon_phase),
        "blood_moon": blood_moon,
        "eclipse": eclipse,
        "dungeon": [int(value) for value in dungeon],
        "crimson": crimson,
        "progression_flags": progression,
        "rescued_town_npc_flags": rescued_town_npcs,
        "invasion_progression_flags": invasion_progression,
        "shadow_orb_smashed": shadow_orb_smashed,
        "spawn_meteor": spawn_meteor,
        "shadow_orb_count": int(shadow_orb_count),
        "altar_count": int(altar_count),
        "hard_mode": hard_mode,
        "after_party_of_doom": after_party_of_doom,
        "invasion_delay": int(invasion_delay),
        "invasion_size": int(invasion_size),
        "invasion_type": int(invasion_type),
        "invasion_x": invasion_x,
        "slime_rain_time": slime_rain_time,
        "sundial_cooldown": int(sundial_cooldown),
        "raining": raining,
        "rain_time": int(rain_time),
        "max_rain": max_rain,
        "hardmode_ore_tiers": [int(value) for value in hardmode_ore_tiers],
        "backgrounds": [int(value) for value in backgrounds],
        "cloud_background": int(cloud_background),
        "cloud_count": int(cloud_count),
        "wind_speed": wind_speed,
        "angler_names": angler_names,
        "saved_angler": saved_angler,
        "angler_quest": int(angler_quest),
        "saved_stylist": saved_stylist,
        "saved_tax_collector": saved_tax_collector,
        "saved_golfer": saved_golfer,
        "invasion_size_start": int(invasion_size_start),
        "cultist_delay": int(cultist_delay),
        "banner_kills": [int(value) for value in banner_kills],
        "banner_claims": [int(value) for value in banner_claims],
        "fast_forward_dawn": fast_forward_dawn,
        "late_progression_flags": late_progression,
        "lunar_active_flags": lunar_active,
        "party_manual": party_manual,
        "party_genuine": party_genuine,
        "party_cooldown": int(party_cooldown),
        "party_npcs": [int(value) for value in party_npcs],
        "sandstorm_happening": sandstorm_happening,
        "sandstorm_time_left": int(sandstorm_time_left),
        "sandstorm_severity": sandstorm_severity,
        "sandstorm_intended_severity": sandstorm_intended_severity,
        "saved_bartender": saved_bartender,
        "dd2_progress": dd2_progress,
        "later_backgrounds": [int(value) for value in later_backgrounds],
        "combat_book_used": combat_book_used,
        "lantern_cooldown": int(lantern_cooldown),
        "lantern_flags": lantern_flags,
        "tree_top_variations": [int(value) for value in tree_top_variations],
        "force_halloween_today": force_halloween_today,
        "force_xmas_today": force_xmas_today,
        "prehardmode_ore_tiers": [int(value) for value in prehardmode_ore_tiers],
        "pet_and_boss_flags": pet_and_boss_flags,
        "unlock_group_one": unlock_group_one,
        "unlocked_truffle": unlocked_truffle,
        "unlock_group_two": unlock_group_two,
        "combat_book_two": combat_book_two,
        "peddlers_satchel": peddlers_satchel,
        "slime_unlocks": slime_unlocks,
        "fast_forward_dusk": fast_forward_dusk,
        "moondial_cooldown": int(moondial_cooldown),
        "holiday_forever_flags": holiday_forever,
        "vampire_seed": vampire_seed,
        "infected_seed": infected_seed,
        "meteor_shower_count": int(meteor_shower_count),
        "coin_rain": int(coin_rain),
        "team_based_spawns_seed": team_based_spawns_seed,
        "extra_spawns": extra_spawns,
        "dual_more_no_lightning_flags": seed_tail_flags,
        "manifest": manifest,
        "section_zero_length": end - start,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Extract canonical Terraria 1.4.5.8 fresh-world data from an official generated fixture."
    )
    parser.add_argument("--world", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    world_path = Path(args.world)
    data = world_path.read_bytes()
    offset = 0

    version, offset = unpack("<i", data, offset)
    magic, offset = read_exact(data, offset, len(EXPECTED_MAGIC))
    file_type, offset = unpack("<B", data, offset)
    revision, offset = unpack("<I", data, offset)
    favorite_flags, offset = unpack("<Q", data, offset)
    section_count, offset = unpack("<h", data, offset)

    if version != EXPECTED_VERSION:
        raise SystemExit(f"Expected world version {EXPECTED_VERSION}, got {version}.")
    if magic != EXPECTED_MAGIC:
        raise SystemExit(f"Expected magic {EXPECTED_MAGIC!r}, got {magic!r}.")
    if file_type != EXPECTED_FILE_TYPE:
        raise SystemExit(f"Expected file type {EXPECTED_FILE_TYPE}, got {file_type}.")
    if section_count != EXPECTED_SECTION_COUNT:
        raise SystemExit(f"Expected {EXPECTED_SECTION_COUNT} sections, got {section_count}.")

    section_offsets: list[int] = []
    for _ in range(section_count):
        value, offset = unpack("<i", data, offset)
        section_offsets.append(int(value))

    frame_importance_count, offset = unpack("<h", data, offset)
    if frame_importance_count != EXPECTED_FRAME_IMPORTANCE_COUNT:
        raise SystemExit(
            f"Expected {EXPECTED_FRAME_IMPORTANCE_COUNT} frame-importance bits, got {frame_importance_count}."
        )

    frame_importance, offset = read_exact(data, offset, EXPECTED_FRAME_IMPORTANCE_BYTES)
    if offset != EXPECTED_ENVELOPE_LENGTH:
        raise SystemExit(f"Envelope length mismatch: expected {EXPECTED_ENVELOPE_LENGTH}, got {offset}.")
    if section_offsets[0] != EXPECTED_ENVELOPE_LENGTH:
        raise SystemExit(
            f"First section pointer must equal envelope length {EXPECTED_ENVELOPE_LENGTH}, got {section_offsets[0]}."
        )
    if any(left >= right for left, right in zip(section_offsets, section_offsets[1:])):
        raise SystemExit(f"Section pointers are not strictly increasing: {section_offsets}")
    if section_offsets[-1] > len(data):
        raise SystemExit(
            f"Last section pointer {section_offsets[-1]} exceeds world length {len(data)}."
        )

    fresh_metadata = read_fresh_header_and_flags(data, section_offsets[0], section_offsets[1])
    set_tile_ids = [
        tile_id
        for tile_id in range(frame_importance_count)
        if frame_importance[tile_id >> 3] & (1 << (tile_id & 7))
    ]
    fixture = {
        "terraria_version": "1.4.5.8",
        "world_file_version": version,
        "file_type": file_type,
        "revision": revision,
        "favorite_flags": favorite_flags,
        "section_count": section_count,
        "section_offsets": section_offsets,
        "frame_importance_count": frame_importance_count,
        "frame_importance_hex": frame_importance.hex(),
        "frame_importance_sha256": hashlib.sha256(frame_importance).hexdigest(),
        "frame_important_tile_ids": set_tile_ids,
        "fresh_metadata": fresh_metadata,
        "world_sha256": hashlib.sha256(data).hexdigest(),
        "world_length": len(data),
    }

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(fixture, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(f"world_version={version}")
    print(f"world_length={len(data)}")
    print(f"frame_importance_count={frame_importance_count}")
    print(f"frame_importance_sha256={fixture['frame_importance_sha256']}")
    print(f"frame_important_tile_count={len(set_tile_ids)}")
    print(f"world_generator_version={fresh_metadata['world_generator_version']}")
    print(f"hardmode_ore_tiers={fresh_metadata['hardmode_ore_tiers']}")
    print(f"prehardmode_ore_tiers={fresh_metadata['prehardmode_ore_tiers']}")
    print(f"fresh_time={fresh_metadata['time']} day_time={fresh_metadata['day_time']}")
    print(f"manifest_length={len(str(fresh_metadata['manifest']).encode('utf-8'))}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
