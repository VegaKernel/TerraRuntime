#!/usr/bin/env python3
from argparse import ArgumentParser
from pathlib import Path
import re

p = ArgumentParser()
p.add_argument('--chest', required=True)
p.add_argument('--net-message', required=True)
p.add_argument('--message-buffer', required=True)
p.add_argument('--player', required=True)
p.add_argument('--luck', required=True)
p.add_argument('--worldgen', required=True)
p.add_argument('--main', required=True)
p.add_argument('--implementation', required=True)
a = p.parse_args()

sources = {
    'Chest': Path(a.chest).read_text(errors='ignore'),
    'NetMessage': Path(a.net_message).read_text(errors='ignore'),
    'MessageBuffer': Path(a.message_buffer).read_text(errors='ignore'),
    'Player': Path(a.player).read_text(errors='ignore'),
    'Luck': Path(a.luck).read_text(errors='ignore'),
    'WorldGen': Path(a.worldgen).read_text(errors='ignore'),
    'Main': Path(a.main).read_text(errors='ignore'),
}

contracts = {
    'Main': [
        'TravelShopMaxSlots',
        'travelShop',
    ],
    'Chest': [
        'public static void SetupTravelShop_AddToShop(int itemID, ref int added, ref int count)',
        'public static bool SetupTravelShop_CanAddItemToShop(int it)',
        'public static void SetupTravelShop_GetPainting(Player playerWithHighestLuck, int[] rarity, ref int it, int minimumRarity = 0)',
        'public static void SetupTravelShop_AdjustSlotRarities(int slotItemAttempts, ref int[] rarity)',
        'public static void SetupTravelShop_GetItem(Player playerWithHighestLuck, int[] rarity, ref int it, int minimumRarity = 0)',
        'public static void SetupTravelShop()',
        'Player playerWithHighestLuck = Player.GetPlayerWithHighestLuck();',
        'int num = Main.rand.Next(4, 7);',
        'if (Main.expertMode && playerWithHighestLuck.RollLuck(2) == 0)',
        'if (NPC.peddlersSatchelWasUsed)',
        'if (Main.tenthAnniversaryWorld)',
        'int[] array = new int[6] { 100, 200, 300, 400, 500, 600 };',
        'while (num2 < 5000)',
        'while (added < num)',
        'SetupTravelShop_GetPainting(playerWithHighestLuck, rarity, ref it3);',
        'if (itemID == 2260)',
        'if (itemID == 5680)',
        'if (itemID == 4555)',
        'if (itemID == 4321)',
        'if (itemID == 4323)',
        'if (itemID == 5390)',
        'if (itemID == 4666)',
        'if (itemID == 3637)',
        'it = 2281 + Main.rand.Next(3);',
        'NPC.downedDeerclops || NPC.downedSlimeKing || NPC.downedBoss1 || NPC.downedBoss2 || NPC.downedBoss3 || NPC.downedQueenBee || Main.hardMode',
    ],
    'NetMessage': [
        'case 72:',
        'for (int num18 = 0; num18 < Main.TravelShopMaxSlots; num18++)',
        'writer.Write((short)Main.travelShop[num18]);',
        'public static void SendTravelShop(int remoteClient)',
        'SendData(72, remoteClient);',
        'SendNPCHousesAndTravelShop(plr);',
        'SendTravelShop(plr);',
    ],
    'Player': [
        'public int RollLuck(int range)',
        'return Luck.RollLuck(luck, range);',
        'public static Player GetPlayerWithHighestLuck()',
        'player2.active && (player == null || player.luck < player2.luck)',
    ],
    'Luck': [
        'public static int RollLuck(float luck, int range)',
        'if (luck > 0f && Main.rand.NextFloat() < luck)',
        'return Main.rand.Next(Main.rand.Next(range / 2, range));',
        'if (luck < 0f && Main.rand.NextFloat() < 0f - luck)',
        'return Main.rand.Next(Main.rand.Next(range, range * 2));',
        'return Main.rand.Next(range);',
    ],
    'WorldGen': [
        'public static void SpawnTravelNPC()',
        'Chest.SetupTravelShop();',
        'NetMessage.SendTravelShop(-1);',
        'NPC.NewNPC(NPC.GetSpawnSourceForTownSpawn()',
    ],
    'MessageBuffer': [
        'case 72:',
        'Main.TravelShopMaxSlots',
        'Main.travelShop',
        'reader.ReadInt16()',
    ],
}

missing = []
for source, needles in contracts.items():
    text = sources[source]
    for needle in needles:
        if needle not in text:
            missing.append(f'{source}: {needle}')

semantic_missing = []
semantic_contracts = {
    'Main': [
        r'TravelShopMaxSlots\s*=\s*40',
        r'travelShop\s*=\s*new\s+int\s*\[\s*TravelShopMaxSlots\s*\]',
    ],
    'MessageBuffer': [
        r'case\s+72\s*:',
        r'for\s*\([^)]*<\s*Main\.TravelShopMaxSlots[^)]*\)',
        r'Main\.travelShop\s*\[[^]]+\]\s*=\s*reader\.ReadInt16\s*\(\s*\)',
    ],
}
for source, patterns in semantic_contracts.items():
    text = sources[source]
    for pattern in patterns:
        if re.search(pattern, text, re.MULTILINE) is None:
            semantic_missing.append(f'{source}: /{pattern}/')

if missing or semantic_missing:
    failures = missing + semantic_missing
    raise SystemExit('Traveling Merchant source drift:\n' + '\n'.join(failures))

print('TerrariaServer 1.4.5.8 Traveling Merchant shop source contract OK')


def method_body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f'Implementation/source method missing: {signature}')
    brace = text.find('{', start)
    if brace < 0:
        raise SystemExit(f'Method body missing: {signature}')
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0:
                return text[brace:index + 1]
    raise SystemExit(f'Unbalanced method body: {signature}')

implementation = Path(a.implementation).read_text(errors='ignore')
for vanilla_signature, implementation_signature in (
    ('public static void SetupTravelShop_GetItem', 'private static void GetItem'),
    ('public static void SetupTravelShop_GetPainting', 'private static void GetPainting'),
):
    vanilla_body = method_body(sources['Chest'], vanilla_signature)
    implementation_body = method_body(implementation, implementation_signature)
    vanilla_ids = [int(value) for value in re.findall(r'\bit\s*=\s*(\d+)', vanilla_body)]
    implementation_ids = [int(value) for value in re.findall(r'\bitem\s*=\s*(\d+)', implementation_body)]
    if vanilla_ids != implementation_ids:
        raise SystemExit(
            f'Traveling Merchant item assignment order drift in {implementation_signature}:\n'
            f'vanilla={vanilla_ids}\nimplementation={implementation_ids}'
        )

print('Traveling Merchant implementation item assignment order matches pinned Chest.cs')
