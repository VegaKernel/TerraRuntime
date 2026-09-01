#!/usr/bin/env python3
from __future__ import annotations

import argparse
import pathlib
import re
import sys


def require(text: str, pattern: str, label: str) -> None:
    if re.search(pattern, text, re.S) is None:
        raise SystemExit(f"missing source contract: {label}")


def slice_between(text: str, start: str, end: str, label: str) -> str:
    a = text.find(start)
    if a < 0:
        raise SystemExit(f"missing source boundary: {label} start")
    b = text.find(end, a + len(start))
    if b < 0:
        raise SystemExit(f"missing source boundary: {label} end")
    return text[a:b]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--npc", required=True)
    parser.add_argument("--npcid", required=True)
    parser.add_argument("--difficulty", required=True)
    args = parser.parse_args()

    npc = pathlib.Path(args.npc).read_text(encoding="utf-8-sig")
    npcid = pathlib.Path(args.npcid).read_text(encoding="utf-8-sig")
    difficulty = pathlib.Path(args.difficulty).read_text(encoding="utf-8-sig")

    require(npcid, r"DangerDetectRange\s*=\s*Factory\.CreateIntSet\([^;]*17,\s*320[^;]*19,\s*900[^;]*22,\s*700[^;]*18,\s*300", "AI_007 danger ranges")
    require(npcid, r"AttackTime\s*=\s*Factory\.CreateIntSet\([^;]*17,\s*34[^;]*19,\s*40[^;]*22,\s*30[^;]*18,\s*34", "AI_007 attack times")
    require(npcid, r"AttackAverageChance\s*=\s*Factory\.CreateIntSet\([^;]*17,\s*30[^;]*19,\s*30[^;]*22,\s*30[^;]*18,\s*60", "AI_007 attack chances")
    require(npcid, r"AttackType\s*=\s*Factory\.CreateIntSet\([^;]*17,\s*0[^;]*19,\s*1[^;]*22,\s*1[^;]*18,\s*0", "AI_007 attack types")

    ai7 = slice_between(npc, "private void AI_007_TownEntities()", "private void AI_007_TownEntities_Shimmer_TeleportToLandingSpot()", "AI_007")
    state10 = slice_between(ai7, "else if (ai[0] == 10f)", "else if (ai[0] == 12f)", "AI_007 state 10")
    state12 = slice_between(ai7, "else if (ai[0] == 12f)", "else if (ai[0] == 13f)", "AI_007 state 12")

    require(ai7, r"float num2 = 1f;", "town damage progression scale initial value")
    require(ai7, r"float num3 = 2f;", "town attack chance progression scale initial value")
    require(ai7, r"combatBookWasUsed.*?num3 \*= 0\.8f;.*?num2 \+= 0\.25f;", "Combat Book progression modifiers")
    require(ai7, r"combatBookVolumeTwoWasUsed.*?num3 \*= 0\.8f;.*?num2 \+= 0\.25f;", "Combat Book Volume Two progression modifiers")
    require(ai7, r"downedSlimeKing.*?num3 \*= 0\.985f;.*?num2 \+= 0\.05f;", "King Slime progression modifier")
    require(ai7, r"downedBoss1.*?num3 \*= 0\.985f;.*?num2 \+= 0\.05f;", "Eye progression modifier")
    require(ai7, r"Main\.hardMode.*?num3 \*= 0\.985f;.*?num2 \+= 0\.4f;", "Hardmode progression modifier")
    require(ai7, r"downedAncientCultist.*?num3 \*= 0\.985f;.*?num2 \+= 0\.15f;", "Cultist progression modifier")
    require(ai7, r"localAI\[1\] > 0f.*?localAI\[1\]--;.*?flag31 = false;", "local attack cooldown gate")
    require(ai7, r"AttackAverageChance\[type\] \* num3", "source attack chance scaling")
    require(ai7, r"AttackType\[type\] == 0.*?ai\[0\] = 10f", "attack type zero enters state 10")
    require(ai7, r"AttackType\[type\] == 1.*?vector8\.Y <= 0\.5f.*?vector8\.Y >= -0\.5f.*?ai\[0\] = 12f", "attack type one angle gate/state 12")

    require(state10, r"type == 17.*?num42 = 48;.*?num44 = 9f;.*?num43 = 12;.*?num45 = 10;.*?num46 = 60;.*?maxValue = 60;.*?num47 = 16f;.*?knockBack = 1\.5f;", "Merchant throwing knife profile")
    require(state10, r"type == 18.*?num42 = 583;.*?num44 = 8f;.*?num43 = 8;.*?num45 = 1;.*?num46 = 15;.*?maxValue = 10;.*?knockBack = 2f;.*?num47 = 10f;", "Nurse hostile syringe profile")
    require(state10, r"num43 = GetAttackDamage_ForTownNPC\(\(float\)num43 \* num2\);", "state 10 town damage scaling")
    require(state10, r"velocity\.X \*= 0\.8f;\s*ai\[1\]--;\s*localAI\[3\]\+\+;", "state 10 timer order")
    require(state10, r"localAI\[1\] = \(localAI\[3\] = num46 / 2 \+ Main\.rand\.Next\(maxValue\)\)", "state 10 recovery ordering")

    require(state12, r"type == 19.*?num51 = 14;.*?num53 = 13f;.*?num52 = 24;.*?num55 = 14;.*?maxValue2 = 4;.*?knockBack2 = 3f;.*?num54 = 1;.*?num57 = 0\.5f;", "Arms Dealer normal bullet profile")
    require(state12, r"type == 19.*?Main\.hardMode.*?num52 = 15;.*?num54 = 10;.*?num54 = 20;.*?num54 = 30;", "Arms Dealer hardmode burst")
    require(state12, r"type == 22.*?num53 = 10f;.*?num52 = 12;.*?num54 = 1;.*?Main\.hardMode.*?num51 = 2;.*?num55 = 15;.*?maxValue2 = 10;.*?num52 \+= 6;.*?num51 = 1;.*?num55 = 30;.*?maxValue2 = 20;.*?knockBack2 = 2\.75f;.*?num56 = 4;.*?num57 = 0\.7f;", "Guide wooden/fire arrow profile")
    require(state12, r"num52 = GetAttackDamage_ForTownNPC\(\(float\)num52 \* num2\);", "state 12 town damage scaling")
    require(state12, r"velocity\.X \*= 0\.8f;\s*ai\[1\]--;\s*localAI\[3\]\+\+;", "state 12 timer order")
    require(state12, r"localAI\[1\] = \(localAI\[3\] = num55 / 2 \+ Main\.rand\.Next\(maxValue2\)\)", "state 12 recovery ordering")

    require(npc, r"public int GetAttackDamage_ForTownNPC\(float normalDamage\).*?normalDamage \* GameDifficultyData\.TownNPCDamageMultiplier\.Sample\(Main\.Difficulty\)", "Town NPC difficulty damage projection")
    require(difficulty, r"TownNPCDamageMultiplier\s*=\s*new LinearCurve\(new LinearCurve\.Key\(GameDifficultyLevel\.Journey, 2f\), new LinearCurve\.Key\(GameDifficultyLevel\.Classic, 1f\), new LinearCurve\.Key\(GameDifficultyLevel\.Expert, 1\.5f\), new LinearCurve\.Key\(GameDifficultyLevel\.Legendary, 2f\)\)", "Town NPC difficulty curve")

    print("Town NPC AI_007 projectile combat source contract: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
