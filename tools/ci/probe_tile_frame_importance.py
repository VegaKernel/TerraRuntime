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

    matches = list(re.finditer(r"tileFrameImportant", main_source))
    if not matches:
        raise SystemExit("Could not locate Main.tileFrameImportant in pinned source.")

    print("tile_id_dirt=0")
    print(f"tile_frame_important_occurrences={len(matches)}")
    for index, match in enumerate(matches[:40]):
        start = max(0, match.start() - 1400)
        end = min(len(main_source), match.end() + 2600)
        print(f"tile_frame_important_context_{index}={main_source[start:end]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
