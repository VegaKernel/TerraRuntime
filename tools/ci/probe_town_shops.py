#!/usr/bin/env python3
"""Pin TerrariaServer 1.4.5.8 Chest.SetupShop ordinary vendor branches 1..18."""
from __future__ import annotations

import argparse
import re
from pathlib import Path

EXPECTED: dict[int, list[int]] = {
    1: [88,87,35,1991,3509,3506,8,4388,28,188,110,189,40,42,965,967,33,4074,279,282,5643,346,488,931,1614,1786,1348,3198,4063,4673,3108],
    2: [97,4915,278,47,95,98,4703,324,534,1432,2177,1261,1836,3108,1783,1785,1736,1737,1738],
    3: [2886,2171,4508,67,59,4504,66,62,63,745,59,2171,27,5309,114,1828,747,746,369,4505,5214,194,1853,1854,3215,3216,3219,3218,3217,3220,3221,3222,4047,4045,4044,4043,4042,4046,4041,4241,4048,4430,4431,4432,4433,4434,4435,4436,4437,4438,4439,4440,4441,4430,4431,4433,4434,4436,4437,4439,4440,8,4386,4385],
    4: [168,166,5542,167,265,5481,5464,937,1347,4827,4824,4825,4826],
    5: [254,981,242,245,246,1288,1289,325,326,269,270,271,503,504,505,322,3362,3363,2856,2858,2857,2859,3242,3243,3244,4685,4686,4704,4705,4706,4707,4708,4709,1429,1740,869,4994,4997,864,865,4995,4998,873,874,875,4996,4999,1275,1276,3246,3247,3730,3731,3733,3734,3735,4744,5308,5630],
    6: [128,486,398,84,407,161,5324],
    7: [487,496,500,507,508,531,149,576,3186,5461,1739],
    8: [509,850,851,3612,510,530,513,538,529,541,542,543,852,853,4261,3707,2739,849,1263,3616,3725,2799,3619,3627,3629,585,584,583,4484,4485,4409,2295],
    9: [588,589,590,597,598,596],
    10: [756,787,868,1551,1181,5231,783],
    11: [779,748,839,840,841,948,3623,3603,3604,3607,3605,3606,3608,3618,3602,3663,3609,3610,995,2203,2193,4142,2192,2204,2195,2198,2197,784,782,781,780,5392,5393,5394,1344,4472,1742],
    12: [1120,5920,3248,1741,1037,2874,1969,2871,2872,4663,4662],
    13: [859,4743,1000,1168,1449,4552,1345,1450,3253,4553,2700,2738,4470,4681,4682,4702,3548,3369,3546,3214,2868,970,971,972,973,4791,3747,3732,3742,3749,3746,3739,3740,3741,3737,3738,3736,3745,3744,3743],
    14: [771,772,773,774,4445,4446,4459,760,1346,5452,5451,5738,5598,5599,4409,4392,1743,1744,1745,2862,3109,3664,5928],
    15: [1071,1072,1100,1097,1099,1098,1966,1967,1968,4668,5344],
    16: [1430,986,2999,6147,1158,1159,1160,1161,1167,1339,1171,1162,909,910,940,941,942,943,944,945,4922,4417,1836,1261,1791],
    17: [928,929,876,877,878,2434,5926,1180,1337],
    18: [1990,1979,1977,1978,1980,1981,1982,1983,1984,1985,1986,2863,3259,5104,5577],
}

EXPECTED_LOOPS = {
    9: r"for \(int m = 1873; m < 1906; m\+\+\)",
    15: r"for \(int num6 = 1073; num6 <= 1084; num6\+\+\)",
}

REQUIRED_SOURCE_MARKERS = {
    1: ["Main.hardMode", "Main.bloodMoon", "BirthdayParty.PartyIsUp"],
    5: ["NPC.downedClown", "NPC.downedAncientCultist", "Main.player[Main.myPlayer].ZoneGraveyard"],
    8: ["NPC.AnyNPCs(369)", "Main.player[Main.myPlayer].ZoneGraveyard"],
    11: ["NPC.downedMoonlord", "Main.player[Main.myPlayer].ZoneSkyHeight", "Main.player[Main.myPlayer].ZoneGraveyard"],
    13: ["LanternNight.LanternsUp", "NPC.AnyNPCs(229)"],
    14: ["Main.eclipse", "NPC.downedMartians"],
    16: ["NPC.AnyNPCs(108)", "Main.player[Main.myPlayer].ZoneJungle", "NPC.downedPlantBoss"],
    17: ["Main.player[Main.myPlayer].ZoneBeach", "NPC.AnyNPCs(208)"],
    18: ["Main.player[Main.myPlayer].statLifeMax", "Main.player[Main.myPlayer].statManaMax", "Main.player[Main.myPlayer].team"],
}


def extract_setup_shop(text: str) -> str:
    start = text.find("public void SetupShop(int type)")
    if start < 0:
        raise SystemExit("Chest.SetupShop(int type) not found")
    return text[start:]


def extract_case(setup: str, case: int) -> str:
    match = re.search(rf"^\t\tcase {case}:\s*$", setup, re.MULTILINE)
    if match is None:
        raise SystemExit(f"SetupShop case {case} not found")
    next_case = re.search(r"^\t\tcase \d+:\s*$", setup[match.end():], re.MULTILINE)
    end = match.end() + (next_case.start() if next_case else len(setup))
    return setup[match.start():end]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--chest", required=True, type=Path)
    args = parser.parse_args()
    setup = extract_setup_shop(args.chest.read_text(encoding="utf-8"))

    for case, expected in EXPECTED.items():
        block = extract_case(setup, case)
        actual = [int(value) for value in re.findall(r"SetDefaults\((\d+)\)", block)]
        if actual != expected:
            raise SystemExit(f"SetupShop case {case} item sequence drifted:\nexpected={expected}\nactual={actual}")
        loop = EXPECTED_LOOPS.get(case)
        if loop is not None and re.search(loop, block) is None:
            raise SystemExit(f"SetupShop case {case} loop contract drifted: {loop}")
        for marker in REQUIRED_SOURCE_MARKERS.get(case, []):
            if marker not in block:
                raise SystemExit(f"SetupShop case {case} lost source marker: {marker}")

    print("TerrariaServer 1.4.5.8 Chest.SetupShop cases 1..18 match the pinned town-shop contract.")


if __name__ == "__main__":
    main()
