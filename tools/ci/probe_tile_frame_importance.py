#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect TerrariaServer 1.4.5.8 tileFrameImportant initialization around Dirt tile 0."
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
    if allocation is None:
        raise SystemExit("Could not locate Main.tileFrameImportant bool-array allocation.")

    bracket_uses = re.findall(r"tileFrameImportant\[([^\]]+)\]", main_source)
    numeric_indices = [int(value) for value in bracket_uses if value.isdigit()]
    dynamic_indices = sorted({value for value in bracket_uses if not value.isdigit()})
    writes = re.findall(r"tileFrameImportant\[(\d+)\]\s*=\s*(true|false)", main_source)
    dirt_writes = [(index, value) for index, value in writes if index == "0"]

    start = max(0, allocation.start() - 1800)
    end = min(len(main_source), allocation.end() + 3000)

    print("tile_id_dirt=0")
    print(f"tile_frame_important_allocation={allocation.group(0)}")
    print(f"tile_frame_important_allocation_context={main_source[start:end]}")
    print(f"tile_frame_important_bracket_uses={len(bracket_uses)}")
    print(f"tile_frame_important_numeric_indices={len(numeric_indices)}")
    print(f"tile_frame_important_direct_writes={len(writes)}")
    print(f"tile_frame_important_dirt_writes={dirt_writes}")
    print(f"tile_frame_important_dynamic_indices={dynamic_indices}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
