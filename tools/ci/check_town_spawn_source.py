#!/usr/bin/env python3
"""Pin TerrariaServer 1.4.5.8 Main.UpdateTime_SpawnTownNPCs source markers."""
from __future__ import annotations

import argparse
from pathlib import Path


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f"source method not found: {signature}")
    brace = text.find("{", start)
    if brace < 0:
        raise SystemExit(f"source method body not found: {signature}")
    depth = 0
    for i in range(brace, len(text)):
        ch = text[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[start : i + 1]
    raise SystemExit(f"unterminated source method: {signature}")


def require(block: str, *markers: str) -> None:
    for marker in markers:
        if marker not in block:
            raise SystemExit(f"town spawn source contract drifted; missing marker: {marker}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--main", required=True, type=Path)
    parser.add_argument("--npc", required=True, type=Path)
    args = parser.parse_args()

    main_text = args.main.read_text(encoding="utf-8")
    npc_text = args.npc.read_text(encoding="utf-8")

    update = extract_method(main_text, "private static void UpdateTime_SpawnTownNPCs()")
    require(
        update,
        "int worldUpdateRate = WorldGen.GetWorldUpdateRate();",
        "checkForSpawns++;",
        "checkForSpawns < 7200 / worldUpdateRate",
        "bool flag = NPC.SpawnAllowed_Merchant();",
        "bool flag2 = NPC.SpawnAllowed_ArmsDealer();",
        "bool flag3 = NPC.SpawnAllowed_Nurse();",
        "bool flag4 = NPC.SpawnAllowed_DyeTrader();",
        "bool flag5 = NPC.SpawnAllowed_Demolitionist();",
        "rand.Next(40) == 0",
        "num40 >= 20",
        "NPC.unlockedPartyGirlSpawn",
        "BirthdayParty.GenuineParty",
        "NPC.unlockedSlimeGreenSpawn",
        "townNPCCanSpawn[22] = true;",
        "townNPCCanSpawn[17] = true;",
        "townNPCCanSpawn[18] = true;",
        "townNPCCanSpawn[19] = true;",
        "townNPCCanSpawn[38] = true;",
        "townNPCCanSpawn[207] = true;",
        "townNPCCanSpawn[208] = true;",
        "townNPCCanSpawn[633] = true;",
        "townNPCCanSpawn[663] = true;",
        "townNPCCanSpawn[670] = true;",
        "townNPCCanSpawn[684] = true;",
    )

    for signature in (
        "public static bool SpawnAllowed_Merchant()",
        "public static bool SpawnAllowed_ArmsDealer()",
        "public static bool SpawnAllowed_Nurse()",
        "public static bool SpawnAllowed_DyeTrader()",
        "public static bool SpawnAllowed_Demolitionist()",
    ):
        extract_method(npc_text, signature)

    require(
        extract_method(npc_text, "public static bool SpawnAllowed_Merchant()"),
        "5000",
        "Main.player",
    )
    require(
        extract_method(npc_text, "public static bool SpawnAllowed_Nurse()"),
        "statLifeMax",
    )

    print("TerrariaServer 1.4.5.8 town spawn cadence and eligibility markers match the pinned contract.")


if __name__ == "__main__":
    main()
