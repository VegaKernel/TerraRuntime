#!/usr/bin/env python3
"""Verify TerraRuntime tree-growth catalogs and framing against pinned TerrariaServer 1.4.5.8 source."""

import argparse
import hashlib
import re
from pathlib import Path


TREE_GROUND = {2, 23, 60, 70, 109, 147, 199, 477, 492, 633, 661, 662}
COMMON_SAPLING = {20, 590, 595, 615}
REPLACEABLE_GROWTH = COMMON_SAPLING | {
    3, 24, 32, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 184, 201, 233, 352,
    485, 529, 530, 637, 655,
}
PLANT_GROWTH_WALL = {
    0, 63, 64, 65, 66, 67, 68, 69, 70, 74, 80, 81, 106, 107, 138, 139, 140, 141,
    145, 150, 152, 245, 264, 265, 268, 315, 317,
}


def extract_method(source: str, name: str) -> str:
    match = re.search(
        rf"(?m)^\s*(?:public|private|internal|protected)[^\n;]*\b{re.escape(name)}\s*\([^\n)]*\)\s*",
        source,
    )
    if not match:
        raise SystemExit(f"Could not locate method {name} in pinned source.")
    brace = source.find("{", match.end())
    if brace < 0:
        raise SystemExit(f"Method {name} has no body.")

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
                    return source[match.start():index + 1]
    raise SystemExit(f"Method {name} body did not terminate.")


def parse_factory_set(source: str, name: str) -> set[int]:
    match = re.search(
        rf"\b{name}\s*=\s*Factory\.CreateBoolSet\((?P<values>[^;]+)\);",
        source,
    )
    if not match:
        raise SystemExit(f"Could not locate {name} bool set in pinned source.")
    return {int(value) for value in re.findall(r"\b\d+\b", match.group("values"))}


def parse_case_values(source: str) -> set[int]:
    return {int(value) for value in re.findall(r"\bcase\s+(\d+)\s*:", source)}


def words(values: set[int], count: int) -> list[int]:
    result = [0] * ((count + 63) // 64)
    for value in values:
        result[value >> 6] |= 1 << (value & 63)
    return result


def runtime_words(source: str, property_name: str) -> list[int]:
    match = re.search(
        rf"\b{property_name}\s*=>\s*\[(?P<words>.*?)\];",
        source,
        re.DOTALL,
    )
    if not match:
        raise SystemExit(f"Runtime catalog is missing {property_name}.")
    return [int(value, 16) for value in re.findall(r"0x([0-9A-Fa-f]+)UL", match.group("words"))]


def require(source: str, pattern: str, label: str) -> None:
    if re.search(pattern, source, re.DOTALL) is None:
        raise SystemExit(f"Pinned tree-growth contract missing {label}: /{pattern}/")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    parser.add_argument("--wall-id", required=True)
    parser.add_argument("--runtime-catalog", required=True)
    parser.add_argument("--runtime-grower", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    tile_id = Path(args.tile_id).read_text(encoding="utf-8")
    wall_id = Path(args.wall_id).read_text(encoding="utf-8")
    runtime_catalog = Path(args.runtime_catalog).read_text(encoding="utf-8")
    runtime_grower = Path(args.runtime_grower).read_text(encoding="utf-8")

    grow_tree = extract_method(world_gen, "GrowTree")
    ground_source = parse_case_values(extract_method(world_gen, "IsTileTypeFitForTree"))
    common_sapling_source = parse_factory_set(tile_id, "CommonSapling")
    plant_wall_source = parse_factory_set(wall_id, "AllowsPlantsToGrow")
    empty_check = extract_method(world_gen, "EmptyTileCheck")
    replaceable_branch = empty_check[empty_check.find("if (flag && !TileID.Sets.CommonSapling"):]
    replaceable_source = common_sapling_source | parse_case_values(replaceable_branch)

    source_sets = [
        ("tree ground", ground_source, TREE_GROUND),
        ("common sapling", common_sapling_source, COMMON_SAPLING),
        ("replaceable growth", replaceable_source, REPLACEABLE_GROWTH),
        ("plant-growth wall", plant_wall_source, PLANT_GROWTH_WALL),
    ]
    for label, actual, expected in source_sets:
        if actual != expected:
            raise SystemExit(f"Pinned {label} set changed: expected {sorted(expected)}, got {sorted(actual)}")

    source_markers = [
        (r"genRand\.Next\(5,\s*17\)\s*\+\s*treeHeightAddon", "tree-height roll"),
        (r"genRand\.Next\(10\)", "segment-feature roll"),
        (r"num5\s*==\s*5\s*\|\|\s*num5\s*==\s*7", "left-branch feature"),
        (r"num5\s*!=\s*6\s*&&\s*num5\s*!=\s*7", "right-branch feature"),
        (r"num6\s*=\s*genRand\.Next\(3\)", "root-shape roll"),
        (r"genRand\.Next\(13\)\s*!=\s*0", "leafy-top roll"),
        (r"frameX\s*=\s*110;\s*Main\.tile\[i,\s*k\]\.frameY\s*=\s*110", "both-branch trunk frame"),
        (r"frameX\s*=\s*22;\s*Main\.tile\[i,\s*j\s*-\s*num2\]\.frameY\s*=\s*242", "leafy top frame"),
    ]
    for pattern, label in source_markers:
        require(grow_tree, pattern, label)

    runtime_sets = [
        ("TreeGroundWords", TREE_GROUND, 754),
        ("CommonSaplingWords", COMMON_SAPLING, 754),
        ("ReplaceableGrowthWords", REPLACEABLE_GROWTH, 754),
        ("PlantGrowthWallWords", PLANT_GROWTH_WALL, 367),
    ]
    for property_name, values, count in runtime_sets:
        actual = runtime_words(runtime_catalog, property_name)
        expected = words(values, count)
        if actual != expected:
            raise SystemExit(f"Runtime {property_name} differs from the pinned source set.")

    runtime_markers = [
        "VanillaTreeFrameCatalog1458.Trunk(feature, variant)",
        "VanillaTreeFrameCatalog1458.LeftBranch(leafy, variant)",
        "VanillaTreeFrameCatalog1458.RightBranch(leafy, variant)",
        "VanillaTreeFrameCatalog1458.RightRoot(variant)",
        "VanillaTreeFrameCatalog1458.LeftRoot(variant)",
        "VanillaTreeFrameCatalog1458.Top(leafyTop, topVariant)",
    ]
    for marker in runtime_markers:
        if marker not in runtime_grower:
            raise SystemExit(f"Runtime grower no longer routes {marker} through the framing catalog.")

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "scope=ordinary WorldGen.GrowTree growth gate, RNG order, branches, roots and top framing",
        f"WorldGen_GrowTree_sha256={hashlib.sha256(grow_tree.encode('utf-8')).hexdigest()}",
        f"tree_ground_count={len(TREE_GROUND)}",
        f"common_sapling_count={len(COMMON_SAPLING)}",
        f"replaceable_growth_count={len(REPLACEABLE_GROWTH)}",
        f"plant_growth_wall_count={len(PLANT_GROWTH_WALL)}",
        "status=verified",
    ]
    for line in lines:
        print(line)
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
