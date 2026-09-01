#!/usr/bin/env python3
from argparse import ArgumentParser
from pathlib import Path

p = ArgumentParser()
p.add_argument('--worldgen', required=True)
p.add_argument('--main', required=True)
p.add_argument('--npc', required=True)
a = p.parse_args()

sources = {
    'WorldGen': Path(a.worldgen).read_text(errors='ignore'),
    'Main': Path(a.main).read_text(errors='ignore'),
    'NPC': Path(a.npc).read_text(errors='ignore'),
}

contracts = {
    'WorldGen': [
        'public static void QuickFindHome(int npc)',
        'bool flag = Main.tileSolid[379];',
        'Main.tileSolid[379] = true;',
        'StartRoomCheck(Main.npc[npc].homeTileX, Main.npc[npc].homeTileY - 1);',
        'for (int i = Main.npc[npc].homeTileX - 1; i < Main.npc[npc].homeTileX + 2; i++)',
        'for (int j = Main.npc[npc].homeTileY - 1; j < Main.npc[npc].homeTileY + 2 && !StartRoomCheck(i, j); j++)',
        'int num = 10;',
        'for (int k = Main.npc[npc].homeTileX - num; k <= Main.npc[npc].homeTileX + num; k += 2)',
        'for (int l = Main.npc[npc].homeTileY - num; l <= Main.npc[npc].homeTileY + num && !StartRoomCheck(k, l); l += 2)',
        'if (!CheckSpecialTownNPCSpawningConditions(Main.npc[npc].type))',
        'RoomNeeds();',
        'ScoreRoom(npc, Main.npc[npc].type);',
        'canSpawn = IsRoomConsideredOccupiedForNPCIndex(npc);',
        'Main.npc[npc].homeTileX = bestX;',
        'Main.npc[npc].homeTileY = bestY;',
        'Main.npc[npc].homeless = false;',
        'Main.npc[npc].homelessDespawn = false;',
        'Main.npc[npc].homeless = true;',
        'Main.tileSolid[379] = flag;',
        'public static void CountTileTypesInArea(int[] tileTypeCounts, int startX, int endX, int startY, int endY)',
        'if (Main.tile[i, j].active())',
    ],
    'Main': [
        'private static void UpdateTime_SpawnTownNPCs()',
        'checkForSpawns++;',
        'if (checkForSpawns < 7200 / worldUpdateRate)',
        'npc[k].type != 368 && npc[k].type != 37 && npc[k].type != 453 && !npc[k].homeless',
        'WorldGen.QuickFindHome(k);',
    ],
    'NPC': [
        'private void AI_007_TownEntities_TeleportToHome(int homeFloorX, int homeFloorY)',
        'homeless = true;',
        'WorldGen.QuickFindHome(whoAmI);',
    ],
}

missing = []
for source, needles in contracts.items():
    text = sources[source]
    for needle in needles:
        if needle not in text:
            missing.append(f'{source}: {needle}')

if missing:
    raise SystemExit('Town QuickFindHome source drift:\n' + '\n'.join(missing))

print('TerrariaServer 1.4.5.8 Town QuickFindHome source contract OK')
