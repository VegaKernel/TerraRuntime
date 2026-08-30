#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def require_window(text: str, anchor: str, tokens: tuple[str, ...], radius: int, label: str) -> None:
    positions = [match.start() for match in re.finditer(re.escape(anchor), text)]
    if not positions:
        raise SystemExit(f"{label}: anchor {anchor!r} was not found")

    for position in positions:
        start = max(0, position - radius)
        end = min(len(text), position + len(anchor) + radius)
        window = text[start:end]
        if all(token in window for token in tokens):
            return

    samples = []
    for position in positions[:3]:
        start = max(0, position - radius)
        end = min(len(text), position + len(anchor) + radius)
        samples.append(text[start:end])
    raise SystemExit(
        f"{label}: no {anchor!r} occurrence had required tokens {tokens}; "
        f"candidate windows={samples}"
    )


def require_window_with_any(
    text: str,
    anchor: str,
    tokens: tuple[str, ...],
    alternatives: tuple[str, ...],
    radius: int,
    label: str,
) -> None:
    positions = [match.start() for match in re.finditer(re.escape(anchor), text)]
    if not positions:
        raise SystemExit(f"{label}: anchor {anchor!r} was not found")

    for position in positions:
        start = max(0, position - radius)
        end = min(len(text), position + len(anchor) + radius)
        window = text[start:end]
        if all(token in window for token in tokens) and any(token in window for token in alternatives):
            return

    raise SystemExit(
        f"{label}: no {anchor!r} occurrence had required tokens {tokens} "
        f"and one of {alternatives}"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--scene-metrics", required=True)
    parser.add_argument("--world-gen", required=True)
    args = parser.parse_args()

    scene = compact(Path(args.scene_metrics).read_text(encoding="utf-8"))
    worldgen = compact(Path(args.world_gen).read_text(encoding="utf-8"))

    require_window(
        scene,
        "SnowTileThreshold",
        ("Skyblock.lowTiles", "300", "1500"),
        5000,
        "SnowTileThreshold",
    )
    require_window(
        scene,
        "DesertTileThreshold",
        ("Skyblock.lowTiles", "300", "1500"),
        5000,
        "DesertTileThreshold",
    )

    require_window_with_any(
        worldgen,
        "lowTiles",
        ("skyblockWorld",),
        ("0.1", "10.0", "/ 10", "* 10", "/10", "*10"),
        1800,
        "WorldGen.Skyblock.lowTiles",
    )
    require_window(
        worldgen,
        "GERunner",
        ("Skyblock.lowTiles",),
        3500,
        "WorldGen.GERunner",
    )

    print("TerrariaServer 1.4.5.8 Skyblock lowTiles runtime contract verified.")


if __name__ == "__main__":
    main()
