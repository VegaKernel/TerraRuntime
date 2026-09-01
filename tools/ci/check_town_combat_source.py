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
    require(npcid, r"DangerDetectRange\s*=\s*Factory\.CreateIntSet\([^;]*207,\s*60[^;]*441,\s*50[^;]*353,\s*60", "AI_007 melee danger ranges")
    require(npcid, r"AttackTime\s*=\s*Factory\.CreateIntSet\([^;]*207,\s*15[^;]*441,\s*15[^;]*353,\s*12", "AI_007 melee attack times")
    require(npcid, r"AttackAverageChance\s*=\s*Factory\.CreateIntSet\([^;]*207,\s*1[^;]*441,\s*1[^;]*353,\s*1", "AI_007 melee attack chances")
    require(npcid, r"AttackType\s*=\s*Factory\.CreateIntSet\([^;]*207,\s*3[^;]*441,\s*3[^;]*353,\s*3", "AI_007 melee attack types")
    require(npcid, r"IsTownPet\s*=\s*Factory\.CreateBoolSet\(637,\s*638,\s*656,\s*670,\s*678,\s*679,\s*680,\s*681,\s*682,\s*683,\s*684\)", "Town pet identity set")
    require(npcid, r"AttackType\s*=\s*Factory\.CreateIntSet\([^;]*638,\s*-1[^;]*637,\s*-1[^;]*656,\s*-1[^;]*670,\s*-1", "Town pets do not enter melee attack naturally")

    ai7 = slice_between(npc, "private void AI_007_TownEntities()", "private void AI_007_TownEntities_Shimmer_TeleportToLandingSpot()", "AI_007")
    state10 = slice_between(ai7, "else if (ai[0] == 10f)", "else if (ai[0] == 12f)", "AI_007 state 10")
    state12 = slice_between(ai7, "else if (ai[0] == 12f)", "else if (ai[0] == 13f)", "AI_007 state 12")
    state15 = slice_between(ai7, "else if (ai[0] == 15f)", "else if (ai[0] == 24f)", "AI_007 state 15")

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
    require(ai7, r"AttackType\[type\] == 1", "attack type one branch")
    require(ai7, r"if \(\s*(\w+)\.Y <= 0\.5f\s*&&\s*\1\.Y >= -0\.5f\s*\)", "attack type one vertical angle gate")
    require(ai7, r"AttackType\[type\] == 1.*?ai\[0\] = 12f", "attack type one enters state 12")

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

    require(ai7, r"AttackType\[type\] == 3.*?ai\[0\] = 15f.*?ai\[1\] = num132", "attack type three enters state 15")
    require(state15, r"type == 207.*?num81 = 11;.*?num83 = \(num84 = 32\);.*?num80 = 12;.*?maxValue4 = 6;.*?num82 = 4\.25f;", "Dye Trader melee profile")
    require(state15, r"type == 441.*?num81 = 9;.*?num83 = \(num84 = 28\);.*?num80 = 9;.*?maxValue4 = 3;.*?num82 = 3\.5f;.*?GivenName == \"Andrew\".*?num81 \*= 2;.*?num82 \*= 2f;", "Tax Collector Andrew melee profile")
    require(state15, r"type == 353.*?num81 = 10;.*?num83 = \(num84 = 32\);.*?num80 = 15;.*?maxValue4 = 8;.*?num82 = 5f;", "Stylist melee profile")
    require(state15, r"NPCID\.Sets\.IsTownPet\[type\].*?num81 = 10;.*?num83 = \(num84 = 32\);.*?num80 = 15;.*?maxValue4 = 8;.*?num82 = 3f;", "source-dead town pet state 15 body")
    require(state15, r"GetSwingStats\(NPCID\.Sets\.AttackTime\[type\] \* 2, \(int\)ai\[1\], spriteDirection, num83, num84\)", "state 15 swing geometry")
    require(state15, r"TweakSwingStats\(NPCID\.Sets\.AttackTime\[type\] \* 2, \(int\)ai\[1\], spriteDirection, ref itemRectangle\)", "state 15 swing rectangle tweak")
    require(state15, r"immune.*?==\s*0", "state 15 server immunity slot gate")
    require(state15, r"dontTakeDamage.*?friendly.*?damage.*?>\s*0", "state 15 hostile damageability gate")
    require(state15, r"itemRectangle.*?Intersects.*?Hitbox", "state 15 melee hitbox intersection")
    require(state15, r"StrikeNPCNoInteraction.*?immune.*?ai\[1\].*?\+\s*2", "state 15 hit and immunity ordering")
    require(npc, r"GetSwingStats.*?swingMax.*?0\.333.*?swingMax.*?0\.666", "GetSwingStats three phases")
    require(npc, r"TweakSwingStats.*?Width.*?1\.4.*?Width\s*\*=\s*2", "TweakSwingStats widening")

    require(npc, r"public int GetAttackDamage_ForTownNPC\(float normalDamage\).*?normalDamage \* GameDifficultyData\.TownNPCDamageMultiplier\.Sample\(Main\.Difficulty\)", "Town NPC difficulty damage projection")
    require(difficulty, r"TownNPCDamageMultiplier\s*=\s*new LinearCurve\(new LinearCurve\.Key\(GameDifficultyLevel\.Journey, 2f\), new LinearCurve\.Key\(GameDifficultyLevel\.Classic, 1f\), new LinearCurve\.Key\(GameDifficultyLevel\.Expert, 1\.5f\), new LinearCurve\.Key\(GameDifficultyLevel\.Legendary, 2f\)\)", "Town NPC difficulty curve")

    print("Town NPC AI_007 projectile combat source contract: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
