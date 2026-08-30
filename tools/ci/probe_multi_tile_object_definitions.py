#!/usr/bin/env python3
"""Verify supported multi-tile object geometry against pinned TerrariaServer 1.4.5.8 TileObjectData."""

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def require_sequence(source: str, label: str, tokens: tuple[str, ...], maximum_span: int = 5000) -> None:
    start = source.find(tokens[0])
    while start >= 0:
        position = start + len(tokens[0])
        matched = True
        for token in tokens[1:]:
            found = source.find(token, position, start + maximum_span)
            if found < 0:
                matched = False
                break
            position = found + len(token)
        if matched:
            return
        start = source.find(tokens[0], start + 1)
    raise SystemExit(f"Pinned TileObjectData sequence changed: {label}.")


def base_style(width: int, height: int, origin_x: int, origin_y: int, name: str) -> tuple[str, ...]:
    return (
        f"newTile.Width = {width};",
        f"newTile.Height = {height};",
        f"newTile.Origin = new Point16({origin_x}, {origin_y});",
        f"addBaseTile(out {name});",
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify TerrariaServer 1.4.5.8 multi-tile definitions used by section metadata."
    )
    parser.add_argument("--tile-object-data", required=True)
    args = parser.parse_args()
    source = compact(Path(args.tile_object_data).read_text(encoding="utf-8"))

    for values in (
        (1, 1, 0, 0, "StyleOnTable1x1"),
        (1, 2, 0, 0, "Style1x2Top"),
        (2, 3, 1, 2, "Style2xX"),
        (3, 2, 1, 1, "Style3x2"),
        (3, 4, 1, 3, "Style3x4"),
        (2, 2, 0, 1, "Style2x2"),
        (3, 3, 1, 1, "Style3x3Wall"),
    ):
        require_sequence(source, values[4], base_style(*values), maximum_span=1200)

    associations = (
        ("food platter", ("newTile.CopyFrom(StyleOnTable1x1);", "TEFoodPlatter.Hook_AfterPlacement", "addTile(520);")),
        ("display jar", ("newTile.CopyFrom(Style1x2Top);", "TEDeadCellsDisplayJar.Hook_AfterPlacement", "addTile(698);")),
        ("training dummy", ("newTile.CopyFrom(Style2xX);", "TETrainingDummy.Hook_AfterPlacement", "addTile(378);")),
        ("display doll", ("newTile.CopyFrom(Style2xX);", "newTile.Origin = new Point16(0, 2);", "TEDisplayDoll.Hook_AfterPlacement", "addTile(470);")),
        ("dresser", ("newTile.CopyFrom(Style3x2);", "Chest.AfterPlacement_Hook", "addTile(88);")),
        ("hat rack", ("newTile.CopyFrom(Style3x4);", "TEHatRack.Hook_AfterPlacement", "addTile(475);")),
        ("pylon", ("newTile.CopyFrom(Style3x4);", "TETeleportationPylon.PlacementPreviewHook_AfterPlacement", "addTile(597);")),
        ("chest", ("newTile.CopyFrom(Style2x2);", "Chest.AfterPlacement_Hook", "addTile(21);")),
        ("chest2", ("newTile.CopyFrom(Style2x2);", "Chest.AfterPlacement_Hook", "addTile(467);")),
        ("item frame", ("newTile.CopyFrom(Style2x2);", "TEItemFrame.Hook_AfterPlacement", "addTile(395);")),
        ("weapons rack", ("newTile.CopyFrom(Style3x3Wall);", "TEWeaponsRack.Hook_AfterPlacement", "addTile(471);")),
    )
    for label, tokens in associations:
        require_sequence(source, label, tokens)

    for tile_id in (55, 85, 425, 573):
        require_sequence(
            source,
            f"sign tile {tile_id}",
            ("newTile.CopyFrom(Style2x2);", f"addTile({tile_id});"),
            maximum_span=1800,
        )

    print("multi_tile_base_styles=7")
    print("multi_tile_supported_definitions=15")
    print("multi_tile_source_contract=ok")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
