"""Pin late-boss persistence and lunar-event header ordering to TerrariaServer 1.4.5.8."""

import argparse
import hashlib
from pathlib import Path
import re

from check_moon_lord_death_source import method, require


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--world", type=Path, required=True)
    args = parser.parse_args()
    if hashlib.sha256(args.assembly.read_bytes()).hexdigest() != "d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e":
        raise SystemExit("Reference assembly is not the pinned TerrariaServer 1.4.5.8")
    raw = args.world.read_bytes()
    source = raw.decode("utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
    save = method(source, "SaveWorldFlags")
    load = method(source, "LoadWorldFlags")
    late = ["downedFishron", "downedMartians", "downedAncientCultist", "downedMoonlord",
            "downedHalloweenKing", "downedHalloweenTree", "downedChristmasIceQueen",
            "downedChristmasSantank", "downedChristmasTree", "downedTowerSolar",
            "downedTowerVortex", "downedTowerNebula", "downedTowerStardust", "TowerActiveSolar",
            "TowerActiveVortex", "TowerActiveNebula", "TowerActiveStardust", "LunarApocalypseIsUp"]
    def writes(fields: list[str]) -> str:
        return " ".join(r"writer\.Write\(" + re.escape(field) + r"\);" for field in fields)
    require(save, writes(["Main.fastForwardTimeToDawn"] + ["NPC." + name for name in late] +
                         ["_tempPartyManual", "_tempPartyGenuine"]), "late boss and lunar save field order")
    require(save, writes(["NPC." + name for name in
                         ["boughtCat", "boughtDog", "boughtBunny", "downedEmpressOfLight", "downedQueenSlime", "downedDeerclops"]]),
            "Empress save position")
    for name in late + ["downedEmpressOfLight"]:
        require(load, r"NPC\." + name + r" = reader\.ReadBoolean\(\);", "load " + name)
    print("Verified original late-boss and lunar-event save/load contract (1.4.5.8).")


if __name__ == "__main__":
    main()
