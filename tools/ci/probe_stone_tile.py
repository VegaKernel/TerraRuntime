#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


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


def signatures(source: str, name: str, require_static: bool = True) -> list[str]:
    static = r" static" if require_static else r"(?: static)?"
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal){static} [^\r\n{{;]*\b{re.escape(name)}\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def named_id(source: str, name: str) -> int:
    match = re.search(rf"\b{re.escape(name)}\s*=\s*(\d+)\s*;", source)
    if match is None:
        raise SystemExit(f"Could not locate pinned {name} constant.")
    return int(match.group(1))


def context(text: str, marker: str, before: int = 900, after: int = 1800) -> str:
    index = text.find(marker)
    if index < 0:
        return f"<missing {marker}>"
    return text[max(0, index - before):min(len(text), index + len(marker) + after)]


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 Stone placement/drop/frame semantics."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--item", required=True)
    parser.add_argument("--item-id", required=True)
    parser.add_argument("--tile-id", required=True)
    parser.add_argument("--main", required=True)
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    item = Path(args.item).read_text(encoding="utf-8")
    item_id = compact(Path(args.item_id).read_text(encoding="utf-8"))
    tile_id = compact(Path(args.tile_id).read_text(encoding="utf-8"))
    main_source = compact(Path(args.main).read_text(encoding="utf-8"))

    stone_item = named_id(item_id, "StoneBlock")
    stone_tile = named_id(tile_id, "Stone")
    print(f"stone_item_id={stone_item}")
    print(f"stone_tile_id={stone_tile}")

    defaults_sig = next(iter(signatures(item, "SetDefaults1", require_static=False)), None)
    if defaults_sig is None:
        raise SystemExit("Could not locate Item.SetDefaults1.")
    defaults = compact(extract_method(item, defaults_sig))
    print(f"stone_item_defaults_context={context(defaults, f'case {stone_item}:')}")

    place_sig = next((s for s in signatures(world_gen, "PlaceTile") if "int Type" in s), None)
    if place_sig is None:
        raise SystemExit("Could not locate WorldGen.PlaceTile.")
    place = compact(extract_method(world_gen, place_sig))
    print(f"stone_place_num_equals_context={context(place, f'num == {stone_tile}', 500, 1200)}")
    print(f"stone_place_case_context={context(place, f'case {stone_tile}:', 500, 1200)}")

    drop_sig = next(
        (s for s in signatures(world_gen, "KillTile_GetItemDrops") if "out int dropItem" in s),
        None,
    )
    if drop_sig is None:
        raise SystemExit("Could not locate WorldGen.KillTile_GetItemDrops.")
    drop = compact(extract_method(world_gen, drop_sig))
    print(f"stone_drop_tile_case_context={context(drop, f'case {stone_tile}:', 1200, 2400)}")
    print(f"stone_drop_item_assignment_context={context(drop, f'dropItem = {stone_item};', 1200, 2400)}")

    numeric_writes = re.findall(r"tileFrameImportant\[(\d+)\]\s*=\s*(true|false)", main_source)
    stone_writes = [(index, value) for index, value in numeric_writes if int(index) == stone_tile]
    print(f"stone_frame_important_direct_writes={stone_writes}")

    dynamic_writes = re.findall(
        r"tileFrameImportant\[(?!\d+\])([^\]]+)\]\s*=\s*(true|false)",
        main_source,
    )
    print(f"tile_frame_important_dynamic_writers={dynamic_writes}")
    print("stone_probe=diagnostic")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
