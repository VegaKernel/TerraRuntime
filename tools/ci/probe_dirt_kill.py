#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


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


def first_signature(source: str, name: str) -> str:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal) static [^\r\n{{;]*\b{re.escape(name)}\([^\r\n)]*\)",
        re.MULTILINE,
    )
    match = pattern.search(source)
    if match is None:
        raise SystemExit(f"Could not locate declaration-like WorldGen.{name} signature.")
    return match.group(0).strip()


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"Pinned source contract changed: missing {label}: {needle}")


def print_context(text: str, marker: str) -> None:
    index = text.find(marker)
    if index < 0:
        print(f"diagnostic_marker_missing={marker}")
        return
    start = max(0, index - 700)
    end = min(len(text), index + len(marker) + 1400)
    print(f"diagnostic_context[{marker}]={text[start:end]}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify pinned TerrariaServer 1.4.5.8 WorldGen.KillTile for conservative Dirt authority."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    args = parser.parse_args()

    raw = Path(args.world_gen).read_text(encoding="utf-8")
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))
    dirt = re.search(r"\bDirt\s*=\s*(\d+)\s*;", tile_id)
    if dirt is None or dirt.group(1) != "0":
        raise SystemExit("Expected TileID.Dirt=0 in pinned source.")

    kill_signature = first_signature(raw, "KillTile")
    kill_method = compact(extract_method(raw, kill_signature))
    drop_signature = first_signature(raw, "KillTile_DropItems")
    drop_method = compact(extract_method(raw, drop_signature))
    get_drops_signature = first_signature(raw, "KillTile_GetItemDrops")
    get_drops_method = compact(extract_method(raw, get_drops_signature))
    breakability_signature = first_signature(raw, "CheckTileBreakability")
    breakability_method = compact(extract_method(raw, breakability_signature))
    survive_signature = first_signature(raw, "CheckTileBreakability2_ShouldTileSurvive")
    survive_method = compact(extract_method(raw, survive_signature))

    if kill_signature != "public static void KillTile(int i, int j, bool fail = false, bool effectOnly = false, bool noItem = false)":
        raise SystemExit(f"Unexpected KillTile signature: {kill_signature}")

    require(
        kill_method,
        "int num = CheckTileBreakability(i, j); if (num == 1) { fail = true; } if (num == 2) { return; }",
        "KillTile breakability dispatch",
    )
    require(
        kill_method,
        "if (!noItem && !stopDrops && Main.netMode != 1) { KillTile_DropBait(i, j, tile); KillTile_DropItems(i, j, tile); }",
        "KillTile no-item drop gate",
    )
    require(
        kill_method,
        "tile.active(active: false); tile.halfBrick(halfBrick: false); tile.frameX = -1; tile.frameY = -1; tile.ClearBlockPaintAndCoating(); tile.frameNumber(0);",
        "KillTile ordinary mutation prefix",
    )
    require(
        kill_method,
        "tile.type = 0; tile.inActive(inActive: false); SquareTileFrame(i, j); CheckExploitDestroyQueue();",
        "KillTile ordinary mutation tail",
    )

    require(
        breakability_method,
        "if (tile3 != null && tile3.active() && IsLockedDoor(tile3)) { return 2; }",
        "locked-door below guard",
    )
    require(breakability_method, "if (tile2.active()) {", "active-above guard")
    require(breakability_method, "if (tile.type == 235) {", "special tile 235 branch")
    if not breakability_method.endswith("return 0; }"):
        raise SystemExit("CheckTileBreakability no longer ends in ordinary return 0.")

    require(survive_method, "if (TileID.Sets.BasicChest[tile.type]) {", "basic chest survivor branch")
    require(survive_method, "if (tile.type == 88) {", "dresser survivor branch")
    require(survive_method, "if (tile.type == 470) {", "display doll survivor branch")
    require(survive_method, "if (tile.type == 475) {", "hat rack survivor branch")
    if "tile.type == 0" in survive_method or "Dirt" in survive_method:
        raise SystemExit("Dirt unexpectedly entered CheckTileBreakability2 survivor branches.")
    if not survive_method.endswith("return false; }"):
        raise SystemExit("CheckTileBreakability2_ShouldTileSurvive no longer ends in false.")

    print(f"drop_signature={drop_signature}")
    print(f"drop_method_prefix={drop_method[:6000]}")
    print(f"get_drops_signature={get_drops_signature}")
    print(f"get_drops_method_prefix={get_drops_method[:16000]}")
    for marker in ("TileID.Dirt", "DirtBlock", "tileCache.type == 0", "case 0:"):
        print_context(get_drops_method, marker)

    print("tile_id_dirt=0")
    print("dirt_kill_no_item_drop_gate=verified")
    print("dirt_kill_mutation_tail=verified")
    print("dirt_kill_breakability_isolated_neighborhood=verified")
    print("dirt_kill_survivor_guard=false")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
