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


def require_in_order(block: str, *markers: str) -> None:
    cursor = 0
    for marker in markers:
        index = block.find(marker, cursor)
        if index < 0:
            raise SystemExit(f"town spawn priority order drifted; missing/out-of-order marker: {marker}")
        cursor = index + len(marker)


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
        "int num42 = WorldGen.prioritizedTownNPCType;",
        "WorldGen.prioritizedTownNPCType = num42;",
    )

    require_in_order(
        update,
        "if (num42 == 0 && infectedSeed && num4 < 1)",
        "if (num42 == 0 && vampireSeed && !infectedSeed && num27 < 1)",
        "if (num42 == 0 && num6 < 1)",
        "if (((num42 == 0) & flag) && num2 < 1)",
        "if (((num42 == 0) & flag3) && num3 < 1 && num2 > 0)",
        "if (((num42 == 0) & flag2) && num5 < 1)",
        "if (num42 == 0 && NPC.savedGoblin && num11 < 1)",
        "if (num42 == 0 && NPC.savedWizard && num10 < 1)",
        "if (num42 == 0 && (NPC.downedBoss1 || NPC.downedBoss2 || NPC.downedBoss3) && num4 < 1)",
        "if (((num42 == 0) & flag5) && num2 > 0 && num8 < 1)",
        "if (num42 == 0 && NPC.downedQueenBee && num20 < 1)",
        "if (num42 == 0 && NPC.downedMechBossAny && num15 < 1)",
        "if (num42 == 0 && NPC.savedMech && num12 < 1)",
        "if (num42 == 0 && NPC.savedAngler && num23 < 1)",
        "if (num42 == 0 && hardMode && NPC.downedPlantBoss && num18 < 1)",
        "if (num42 == 0 && NPC.downedPirates && num21 < 1)",
        "if (num42 == 0 && NPC.downedBoss3 && num9 < 1)",
        "if (num42 == 0 && NPC.savedStylist && num22 < 1)",
        "if (((num42 == 0 && num40 >= 4) & flag4) && num16 < 1)",
        "if (num42 == 0 && num40 >= 8 && num19 < 1)",
        "if (((num42 == 0) & flag7) && num17 < 1)",
        "if (num42 == 0 && NPC.downedFrost && num13 < 1 && xMas)",
        "if (num42 == 0 && NPC.savedBartender && num25 < 1)",
        "if (num42 == 0 && NPC.savedGolfer && num26 < 1)",
        "if (num42 == 0 && NPC.savedTaxCollector && num24 < 1)",
        "if (num42 == 0 && hardMode && num14 < 1)",
        "if (num42 == 0 && bestiaryProgressReport.CompletionPercent >= 0.1f && num27 < 1)",
        "if (((num42 == 0) & flag9) && num39 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimeCopperSpawn && num38 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimeBlueSpawn && num31 < 1)",
        "if (((num42 == 0) & flag8) && num32 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimeOldSpawn && num33 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimePurpleSpawn && num34 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimeRedSpawn && num36 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimeYellowSpawn && num37 < 1)",
        "if (num42 == 0 && NPC.unlockedSlimeRainbowSpawn && num35 < 1)",
        "if (num42 == 0 && NPC.boughtBunny && num30 < 1)",
        "if (num42 == 0 && NPC.boughtCat && num28 < 1)",
        "if (num42 == 0 && NPC.boughtDog && num29 < 1)",
        "WorldGen.prioritizedTownNPCType = num42;",
    )

    for signature in (
        "public static bool SpawnAllowed_Merchant()",
        "public static bool SpawnAllowed_ArmsDealer()",
        "public static bool SpawnAllowed_Nurse()",
        "public static bool SpawnAllowed_DyeTrader()",
        "public static bool SpawnAllowed_Demolitionist()",
    ):
        extract_method(npc_text, signature)

    require(extract_method(npc_text, "public static bool SpawnAllowed_Merchant()"), "5000", "Main.player")
    require(extract_method(npc_text, "public static bool SpawnAllowed_Nurse()"), "statLifeMax")

    print("TerrariaServer 1.4.5.8 town spawn cadence, eligibility and priority markers match the pinned contract.")


if __name__ == "__main__":
    main()
