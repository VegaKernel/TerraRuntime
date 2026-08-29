#!/usr/bin/env python3
"""Extract a narrow, reviewable projectile tile-cut contract from ILSpy C# output.

The script deliberately does not persist or print the complete decompiled Terraria types. It emits only the
small methods and call-site context needed to validate TerraRuntime's source-backed projectile side effects.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def extract_method(source: str, method_name: str) -> str:
    signature = re.compile(
        rf"(?m)^[ \t]*(?:public|private|protected|internal)[^\n{{;]*\b{re.escape(method_name)}\s*\([^\n)]*\)[^\n{{;]*\{{"
    )
    match = signature.search(source)
    if match is None:
        candidates = [" ".join(line.split()) for line in source.splitlines() if method_name in line][:20]
        detail = " | ".join(candidates) if candidates else "<none>"
        raise SystemExit(f"method not found: {method_name}; candidates: {detail}")

    opening = source.find("{", match.start())
    depth = 0
    in_string = False
    in_char = False
    escaped = False

    for index in range(opening, len(source)):
        char = source[index]
        if escaped:
            escaped = False
            continue

        if char == "\\" and (in_string or in_char):
            escaped = True
            continue

        if char == '"' and not in_char:
            in_string = not in_string
            continue

        if char == "'" and not in_string:
            in_char = not in_char
            continue

        if in_string or in_char:
            continue

        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[match.start() : index + 1]

    raise SystemExit(f"unterminated method: {method_name}")


def compact(text: str) -> str:
    return " ".join(text.split())


def around_first(source: str, needle: str, radius: int = 360) -> str:
    normalized = compact(source)
    index = normalized.find(needle)
    if index < 0:
        raise SystemExit(f"call-site token not found: {needle}")
    start = max(0, index - radius)
    end = min(len(normalized), index + len(needle) + radius)
    return normalized[start:end]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--projectile", required=True, type=Path)
    parser.add_argument("--delegate-methods", required=True, type=Path)
    args = parser.parse_args()

    projectile_source = args.projectile.read_text(encoding="utf-8")
    delegate_source = args.delegate_methods.read_text(encoding="utf-8")

    can_cut_tiles = compact(extract_method(projectile_source, "CanCutTiles"))
    cut_tiles = compact(extract_method(projectile_source, "CutTiles"))
    delegate_cut_tiles = compact(extract_method(delegate_source, "CutTiles"))

    print("projectile_can_cut_tiles=" + can_cut_tiles)
    print("projectile_cut_tiles=" + cut_tiles)
    print("projectile_cut_tiles_callsite=" + around_first(projectile_source, "CutTiles();"))
    print("delegate_methods_cut_tiles=" + delegate_cut_tiles)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
