#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def signatures(source: str, name: str) -> list[str]:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal)(?: static)? [^\r\n{{;]*\b{re.escape(name)}\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate exact signature: {signature}")

    brace = source.find("{", start + len(signature))
    if brace < 0:
        raise SystemExit(f"Method declaration has no body: {signature}")

    depth = 0
    in_string = False
    in_char = False
    escaped = False
    for index in range(brace, len(source)):
        ch = source[index]
        if escaped:
            escaped = False
        elif ch == "\\" and (in_string or in_char):
            escaped = True
        elif ch == '"' and not in_char:
            in_string = not in_string
        elif ch == "'" and not in_string:
            in_char = not in_char
        elif not in_string and not in_char:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return source[start:index + 1]

    raise SystemExit(f"Method body did not terminate: {signature}")


def dump_required_method(source: str, name: str, prefix: str) -> None:
    matches = signatures(source, name)
    if not matches:
        raise SystemExit(f"Could not locate {name} in pinned source.")

    print(f"{prefix}_signature_count={len(matches)}")
    for index, signature in enumerate(matches):
        print(f"{prefix}_signature_{index}={signature}")
        print(f"{prefix}_method_{index}={compact(extract_method(source, signature))}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 player tile-mining authority logic."
    )
    parser.add_argument("--player", required=True)
    parser.add_argument("--world-gen")
    args = parser.parse_args()

    player = Path(args.player).read_text(encoding="utf-8")

    dump_required_method(player, "PickTile", "pick_tile")
    dump_required_method(player, "PickTile_DetermineDamage", "pick_tile_determine")
    dump_required_method(player, "GetPickaxeDamage", "get_pickaxe_damage")
    dump_required_method(player, "DoesPickTargetTransformOnKill", "pick_target_transform")

    if args.world_gen:
        world_gen = Path(args.world_gen).read_text(encoding="utf-8")
        dump_required_method(world_gen, "CanKillTile", "worldgen_can_kill_tile")

    print("tile_mining_probe=diagnostic")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
