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


def signatures(source: str, name: str) -> list[str]:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal) static [^\r\n{{;]*\b{re.escape(name)}\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"Pinned source contract changed: missing {label}: {needle}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 Dirt item-drop creation semantics."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--item", required=True)
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    item = Path(args.item).read_text(encoding="utf-8")

    drop_sig = next(
        (s for s in signatures(world_gen, "KillTile_GetItemDrops") if "out int dropItem" in s),
        None,
    )
    if drop_sig is None:
        raise SystemExit("Could not locate WorldGen.KillTile_GetItemDrops.")
    drop = compact(extract_method(world_gen, drop_sig))
    require(
        drop,
        "case 0: case 2: case 109: case 199: case 477: case 492: dropItem = 2; break;",
        "Dirt tile-to-item mapping",
    )
    require(drop, "dropItemStack = 1;", "default primary stack")
    require(drop, "noPrefix = false;", "default prefix policy")

    drop_items_sig = next(iter(signatures(world_gen, "KillTile_DropItems")), None)
    if drop_items_sig is None:
        raise SystemExit("Could not locate WorldGen.KillTile_DropItems.")
    drop_items = compact(extract_method(world_gen, drop_items_sig))
    require(
        drop_items,
        "Item.NewItem(GetItemSource_FromTileBreak(x, y), x * 16, y * 16, 16, 16, dropItem, dropItemStack, noBroadcast: false, noPrefix ? (-4) : (-1));",
        "primary tile-break Item.NewItem call",
    )

    new_item_signatures = signatures(item, "NewItem")
    print("new_item_signatures_begin")
    for signature in new_item_signatures:
        print(signature)
    print("new_item_signatures_end")

    candidate = next(
        (
            signature
            for signature in new_item_signatures
            if "int X" in signature
            and "int Y" in signature
            and "int Width" in signature
            and "int Height" in signature
            and "int Type" in signature
        ),
        None,
    )
    if candidate is None:
        raise SystemExit("Could not locate rectangle-based Item.NewItem overload.")

    body = compact(extract_method(item, candidate))
    print(f"new_item_candidate={candidate}")
    print(f"new_item_body_prefix={body[:18000]}")
    print("dirt_drop_item=2")
    print("dirt_drop_stack=1")
    print("dirt_drop_rectangle=x*16,y*16,16,16")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
