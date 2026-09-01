#!/usr/bin/env python3
from __future__ import annotations
import argparse
import json
import re
from pathlib import Path

CATEGORIES = (
    "MerchantNames", "NurseNames", "ArmsDealerNames", "DryadNames", "GuideNames",
    "DemolitionistNames", "ClothierNames", "GoblinTinkererNames", "WizardNames", "MechanicNames",
    "TruffleNames", "SteampunkerNames", "DyeTraderNames", "PartyGirlNames", "CyborgNames",
    "PainterNames", "WitchDoctorNames", "PirateNames", "StylistNames", "TravelingMerchantNames",
    "AnglerNames", "SkeletonMerchantNames", "TaxCollectorNames", "BartenderNames", "GolferNames",
    "BestiaryGirlNames", "PrincessNames",
    "CatNames_Siamese", "CatNames_Black", "CatNames_OrangeTabby", "CatNames_RussianBlue", "CatNames_Silver", "CatNames_White",
    "DogNames_Labrador", "DogNames_PitBull", "DogNames_Beagle", "DogNames_Corgi", "DogNames_Dalmation", "DogNames_Husky",
    "BunnyNames_White", "BunnyNames_Angora", "BunnyNames_Dutch", "BunnyNames_Flemish", "BunnyNames_Lop", "BunnyNames_Silver",
    "SlimeNames_Blue", "SlimeNames_Green", "SlimeNames_Old", "SlimeNames_Purple", "SlimeNames_Rainbow", "SlimeNames_Red", "SlimeNames_Yellow", "SlimeNames_Copper",
)


def read(path: str) -> str:
    return Path(path).read_text(encoding='utf-8')


def require(text: str, marker: str, label: str) -> None:
    if marker not in text:
        raise SystemExit(f'missing source marker: {label}: {marker!r}')


def require_ordered(text: str, markers: tuple[str, ...], label: str) -> None:
    pos = -1
    for marker in markers:
        nxt = text.find(marker, pos + 1)
        if nxt < 0:
            raise SystemExit(f'missing ordered source marker in {label}: {marker!r}')
        pos = nxt


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f'missing method signature: {signature}')
    brace = text.find('{', start)
    if brace < 0:
        raise SystemExit(f'missing opening brace: {signature}')
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == '{': depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    raise SystemExit(f'unterminated method: {signature}')


def load_json_with_trailing_commas(path: str) -> dict[str, object]:
    text = Path(path).read_text(encoding='utf-8-sig')
    text = re.sub(r',\s*([}\]])', r'\1', text)
    return json.loads(text)


