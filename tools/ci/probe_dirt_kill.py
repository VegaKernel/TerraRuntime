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
        raise SystemExit("KillTile declaration has no body.")

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
    raise SystemExit("KillTile body did not terminate.")


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

    declarations = list(re.finditer(
        r"(?:public|private|internal) static [^{;]{0,400}\bKillTile\([^)]*\)",
        compact_source,
        re.DOTALL,
    ))
    if not declarations:
        raise SystemExit("Could not locate declaration-like WorldGen.KillTile signature.")

    print("tile_id_dirt=0")
    print(f"kill_tile_declarations={len(declarations)}")
    for index, match in enumerate(declarations[:12]):
        signature = match.group(0)
        print(f"worldgen_kill_tile_signature_{index}={signature}")
        if index == 0:
            method = compact(extract_method(raw, signature))
            print(f"worldgen_kill_tile_context={method[:50000]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
