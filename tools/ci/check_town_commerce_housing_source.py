#!/usr/bin/env python3
from argparse import ArgumentParser
from pathlib import Path

p = ArgumentParser()
p.add_argument('--player', required=True)
p.add_argument('--message-buffer', required=True)
p.add_argument('--shop-helper', required=True)
p.add_argument('--scene-metrics', required=True)
p.add_argument('--worldgen', required=True)
a = p.parse_args()

sources = {
    'Player.SetTalkNPC': Path(a.player).read_text(errors='ignore'),
    'MessageBuffer': Path(a.message_buffer).read_text(errors='ignore'),
    'ShopHelper': Path(a.shop_helper).read_text(errors='ignore'),
    'SceneMetrics': Path(a.scene_metrics).read_text(errors='ignore'),
    'WorldGen': Path(a.worldgen).read_text(errors='ignore'),
}

contracts = {
    'Player.SetTalkNPC': [
        'currentShoppingSettings = Main.ShopHelper.GetShoppingSettings(this, Main.npc[talkNPC])',
    ],
    'MessageBuffer': [
        'num53 = whoAmI',
        'Main.player[num53].SetTalkNPC(talkNPC)',
    ],
    'ShopHelper': [
        'LowestPossiblePriceMultiplier = 0.75f',
        'HighestPossiblePriceMultiplier = 1.5f',
        'Main.remixWorld || npc.type == 368 || npc.type == 453',
        'if (num < 25f)',
        'else if (num < 120f)',
    ],
    'SceneMetrics': [
        'AssumedConstantScreenSize = new Point(1920, 1200)',
        'ZoneScanPadding = 25',
        'CorruptionTileThreshold = 300',
        'CrimsonTileThreshold = 300',
        'HallowTileThreshold = 125',
        'JungleTileThreshold = 140',
        'SnowTileNormalThreshold = 1500',
        'SnowTileSkyblockThreshold = 300',
        'DesertTileNormalThreshold = 1500',
        'DesertTileSkyblockThreshold = 300',
        'MushroomTileThreshold = 100',
        'DungeonTileThreshold = 250',
        'GraveyardTileThreshold = 28',
    ],
    'WorldGen': [
        'if (type == 160)',
        '!NPC.unlockedTruffleSpawn && (double)roomY2 > Main.worldSurface && !Main.NoFunctionalSurface',
        'tile.type == 70 || tile.type == 71 || tile.type == 72 || tile.type == 528',
        'num >= SceneMetrics.MushroomTileThreshold',
        'NPC.unlockedTruffleSpawn = true',
    ],
}

missing = []
for source, needles in contracts.items():
    text = sources[source]
    for needle in needles:
        if needle not in text:
            missing.append(f'{source}: {needle}')

if missing:
    raise SystemExit('Town commerce/housing source drift:\n' + '\n'.join(missing))

print('TerrariaServer 1.4.5.8 town commerce/housing source contract OK')