def verify_runtime_name_catalog(identity_source: str, town_json_path: str) -> None:
    town = load_json_with_trailing_commas(town_json_path)
    for category in CATEGORIES:
        raw = town.get(category)
        if not isinstance(raw, dict):
            raise SystemExit(f'missing Town.json category: {category}')
        expected = list(raw.values())
        pattern = re.compile(
            rf'private static readonly string\[\] {re.escape(category)}\s*=\s*\[(.*?)\];',
            re.DOTALL,
        )
        match = pattern.search(identity_source)
        if match is None:
            raise SystemExit(f'missing runtime identity category: {category}')
        actual = [json.loads('"' + value + '"') for value in re.findall(r'"((?:\\.|[^"\\])*)"', match.group(1))]
        if actual != expected:
            raise SystemExit(f'runtime identity category differs from pinned Town.json: {category}')


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--npc', required=True)
    ap.add_argument('--profiles', required=True)
    ap.add_argument('--town-profiles', required=True)
    ap.add_argument('--worldgen', required=True)
    ap.add_argument('--chat-helper', required=True)
    ap.add_argument('--town-json', required=True)
    ap.add_argument('--runtime-identity', required=True)
    args = ap.parse_args()

    npc = read(args.npc)
    profiles = read(args.profiles)
    town_profiles = read(args.town_profiles)
    worldgen = read(args.worldgen)
    chat = read(args.chat_helper)
    runtime_identity = read(args.runtime_identity)

    get_name = extract_method(npc, 'private static string getNewNPCNameInner(int npcType)')
    for marker in (
        '17 => Language.RandomFromCategory("MerchantNames", WorldGen.genRand).Value',
        '633 => Language.RandomFromCategory("BestiaryGirlNames", WorldGen.genRand).Value',
        '637 => Language.RandomFromCategory("CatNames_Siamese", WorldGen.genRand).Value',
        '638 => Language.RandomFromCategory("DogNames_Labrador", WorldGen.genRand).Value',
        '656 => Language.RandomFromCategory("BunnyNames_White", WorldGen.genRand).Value',
        '670 => Language.RandomFromCategory("SlimeNames_Blue", WorldGen.genRand).Value',
        '684 => Language.RandomFromCategory("SlimeNames_Copper", WorldGen.genRand).Value',
    ):
        require(get_name, marker, 'NPC.getNewNPCNameInner category')

    unique = extract_method(npc, 'private static void GiveTownUniqueDataToNPCsThatNeedIt(int Type, int nextNPC)')
    require_ordered(unique, (
        'nPC.GivenName = getNewNPCName(Type)',
        'TownNPCProfiles.Instance.GetProfile(Type, out var profile)',
        'nPC.townNpcVariationIndex = profile.RollVariation()',
        'nPC.GivenName = profile.GetNameForVariant(nPC)',
        'if (ShimmeredTownNPCs[Type])',
        'nPC.townNpcVariationIndex = 1',
        'nPC.needsUniqueInfoUpdate = true',
    ), 'GiveTownUniqueDataToNPCsThatNeedIt')

    variant_class = profiles[profiles.find('public class VariantNPCProfile'):]
    require(variant_class, 'return Main.rand.Next(_variants.Length);', 'VariantNPCProfile Main.rand variation')
    require(variant_class, 'Language.RandomFromCategory(_npcBaseName + "Names_" + _variants[npc.townNpcVariationIndex], WorldGen.genRand).Value', 'VariantNPCProfile variant name')
    legacy = profiles[profiles.find('public class LegacyNPCProfile'):profiles.find('public class TransformableNPCProfile')]
    require(legacy, 'return NPC.getNewNPCName(npc.type);', 'LegacyNPCProfile second name roll')
    transform = profiles[profiles.find('public class TransformableNPCProfile'):profiles.find('public class VariantNPCProfile')]
    require(transform, 'return NPC.getNewNPCName(npc.type);', 'TransformableNPCProfile second name roll')

    require(town_profiles, '"Siamese", "Black", "OrangeTabby", "RussianBlue", "Silver", "White"', 'cat variant order')
    require(town_profiles, '"Labrador", "PitBull", "Beagle", "Corgi", "Dalmation", "Husky"', 'dog variant order')
    require(town_profiles, '"White", "Angora", "Dutch", "Flemish", "Lop", "Silver"', 'bunny variant order')
    for npc_type in ('17', '142', '633', '670', '684'):
        require(town_profiles, '{\n\t\t\t' + npc_type + ',', f'TownNPCProfiles membership {npc_type}')

    spawn = extract_method(worldgen, 'public static TownNPCSpawnResult SpawnTownNPC(int x, int y, bool canSpawnNewTownNPC = true)')
    require_ordered(spawn, (
        'NPC.NewNPC(NPC.GetSpawnSourceForTownSpawn(), num5 * 16, num6 * 16, num, 1)',
        'Main.npc[num9].netUpdate = true',
        'ChatHelper.BroadcastChatMessage(NetworkText.FromKey("Announcement.HasArrived", Main.npc[num9].GetFullNetName()), ChatColors.NPCTravel)',
    ), 'SpawnTownNPC arrival announcement')

    full_name = extract_method(npc, 'public NetworkText GetFullNetName()')
    require_ordered(full_name, (
        'if (!HasGivenName)',
        'return GetTypeNetName()',
        'NetworkText.FromKey("Game.NPCTitle", GetGivenNetName(), GetTypeNetName())',
    ), 'NPC.GetFullNetName')
    require(npc, 'return NetworkText.FromKey(Lang.GetNPCName(netID).Key);', 'NPC type NetworkText localization')
    require(chat, 'BroadcastChatMessageAs(byte.MaxValue, text, color, excludedPlayer);', 'server chat author 255')
    verify_runtime_name_catalog(runtime_identity, args.town_json)

    print('town arrival identity source contract: OK')

if __name__ == '__main__':
    main()
