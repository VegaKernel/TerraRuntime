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


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"Pinned source contract changed: missing {label}: {needle}")


def print_context(text: str, marker: str, radius: int = 3500) -> None:
    index = text.find(marker)
    if index < 0:
        print(f"diagnostic_marker_missing={marker}")
        return
    print(f"diagnostic_context[{marker}]={text[max(0, index-radius):min(len(text), index+len(marker)+radius)]}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 Dirt item-drop creation semantics."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--item", required=True)
    parser.add_argument("--item-id", required=True)
    parser.add_argument("--prefix-legacy", required=True)
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    item = Path(args.item).read_text(encoding="utf-8")
    item_id = compact(Path(args.item_id).read_text(encoding="utf-8"))
    prefix_legacy = compact(Path(args.prefix_legacy).read_text(encoding="utf-8"))

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
    expected_rectangle = (
        "public static int NewItem(IEntitySource source, int X, int Y, int Width, int Height, "
        "int type, int stack = 1, bool noBroadcast = false, int prefix = 0, "
        "NewItemOwnership ownership = NewItemOwnership.None, Vector2? velocity = null, "
        "NewItemModifier modifier = null)"
    )
    rectangle = next((signature for signature in new_item_signatures if signature == expected_rectangle), None)
    if rectangle is None:
        raise SystemExit("Pinned rectangle-based Item.NewItem signature changed.")
    rectangle_body = compact(extract_method(item, rectangle))
    require(
        rectangle_body,
        "return NewItem(source, new Vector2((float)(X + Width / 2), (float)(Y + Height / 2)), type, stack, prefix, ownership, velocity, modifier, noBroadcast);",
        "rectangle-to-center delegation",
    )

    expected_vector = (
        "public static int NewItem(IEntitySource source, Vector2 center, int type, int stack = 1, "
        "int prefix = 0, NewItemOwnership ownership = NewItemOwnership.None, Vector2? velocity = null, "
        "NewItemModifier modifier = null, bool noBroadcast = false)"
    )
    vector = next((signature for signature in new_item_signatures if signature == expected_vector), None)
    if vector is None:
        raise SystemExit("Pinned vector-based Item.NewItem signature changed.")
    vector_body = compact(extract_method(item, vector))
    require(vector_body, "item.Prefix(prefix);", "spawn prefix application")
    require(vector_body, "worldItem.Center = center;", "spawn center assignment")
    require(
        vector_body,
        "worldItem.velocity.X = (float)Main.rand.Next(-30, 31) * 0.1f; worldItem.velocity.Y = (float)Main.rand.Next(-40, -15) * 0.1f;",
        "ordinary-gravity randomized velocity",
    )
    require(
        vector_body,
        "else if (Main.netMode == 2 && !noBroadcast) { NetMessage.SendData(21, -1, -1, null, num, (float)ownership); worldItem.ApplySpawnOwnership(ownership, _DefaultAssignItemsToNewPlayer ?? Main.myPlayer); }",
        "server packet-21 broadcast before spawn ownership",
    )

    defaults1 = next(iter(signatures(item, "SetDefaults1", require_static=False)), None)
    if defaults1 is None:
        raise SystemExit("Could not locate Item.SetDefaults1.")
    defaults1_body = compact(extract_method(item, defaults1))
    require(
        defaults1_body,
        "case 2: useStyle = 1; useTurn = true; useAnimation = 15; useTime = 10; autoReuse = true; consumable = true; createTile = 0; width = 12; height = 12; break;",
        "Dirt Block 12x12 defaults",
    )

    get_rollable = next(iter(signatures(item, "GetRollablePrefixes", require_static=False)), None)
    if get_rollable is None:
        raise SystemExit("Could not locate Item.GetRollablePrefixes.")
    get_rollable_body = compact(extract_method(item, get_rollable))
    require(get_rollable_body, "if (PrefixLegacy.ItemSets.SwordsHammersAxesPicks[type])", "melee prefix set gate")
    require(get_rollable_body, "if (IsAPrefixableAccessory())", "accessory prefix gate")
    require(get_rollable_body, "return null;", "non-prefixable return")

    print_context(prefix_legacy, "SwordsHammersAxesPicks")
    print_context(prefix_legacy, "SpearsMacesChainsawsDrillsPunchCannon")
    print_context(prefix_legacy, "GunsBows")
    print_context(prefix_legacy, "Magic")
    print_context(prefix_legacy, "Summon")
    print_context(prefix_legacy, "BoomerangsChakrams")
    print_context(prefix_legacy, "ItemsThatCanHaveLegendary2")
    print_context(item_id, "ItemNoGravity")

    print("dirt_drop_item=2")
    print("dirt_drop_stack=1")
    print("dirt_drop_item_size=12x12")
    print("dirt_drop_center=x*16+8,y*16+8")
    print("dirt_drop_position=x*16+2,y*16+2")
    print("dirt_drop_velocity_x=Main.rand.Next(-30,31)*0.1")
    print("dirt_drop_velocity_y=Main.rand.Next(-40,-15)*0.1")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
