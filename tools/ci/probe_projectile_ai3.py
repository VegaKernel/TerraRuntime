#!/usr/bin/env python3
"""Extract the narrow TerrariaServer 1.4.5.8 contract needed for projectile aiStyle 3.

The probe intentionally emits only Enchanted Boomerang defaults, the AI_003 body, and the relevant
movement/Kill contexts. The official server binary remains the source of truth; this script does not
encode expected gameplay values.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return " ".join(text.split())


def extract_method(source: str, method_name: str) -> str:
    signature = re.compile(
        rf"(?m)^[ \t]*(?:public|private|protected|internal)\b[^\n;{{]*\b{re.escape(method_name)}\s*\([^\n)]*\)[^\n;{{]*$"
    )
    match = signature.search(source)
    if match is None:
        return "<none>"

    opening = source.find("{", match.end())
    if opening < 0 or source[match.end() : opening].strip():
        return "<none>"

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
    return "<none>"


def around_optional(source: str, needle: str, radius: int) -> str:
    normalized = compact(source)
    index = normalized.find(needle)
    if index < 0:
        return "<none>"
    start = max(0, index - radius)
    end = min(len(normalized), index + len(needle) + radius)
    return normalized[start:end]


def matching_lines(source: str, needle: str, limit: int = 80) -> str:
    lines = [compact(line) for line in source.splitlines() if needle in line]
    return " | ".join(lines[:limit]) if lines else "<none>"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--projectile", required=True, type=Path)
    args = parser.parse_args()

    source = args.projectile.read_text(encoding="utf-8")
    set_defaults = extract_method(source, "SetDefaults")
    ai003 = extract_method(source, "AI_003")
    handle_movement = extract_method(source, "HandleMovement")
    kill = extract_method(source, "Kill")

    print("projectile_type6_defaults=" + around_optional(set_defaults, "type == 6", radius=2600))
    print("projectile_ai003_declarations=" + matching_lines(source, "AI_003", limit=40))
    print("projectile_ai_style3_dispatch=" + matching_lines(source, "aiStyle == 3", limit=40))
    print("projectile_ai003_length=" + str(len(compact(ai003))))
    print("projectile_ai003=" + compact(ai003))
    print("projectile_type6_mentions=" + matching_lines(source, "type == 6", limit=80))
    print("projectile_handle_movement_ai3=" + around_optional(handle_movement, "aiStyle == 3", radius=3200))
    print("projectile_kill_type6=" + around_optional(kill, "type == 6", radius=3600))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
