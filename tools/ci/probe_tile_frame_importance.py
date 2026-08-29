#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


ECHO_FURNITURE_TILE_IDS = [647, 648, 706, 650, 649, 652, 651, 693, 694]


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

    numeric_writes = re.findall(r"tileFrameImportant\[(\d+)\]\s*=\s*(true|false)", main_source)
    dirt_writes = [(index, value) for index, value in numeric_writes if index == "0"]
    if dirt_writes:
        raise SystemExit(f"Pinned source writes tileFrameImportant[0]: {dirt_writes}.")

    dynamic_writes = re.findall(
        r"tileFrameImportant\[(?!\d+\])([^\]]+)\]\s*=\s*(true|false)",
        main_source,
    )
    if sorted(dynamic_writes) != sorted([("tileId", "true"), ("num2", "true")]):
        raise SystemExit(f"Unexpected dynamic tileFrameImportant writers: {dynamic_writes}.")

    echo_helper = compact(
        """
        private static void AddEchoFurnitureTile(int tileId) {
            tileFrameImportant[tileId] = true;
            tileNoFail[tileId] = true;
            tileObsidianKill[tileId] = true;
        }
        """
    )
    if echo_helper not in main_source:
        raise SystemExit("AddEchoFurnitureTile frame-importance writer changed.")

    echo_calls = [int(value) for value in re.findall(r"AddEchoFurnitureTile\((\d+)\);", main_source)]
    if echo_calls != ECHO_FURNITURE_TILE_IDS:
        raise SystemExit(f"Unexpected AddEchoFurnitureTile ids: {echo_calls}.")
    if 0 in echo_calls:
        raise SystemExit("Dirt unexpectedly entered AddEchoFurnitureTile initialization.")

    team_platform_loop = re.search(
        r"for \(int num2 = 435; num2 <= 439; num2\+\+\) \{ tileFrameImportant\[num2\] = true;",
        main_source,
    )
    if team_platform_loop is None:
        raise SystemExit("Expected the only num2 frame-importance writer to remain bounded to 435..439.")

    print("tile_id_dirt=0")
    print(f"tile_frame_important_allocation={allocation.group(0)}")
    print(f"tile_frame_important_direct_numeric_writes={len(numeric_writes)}")
    print("tile_frame_important_dirt_writes=none")
    print(f"tile_frame_important_echo_ids={echo_calls}")
    print("tile_frame_important_team_platform_range=435..439")
    print("tile_frame_important_dirt=false")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
