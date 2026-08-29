#!/usr/bin/env python3
"""Extract the narrow TerrariaServer 1.4.5.8 contract needed for projectile aiStyle 3.

The official server binary remains the source of truth. Keep the emitted contexts deliberately small
so CI logs expose the behavioral contract without persisting or dumping the decompiled type.
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


def around_optional(source: str, needle: str, radius: int = 360) -> str:
    normalized = compact(source)
    index = normalized.find(needle)
    if index < 0:
        return "<none>"
    start = max(0, index - radius)
    end = min(len(normalized), index + len(needle) + radius)
    return normalized[start:end]


def all_contexts(source: str, needle: str, radius: int = 240, limit: int = 12) -> str:
    normalized = compact(source)
    contexts: list[str] = []
    offset = 0
    while len(contexts) < limit:
        index = normalized.find(needle, offset)
        if index < 0:
            break
        start = max(0, index - radius)
        end = min(len(normalized), index + len(needle) + radius)
        contexts.append(normalized[start:end])
        offset = index + len(needle)
    return " || ".join(contexts) if contexts else "<none>"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--projectile", required=True, type=Path)
    args = parser.parse_args()

    source = args.projectile.read_text(encoding="utf-8")
    set_defaults = extract_method(source, "SetDefaults")
    ai003 = extract_method(source, "AI_003")
    handle_movement = extract_method(source, "HandleMovement")
    kill = extract_method(source, "Kill")

    print("projectile_type6_defaults=" + around_optional(set_defaults, "type == 6", radius=850))
    print("projectile_ai003_length=" + str(len(compact(ai003))))
    print("projectile_ai003_type6=" + all_contexts(ai003, "type == 6", radius=320, limit=8))
    print("projectile_ai003_ai0=" + all_contexts(ai003, "ai[0]", radius=300, limit=12))
    print("projectile_ai003_owner=" + all_contexts(ai003, "owner", radius=300, limit=12))
    print("projectile_ai003_tile_collide=" + all_contexts(ai003, "tileCollide", radius=300, limit=8))
    print("projectile_ai003_velocity=" + all_contexts(ai003, "velocity", radius=260, limit=16))
    print("projectile_ai003_kill=" + all_contexts(ai003, "Kill", radius=300, limit=8))
    print("projectile_handle_movement_ai3=" + around_optional(handle_movement, "aiStyle == 3", radius=850))
    print("projectile_kill_type6=" + around_optional(kill, "type == 6", radius=850))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
