#!/usr/bin/env python3
"""Extract the narrow TerrariaServer 1.4.5.8 contract needed for projectile aiStyle 3.

The official server binary remains the source of truth. Keep the emitted contexts deliberately small
so CI logs expose the behavioral contract without persisting the decompiled type in the repository.
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
    boomerang = extract_method(source, "AI_003_Boomerang")
    handle_movement = extract_method(source, "HandleMovement")
    can_cut_tiles = extract_method(source, "CanCutTiles")
    collision_params = extract_method(source, "GetCollisionParams")
    update = extract_method(source, "Update")

    print("projectile_type6_defaults=" + around_optional(set_defaults, "type == 6", radius=850))
    print("projectile_boomerang_outbound=" + all_contexts(boomerang, "if (ai[0] == 0f)", radius=1700, limit=2))
    print("projectile_boomerang_return_entry=" + all_contexts(boomerang, "tileCollide = false", radius=1200, limit=4))
    print("projectile_boomerang_owner_speed=" + all_contexts(boomerang, "meleeSpeed", radius=700, limit=4))
    print("projectile_boomerang_distance_kill=" + all_contexts(boomerang, "3000f", radius=850, limit=6))
    print("projectile_handle_movement_ai3=" + all_contexts(handle_movement, "aiStyle == 3 || aiStyle == 13", radius=1800, limit=4))
    print("projectile_can_cut_tiles_length=" + str(len(compact(can_cut_tiles))))
    print("projectile_can_cut_tiles_type6=" + all_contexts(can_cut_tiles, "type == 6", radius=900, limit=6))
    print("projectile_can_cut_tiles_tail=" + compact(can_cut_tiles)[-2200:])
    print("projectile_collision_params_length=" + str(len(compact(collision_params))))
    print("projectile_collision_params_type6=" + all_contexts(collision_params, "type == 6", radius=900, limit=6))
    print("projectile_collision_params_head=" + compact(collision_params)[:2600])
    print("projectile_update_handle_movement=" + all_contexts(update, "HandleMovement", radius=1800, limit=6))
    print("projectile_update_position=" + all_contexts(update, "position +=", radius=1200, limit=8))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
