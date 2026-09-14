"""Check the Moon Lord loot/participation contract against the pinned official reference."""

import argparse
import hashlib
from pathlib import Path
import re

from check_moon_lord_death_source import method, require


def read(path: Path) -> str:
    raw = path.read_bytes()
    return raw.decode("utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("assembly", "database", "selection", "npc"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    if hashlib.sha256(args.assembly.read_bytes()).hexdigest() != "d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e":
        raise SystemExit("Reference assembly is not the pinned TerrariaServer 1.4.5.8")
    database = read(args.database)
    require(method(database, "Populate"), r"RegisterBossTrophies\(\).*?RegisterBosses\(\)", "trophy ordering")
    boss = method(database, "RegisterBoss_MoonLord")
    require(boss, r"short type = 398;", "core identity")
    require(boss, r"Conditions.NotExpert condition", "Classic condition")
    rules = [
        "BossBag(3332)", "MasterModeCommonDrop(4938)",
        "MasterModeDropOnAllPlayers(4810, _masterModeDropRng)",
        "ByCondition(condition, 3373, 7)", "ByCondition(condition, 4469, 10)",
        "ByCondition(condition, 3384)", "ByCondition(condition, 3460, 1, 70, 90)",
        "FromOptionsWithoutRepeatsDropRule(2, 3063, 3389, 3065, 1553, 3930, 3541, 3570, 3571, 3569, 5480)",
    ]
    require(boss, ".*?".join(map(re.escape, rules)), "ordered difficulty rules and ten-option pool")
    require(database, r"_masterModeDropRng\s*=\s*4;", "pet denominator")
    selection = re.sub(r"\s+", " ", read(args.selection))
    require(selection, r"i < dropCount.*?rng.Next\(_temporaryAvailableItems.Count\).*?DropItemFromNPC\(info.npc, _temporaryAvailableItems\[index\], 1\);.*?RemoveAt\(index\)", "selection/delivery/removal ordering")
    interaction = method(read(args.npc), "ApplyInteraction")
    require(interaction, r"type == 396 \|\| type == 397\).*?Main.npc\[\(int\)ai\[3\]\].active && Main.npc\[\(int\)ai\[3\]\].type == 398\).*?Main.npc\[\(int\)ai\[3\]\].ApplyInteraction_Inner\(player, localOnly\)", "part participation uses exact active core")
    print("Moon Lord 1.4.5.8 loot and participation source contract passed")


if __name__ == "__main__":
    main()
