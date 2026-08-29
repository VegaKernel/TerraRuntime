#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect TerrariaServer 1.4.5.8 Dirt tile frame-importance writers."
    )
    parser.add_argument("--main", required=True)
    parser.add_argument("--tile-id", required=True)
    args = parser.parse_args()

    main_source = compact(Path(args.main).read_text(encoding="utf-8"))
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))

    dirt = re.search(r"\bDirt\s*=\s*(\d+)\s*;", tile_id)
    if dirt is None or dirt.group(1) != "0":
        raise SystemExit("Expected pinned TileID.Dirt = 0.")

    allocation = re.search(r"tileFrameImportant\s*=\s*new bool\[([^\]]+)\]", main_source)
    if allocation is None or allocation.group(1) != "TileID.Count":
        raise SystemExit("Expected Main.tileFrameImportant to be a zero-initialized bool[TileID.Count].")

    bracket_uses = re.findall(r"tileFrameImportant\[([^\]]+)\]", main_source)
    numeric_writes = re.findall(r"tileFrameImportant\[(\d+)\]\s*=\s*(true|false)", main_source)
    dynamic_write_matches = list(re.finditer(
        r"tileFrameImportant\[(?!\d+\])([^\]]+)\]\s*=\s*(true|false)",
        main_source,
    ))
    dirt_writes = [(index, value) for index, value in numeric_writes if index == "0"]

    if dirt_writes:
        raise SystemExit(f"Pinned source writes tileFrameImportant[0]: {dirt_writes}.")

    print("tile_id_dirt=0")
    print(f"tile_frame_important_allocation={allocation.group(0)}")
    print(f"tile_frame_important_bracket_uses={len(bracket_uses)}")
    print(f"tile_frame_important_direct_numeric_writes={len(numeric_writes)}")
    print("tile_frame_important_dirt_writes=none")
    print(f"tile_frame_important_dynamic_writes={len(dynamic_write_matches)}")
    for index, match in enumerate(dynamic_write_matches):
        start = max(0, match.start() - 2400)
        end = min(len(main_source), match.end() + 3200)
        print(f"tile_frame_important_dynamic_write_{index}={match.group(0)}")
        print(f"tile_frame_important_dynamic_write_context_{index}={main_source[start:end]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
