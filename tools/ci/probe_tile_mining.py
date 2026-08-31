#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def signatures(source: str, name: str) -> list[str]:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal)(?: static)? [^\r\n{{;]*\b{re.escape(name)}\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate exact signature: {signature}")

    brace = source.find("{", start + len(signature))
    if brace < 0:
        raise SystemExit(f"Method declaration has no body: {signature}")

    depth = 0
    in_string = False
    in_char = False
    escaped = False
    for index in range(brace, len(source)):
        ch = source[index]
        if escaped:
            escaped = False
        elif ch == "\\" and (in_string or in_char):
            escaped = True
        elif ch == '"' and not in_char:
            in_string = not in_string
        elif ch == "'" and not in_string:
            in_char = not in_char
        elif not in_string and not in_char:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return source[start:index + 1]

    raise SystemExit(f"Method body did not terminate: {signature}")


def single_method(source: str, name: str) -> str:
    matches = signatures(source, name)
    if len(matches) != 1:
        raise SystemExit(f"Expected exactly one {name} method, found {len(matches)}.")
    return compact(extract_method(source, matches[0]))


def exact_method(source: str, signature: str) -> str:
    return compact(extract_method(source, signature))


def require(source: str, needle: str, description: str) -> None:
    if needle not in source:
        raise SystemExit(f"Pinned Terraria 1.4.5.8 mining contract changed: {description}.")


def read_tile_id(source: str, name: str, expected: int) -> int:
    match = re.search(rf"\b{re.escape(name)}\s*=\s*(\d+)\s*;", source)
    if match is None:
        raise SystemExit(f"Could not locate TileID.{name} in pinned source.")
    value = int(match.group(1))
    if value != expected:
        raise SystemExit(f"Expected TileID.{name}={expected}, got {value}.")
    return value


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify the pinned TerrariaServer 1.4.5.8 simple tile-mining authority slice."
    )
    parser.add_argument("--player", required=True)
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    args = parser.parse_args()

    player = Path(args.player).read_text(encoding="utf-8")
    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    tile_id_source = compact(Path(args.tile_id).read_text(encoding="utf-8"))

    dirt = read_tile_id(tile_id_source, "Dirt", 0)
    stone = read_tile_id(tile_id_source, "Stone", 1)
    grass = read_tile_id(tile_id_source, "Grass", 2)
    sand = read_tile_id(tile_id_source, "Sand", 53)
    ebonstone = read_tile_id(tile_id_source, "Ebonstone", 25)
    lihzahrd = read_tile_id(tile_id_source, "LihzahrdBrick", 226)

    pick_tile = single_method(player, "PickTile")
    determine = single_method(player, "PickTile_DetermineDamage")
    get_damage = single_method(player, "GetPickaxeDamage")
    transform = single_method(player, "DoesPickTargetTransformOnKill")
    can_kill = exact_method(
        world_gen,
        "public static bool CanKillTile(int i, int j, out bool blockDamaged)",
    )

    require(pick_tile, "PickTile_DetermineDamage(x, y, pickPower", "PickTile damage delegation changed")
    require(pick_tile, "if (hitTile.AddDamage(bufferIndex, damage) >= 100)", "mining hit accumulation changed")
    require(determine, "damage = GetPickaxeDamage(x, y, pickPower", "pickaxe damage lookup changed")
    require(determine, "if (!WorldGen.CanKillTile(x, y)) { damage = 0; }", "CanKillTile gate changed")
    require(
        determine,
        "DoesPickTargetTransformOnKill(hitTile, damage, x, y, pickPower, bufferIndex, tileTarget)",
        "transform-on-kill gate changed",
    )

    # Dirt and Sand receive the generic pick damage plus the pinned fast-dig bonus. Stone is absent from every
    # special tile-id branch and therefore remains on the generic +pickPower path.
    require(get_damage, f"tileTarget.type == {dirt}", "Dirt fast-dig evidence disappeared")
    require(get_damage, f"tileTarget.type == {sand}", "Sand fast-dig evidence disappeared")
    if re.search(rf"tileTarget\.type\s*==\s*{stone}(?!\d)", get_damage):
        raise SystemExit("Stone gained special GetPickaxeDamage semantics; revisit the simple-kill slice.")

    # Grass is not a generic clear: a completed pick hit transforms the tile instead. High-tier examples also have
    # explicit pick-power gates, proving that non-frame-important does not mean generic-removal-safe.
    require(transform, f"tileTarget.type == {grass}", "Grass transform-on-kill evidence disappeared")
    require(
        get_damage,
        f"(tileTarget.type == {ebonstone} || tileTarget.type == 203) && pickPower < 65",
        "Ebonstone/Crimstone 65-pick gate changed",
    )
    require(
        get_damage,
        f"(tileTarget.type == {lihzahrd} || tileTarget.type == 237) && pickPower < 210",
        "Lihzahrd 210-pick gate changed",
    )

    require(can_kill, "if (!tile.active()) { return false; }", "inactive-tile rejection changed")
    require(can_kill, "if (tile.wall == 350) { return false; }", "wall-dependent breakability changed")
    require(can_kill, "if (TileID.Sets.Boulders[tile.type] && CheckBoulderChest(i, j))", "boulder/container guard changed")
    require(can_kill, "case 21: case 467:", "chest destruction guard changed")

    print("tile_mining_pick_tile_hit_threshold=100")
    print("tile_mining_simple_kill_ids=0,1,53")
    print("tile_mining_grass_transform_id=2")
    print("tile_mining_ebonstone_min_pick=65")
    print("tile_mining_lihzahrd_min_pick=210")
    print("tile_mining_worldgen_can_kill=environment-dependent")
    print("tile_mining_probe=verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
