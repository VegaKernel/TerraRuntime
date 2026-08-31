#!/usr/bin/env python3
import argparse
import re
from pathlib import Path

PICKAXES = (
    "CopperPickaxe",
    "TinPickaxe",
    "IronPickaxe",
    "LeadPickaxe",
    "SilverPickaxe",
    "TungstenPickaxe",
    "GoldPickaxe",
    "PlatinumPickaxe",
    "NightmarePickaxe",
    "DeathbringerPickaxe",
    "MoltenPickaxe",
)
STACK_ITEMS = ("DirtBlock", "Gel", "SlimeStaff", "CopperPickaxe")


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def find_item_id(item_ids: str, name: str) -> int:
    patterns = (
        rf"\b{re.escape(name)}\s*=\s*(-?\d+)\s*;",
        rf"\b{re.escape(name)}\s*=\s*unchecked\(\(short\)(-?\d+)\)\s*;",
    )
    for pattern in patterns:
        match = re.search(pattern, item_ids)
        if match:
            return int(match.group(1))
    raise SystemExit(f"Could not locate ItemID.{name} in pinned source.")


def case_fragments(source: str, item_id: int) -> list[str]:
    pattern = re.compile(
        rf"case\s+{item_id}\s*:(?P<body>.*?)(?=case\s+-?\d+\s*:|default\s*:|\}})",
        re.DOTALL,
    )
    return [compact(match.group("body")) for match in pattern.finditer(source)]


def method_body(source: str, name: str) -> str:
    match = re.search(rf"\b{name}\s*\([^\r\n)]*\)\s*\{{", source)
    if match is None:
        raise SystemExit(f"Could not locate Item.{name} in pinned source.")
    brace = source.find("{", match.start())
    depth = 0
    in_string = False
    escaped = False
    for index in range(brace, len(source)):
        ch = source[index]
        if escaped:
            escaped = False
        elif ch == "\\" and in_string:
            escaped = True
        elif ch == '"':
            in_string = not in_string
        elif not in_string:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return source[brace + 1:index]
    raise SystemExit(f"Item.{name} body did not terminate.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 pickaxe item defaults.")
    parser.add_argument("--item", required=True)
    parser.add_argument("--item-id", required=True)
    args = parser.parse_args()

    item = Path(args.item).read_text(encoding="utf-8")
    item_ids = Path(args.item_id).read_text(encoding="utf-8")

    for name in PICKAXES:
        item_id = find_item_id(item_ids, name)
        fragments = case_fragments(item, item_id)
        print(f"pickaxe_{name}_id={item_id}")
        print(f"pickaxe_{name}_case_count={len(fragments)}")
        for index, fragment in enumerate(fragments):
            print(f"pickaxe_{name}_case_{index}={fragment[:1200]}")

    for name in STACK_ITEMS:
        item_id = find_item_id(item_ids, name)
        fragments = case_fragments(item, item_id)
        assignments = []
        for fragment in fragments:
            assignments.extend(re.findall(r"\bmaxStack\s*=\s*[^;]+;", fragment))
        print(f"stack_{name}_id={item_id}")
        print(f"stack_{name}_case_assignments={','.join(assignments) if assignments else 'none'}")

    set_defaults = compact(method_body(item, "SetDefaults"))
    reset_match = re.search(r".{0,300}\bmaxStack\s*=\s*1\s*;.{0,300}", set_defaults)
    print(f"item_setdefaults_maxstack1_context={reset_match.group(0) if reset_match else 'not-found'}")

    print("pickaxe_catalog_probe=diagnostic")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
