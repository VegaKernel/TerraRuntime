#!/usr/bin/env python3
import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


def find_key(value: Any, key: str, path: str = "$", found: list[tuple[str, Any]] | None = None) -> list[tuple[str, Any]]:
    if found is None:
        found = []
    if isinstance(value, dict):
        for child_key, child_value in value.items():
            child_path = f"{path}.{child_key}"
            if child_key == key:
                found.append((child_path, child_value))
            find_key(child_value, key, child_path, found)
    elif isinstance(value, list):
        for index, child_value in enumerate(value):
            find_key(child_value, key, f"{path}[{index}]", found)
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 embedded worldgen configuration.")
    parser.add_argument("--configuration", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    path = Path(args.configuration)
    raw = path.read_bytes()
    data = json.loads(raw.decode("utf-8-sig"))
    matches = find_key(data, "FlatBeachPadding")
    if len(matches) != 1:
        raise SystemExit(f"Expected exactly one FlatBeachPadding entry; found {len(matches)}: {matches}")

    key_path, value = matches[0]
    if type(value) is not int:
        raise SystemExit(f"FlatBeachPadding must be an integer in pinned config; got {type(value).__name__}.")

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "resource=Terraria.GameContent.WorldBuilding.Configuration.json",
        f"WorldGenConfiguration_sha256={hashlib.sha256(raw).hexdigest()}",
        f"FlatBeachPadding_path={key_path}",
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
