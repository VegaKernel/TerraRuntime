#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 embedded worldgen configuration.")
    parser.add_argument("--configuration", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    path = Path(args.configuration)
    raw = path.read_bytes()
    text = raw.decode("utf-8-sig")

    # Terraria's embedded worldgen configuration is JSON-like source accepted by its own configuration loader, but
    # it is not guaranteed to be strict RFC JSON. Extract the one scalar TerrainPass consumes directly and require
    # it to occur exactly once rather than normalizing/re-serializing the resource through Python's JSON parser.
    pattern = re.compile(r'(?m)^\s*["\']?FlatBeachPadding["\']?\s*:\s*(-?\d+)\s*,?\s*$')
    matches = list(pattern.finditer(text))
    if len(matches) != 1:
        context = [line.strip() for line in text.splitlines() if "FlatBeachPadding" in line]
        raise SystemExit(f"Expected exactly one FlatBeachPadding scalar; found {len(matches)}: {context}")

    value = int(matches[0].group(1))
    lines = [
        "source=TerrariaServer 1.4.5.8",
        "resource=Terraria.GameContent.WorldBuilding.Configuration.json",
        f"WorldGenConfiguration_sha256={hashlib.sha256(raw).hexdigest()}",
        f"FlatBeachPadding_value={value}",
    ]
    for line in lines:
        print(line)

    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
