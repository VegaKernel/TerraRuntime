#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify TerrariaServer 1.4.5.8 Dirt tile frame-importance contract."
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
    numeric_indices = [int(value) for value in bracket_uses if value.isdigit()]
    dynamic_indices = sorted({value for value in bracket_uses if not value.isdigit()})
    numeric_writes = re.findall(r"tileFrameImportant\[(\d+)\]\s*=\s*(true|false)", main_source)
    all_write_indices = re.findall(r"tileFrameImportant\[([^\]]+)\]\s*=\s*(?:true|false)", main_source)
    dynamic_writes = sorted({value for value in all_write_indices if not value.isdigit()})
    dirt_writes = [(index, value) for index, value in numeric_writes if index == "0"]

    if dirt_writes:
        raise SystemExit(f"Pinned source writes tileFrameImportant[0]: {dirt_writes}.")
    if dynamic_writes:
        raise SystemExit(
            "Pinned source has dynamic tileFrameImportant writes, so Dirt=false cannot be proven by zero-init: "
            + repr(dynamic_writes)
        )

    print("tile_id_dirt=0")
    print(f"tile_frame_important_allocation={allocation.group(0)}")
    print(f"tile_frame_important_bracket_uses={len(bracket_uses)}")
    print(f"tile_frame_important_numeric_indices={len(numeric_indices)}")
    print(f"tile_frame_important_direct_writes={len(numeric_writes)}")
    print("tile_frame_important_dirt_writes=none")
    print(f"tile_frame_important_dynamic_reads={dynamic_indices}")
    print("tile_frame_important_dynamic_writes=none")
    print("tile_frame_important_dirt=false")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
