#!/usr/bin/env python3
"""Verify the clean-room Smooth World catalog and decision routes against TerrariaServer 1.4.5.8."""

import argparse
import hashlib
import re
from pathlib import Path


EXPECTED = {
    "CannotClearDuringGeneration": {396, 400, 401, 397, 398, 399, 404, 368, 367, 41, 43, 44, 481, 482, 483, 226, 237},
    "PreventsGenerationSlopes": {48, 137, 232, 191, 151, 274, 135, 442, 428},
    "SandConversionFamily": {53, 112, 116, 234},
    "ForbidsSlopeBelow": {21, 26, 77, 88, 235, 237, 441, 467, 468, 470, 475, 488, 597},
    "SecondPhaseExclusions": {137, 48, 232, 191, 151, 274, 75, 76},
    "UnsupportedGapNeighbors": {190, 48, 232},
    "TreeTrunks": {5, 72, 583, 584, 585, 586, 587, 588, 589, 596, 616, 634},
    "ProtectsDifferentSupport": {21, 26, 72, 77, 88, 467, 488},
}


def extract_method(source: str, name: str) -> str:
    match = re.search(rf"(?m)^\s*(?:public|private|internal|protected)[^\n;]*\b{re.escape(name)}\s*\([^\n)]*\)\s*", source)
    if not match:
        raise SystemExit(f"Could not locate pinned method {name}.")
    return extract_block(source, match.start(), match.end())


def extract_block(source: str, start: int, header_end: int) -> str:
    brace = source.find("{", header_end)
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise SystemExit("Pinned source block did not terminate.")


def factory_set(source: str, name: str) -> tuple[bool, set[int]]:
    match = re.search(rf"\b{name}\s*=\s*Factory\.CreateBoolSet\((?P<body>[^;]+)\);", source)
    if not match:
        raise SystemExit(f"Pinned TileID set {name} is missing.")
    body = match.group("body")
    default_true = re.match(r"\s*true\s*,", body) is not None
    return default_true, {int(value) for value in re.findall(r"\b\d+\b", body)}


def runtime_set(source: str, name: str) -> set[int]:
    match = re.search(rf"\b{name}\s*=>\s*\[(?P<body>[^]]*)\]", source, re.DOTALL)
    if not match:
        raise SystemExit(f"Runtime smoothing catalog is missing {name}.")
    return {int(value) for value in re.findall(r"\b\d+\b", match.group("body"))}


def require(source: str, pattern: str, label: str) -> None:
    if re.search(pattern, source, re.DOTALL) is None:
        raise SystemExit(f"Smooth World contract is missing {label}: /{pattern}/")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    parser.add_argument("--runtime-catalog", required=True)
    parser.add_argument("--runtime-smoother", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    tile_id = Path(args.tile_id).read_text(encoding="utf-8")
    runtime_catalog = Path(args.runtime_catalog).read_text(encoding="utf-8")
    runtime_smoother = Path(args.runtime_smoother).read_text(encoding="utf-8")

    start = world_gen.find("AddGenerationPass(GenPassNameID.SmoothWorld")
    if start < 0:
        raise SystemExit("Pinned Smooth World generation pass is missing.")
    end = world_gen.find("AddGenerationPass(", start + 20)
    smooth_pass = world_gen[start:end]

    can_clear_default, cannot_clear = factory_set(tile_id, "CanBeClearedDuringGeneration")
    prevents_default, prevents = factory_set(tile_id, "PreventsSlopesDuringGeneration")
    _, sand = factory_set(tile_id, "Sand")
    _, boulders = factory_set(tile_id, "Boulders")
    _, tree_trunks = factory_set(tile_id, "IsATreeTrunk")
    if not can_clear_default or prevents_default:
        raise SystemExit("Pinned generation-set defaults changed.")
    source_sets = {
        "CannotClearDuringGeneration": cannot_clear,
        "PreventsGenerationSlopes": prevents,
        "SandConversionFamily": sand,
        "TreeTrunks": tree_trunks,
    }
    for name, expected in EXPECTED.items():
        actual = source_sets.get(name, expected)
        if actual != expected:
            raise SystemExit(f"Pinned {name} changed: expected {sorted(expected)}, got {sorted(actual)}")
        if runtime_set(runtime_catalog, name) != expected:
            raise SystemExit(f"Runtime {name} differs from pinned 1.4.5.8 facts.")

    can_pound = extract_method(world_gen, "CanPoundTile")
    pound_cases = {int(value) for value in re.findall(r"\bcase\s+(\d+)\s*:", can_pound)}
    cannot_pound = pound_cases | boulders | {30, 190}
    if runtime_set(runtime_catalog, "CannotBePounded") != cannot_pound:
        raise SystemExit("Runtime CannotBePounded differs from CanPoundTile + Boulders generation rules.")

    forbids = {int(value) for value in re.findall(r"\bcase\s+(\d+)\s*:", extract_method(world_gen, "ForbidsSloping"))}
    if forbids != EXPECTED["ForbidsSlopeBelow"]:
        raise SystemExit(f"Pinned ForbidsSloping changed: {sorted(forbids)}")

    can_kill_signature = "public static bool CanKillTile(int i, int j, out bool blockDamaged)"
    can_kill_start = world_gen.find(can_kill_signature)
    if can_kill_start < 0:
        raise SystemExit("Pinned detailed CanKillTile overload is missing.")
    can_kill = extract_block(world_gen, can_kill_start, can_kill_start + len(can_kill_signature))
    require(can_kill, r"TileID\.Sets\.IsATreeTrunk.*?frameX != 66.*?frameX != 88.*?frameY < 198", "tree support guard")
    require(can_kill, r"case 21:\s*case 26:\s*case 72:\s*case 77:\s*case 88:\s*case 467:\s*case 488:", "multi-tile support guard")

    source_markers = [
        (r"for \(int i = 20; i < Main\.maxTilesX - 20; i\+\+\)", "first 20-tile border scan"),
        (r"for \(int k = 20; k < Main\.maxTilesX - 20; k\+\+\)", "second 20-tile border scan"),
        (r"genRand\.Next\(5\).*?genRand\.Next\(5\)", "ordered erosion rolls"),
        (r"SlopeTile\(i, j, 3\).*?SlopeTile\(i, j, 4\)", "bottom slope directions"),
        (r"Tile\.SmoothSlope\(k, l, applyToNeighbors: false\)", "sand smoothing"),
        (r"slope\(\) == 1.*?PoundTile\(k, l\).*?slope\(\) == 2", "orphan-slope correction"),
    ]
    for pattern, label in source_markers:
        require(smooth_pass, pattern, label)

    runtime_markers = [
        "ApplyTopologyCell(grid, random, x, y",
        "ApplyFinishCell(grid, random, x, y",
        "ShapeErodedEdge(ref tile, random",
        "SmoothSandSlope(grid, x, y",
        "checked((byte)(sourceSlope + 1))",
    ]
    for marker in runtime_markers:
        if marker not in runtime_smoother:
            raise SystemExit(f"Runtime smoother is missing route: {marker}")

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "scope=ordinary Smooth World topology, shared-RNG order, slopes, half-bricks and sand normalization",
        f"WorldGen_SmoothWorld_sha256={hashlib.sha256(smooth_pass.encode('utf-8')).hexdigest()}",
        f"cannot_clear_count={len(cannot_clear)}",
        f"prevents_slopes_count={len(prevents)}",
        f"cannot_pound_count={len(cannot_pound)}",
        f"sand_family_count={len(sand)}",
        "status=verified",
    ]
    print("\n".join(lines))
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
