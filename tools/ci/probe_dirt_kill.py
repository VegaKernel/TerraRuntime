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
    match = re.search(
        rf"(?:public|private|internal) static [^{{;]]{{0,600}}\b{re.escape(name)}\([^)]*\)",
        source,
        re.DOTALL,
    )
    if match is None:
        # ILSpy output can contain generic/attribute noise which is easier to tolerate with a looser fallback.
        match = re.search(
            rf"(?:public|private|internal) static .*?\b{re.escape(name)}\([^)]*\)",
            source,
            re.DOTALL,
        )
    if match is None:
        raise SystemExit(f"Could not locate declaration-like WorldGen.{name} signature.")
    return compact(match.group(0))


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 WorldGen.KillTile for conservative Dirt authority."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--tile-id", required=True)
    args = parser.parse_args()

    raw = Path(args.world_gen).read_text(encoding="utf-8")
    compact_source = compact(raw)
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))
    dirt = re.search(r"\bDirt\s*=\s*(\d+)\s*;", tile_id)
    if dirt is None or dirt.group(1) != "0":
        raise SystemExit("Expected TileID.Dirt=0 in pinned source.")

    kill_signature = first_signature(raw, "KillTile")
    kill_method = compact(extract_method(raw, kill_signature))
    breakability_signature = first_signature(raw, "CheckTileBreakability")
    breakability_method = compact(extract_method(raw, breakability_signature))
    survive_signature = first_signature(raw, "CheckTileBreakability2_ShouldTileSurvive")
    survive_method = compact(extract_method(raw, survive_signature))

    print("tile_id_dirt=0")
    print(f"worldgen_kill_tile_signature={kill_signature}")
    print(f"worldgen_kill_tile_context={kill_method[:50000]}")
    print(f"worldgen_check_tile_breakability_signature={breakability_signature}")
    print(f"worldgen_check_tile_breakability_context={breakability_method[:50000]}")
    print(f"worldgen_check_tile_survive_signature={survive_signature}")
    print(f"worldgen_check_tile_survive_context={survive_method[:50000]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
