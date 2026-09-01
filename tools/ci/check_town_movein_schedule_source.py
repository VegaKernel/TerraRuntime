#!/usr/bin/env python3
from __future__ import annotations
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def require(text: str, fragment: str, label: str) -> None:
    if fragment not in text:
        raise SystemExit(f"missing pinned town source contract: {label}")


def require_ordered(text: str, fragments: tuple[str, ...], label: str) -> None:
    position = -1
    for fragment in fragments:
        next_position = text.find(fragment, position + 1)
        if next_position < 0:
            raise SystemExit(f"missing pinned town source contract: {label}: {fragment}")
        if next_position <= position:
            raise SystemExit(f"town source contract order drifted: {label}: {fragment}")
        position = next_position


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f"missing pinned town source method: {signature}")
    brace = text.find("{", start)
    if brace < 0:
        raise SystemExit(f"missing body for pinned town source method: {signature}")
    depth = 0
    for index in range(brace, len(text)):
        ch = text[index]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    raise SystemExit(f"unterminated pinned town source method: {signature}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--worldgen", required=True)
    parser.add_argument("--npc", required=True)
    parser.add_argument("--town-room-manager", required=True)
    parser.add_argument("--npc-id", required=True)
    args = parser.parse_args()
    worldgen = compact(Path(args.worldgen).read_text(encoding="utf-8"))
    npc = compact(Path(args.npc).read_text(encoding="utf-8"))
    town_rooms = compact(Path(args.town_room_manager).read_text(encoding="utf-8"))
    npc_id = compact(Path(args.npc_id).read_text(encoding="utf-8"))

    require(worldgen,
        "Main.eclipse || !Main.dayTime || (Main.invasionType > 0 && Main.invasionDelay == 0 && Main.invasionSize > 0) || prioritizedTownNPCType == 0 || homelessSpawnTimeout > 0",
        "SpawnHomelessNPC day/eclipse/invasion gate")
    require(worldgen, "homelessSpawnTimeout = 54000", "SpawnHomelessNPC timeout")
    require(worldgen, "FindNPCLookingForHomeThatCanMoveIn(num)", "existing homeless resident relocation")
    require(worldgen, "Main.npc[i].homeless && Main.npc[i].townNPC && Main.npc[i].lookForHomeTimeout == 0", "homeless resident priority eligibility")
    require(worldgen, "Main.npc[n].lookForHomeTimeout = NPC.KickOutLookForHomeTimeout", "manual kick-out look-for-home timeout")
    require(worldgen, "Main.npc[num2].homeless = false", "homeless resident relocation clears homeless")
    require(worldgen, "Main.npc[num2].homelessDespawn = false", "homeless resident relocation clears despawn flag")
    require(worldgen, "TownManager.HasRoom(num, out roomPosition)", "town pet assigned-room lookup")
    require(worldgen, "TownManager.HasRoom(num, out var roomPosition2) && !currentlyTryingToUseAlternateHousingSpot", "assigned-room priority before fallback housing")
    require(worldgen, "SpawnTownNPC(roomPosition2.X, roomPosition2.Y - 2)", "assigned-room recursive seed offset")
    require(worldgen, "IsRoomConsideredAlreadyOccupied(num5, num6, npcTypeToSpawn)", "room occupancy gate")
    require(worldgen, "TownManager.CanNPCsLiveWithEachOther(npcTypeToSpawn, nPC)", "housing-category sharing gate")

    spawn_town = extract_method(worldgen, "public static TownNPCSpawnResult SpawnTownNPC(int x, int y, bool canSpawnNewTownNPC = true)")
    require_ordered(spawn_town, (
        "int num5 = bestX",
        "int num6 = bestY",
        "flag2 = true",
        "NPC.sWidth + NPC.safeRangeX * 2",
        "NPC.sHeight + NPC.safeRangeY * 2",
        "for (int i = 0; i < 255; i++)",
        "Main.player[i].active",
        "Main.player[i].position.X",
        "Intersects(",
        "if (!flag2 && !((double)num6 > Main.worldSurface))",
        "for (int j = 1; j < 500; j++)",
        "num5 = ((k != 0) ? (bestX - j) : (bestX + j))",
        "num5 > 10 && num5 < Main.maxTilesX - 10",
        "int num7 = bestY - j",
        "double num8 = bestY + j",
        "num7 = 10",
        "num8 = Main.worldSurface",
        "Main.tile[num5, num6].nactive()",
        "Main.tileSolid[Main.tile[num5, num6].type]",
        "Collision.SolidTiles(num5 - 1, num5 + 1, num6 - 3, num6 - 1)",
        "flag2 = true",
        "NPC.sWidth + NPC.safeRangeX * 2",
        "NPC.sHeight + NPC.safeRangeY * 2",
        "for (int m = 0; m < 255; m++)",
        "Main.player[m].active",
        "Main.player[m].position.X",
        "Intersects(",
        "NPC.NewNPC(NPC.GetSpawnSourceForTownSpawn(), num5 * 16, num6 * 16, num, 1)",
        "if (num5 < bestX)",
        "Main.npc[num9].direction = 1",
        "else if (num5 > bestX)",
        "Main.npc[num9].direction = -1"
    ), "SpawnTownNPC physical spawn search and facing")

    selector = extract_method(worldgen, "public static int IsThereASpawnablePrioritizedTownNPC(int x, int y)")
    require_ordered(selector, (
        "TownManager.AddOccupantsToList(x, y, list)",
        "for (int i = 0; i < list.Count; i++)",
        "Main.townNPCCanSpawn[num] && !NPC.AnyNPCs(num) && CheckSpecialTownNPCSpawningConditions(num)",
        "for (int j = 0; j < NPCID.Count; j++)",
        "TownManager.HasRoomQuick(j)",
        "NPCID.Sets.IsTownPet[j]",
        "j == prioritizedTownNPCType",
        "return result"
    ), "SpawnTownNPC room-aware candidate precedence")

    add_occupants = extract_method(town_rooms, "public void AddOccupantsToList(Point tilePosition, List<int> occupants)")
    require_ordered(add_occupants, (
        "foreach (Tuple<int, Point> roomLocationPair in _roomLocationPairs)",
        "roomLocationPair.Item2 == tilePosition",
        "occupants.Add(roomLocationPair.Item1)"
    ), "TownRoomManager AddOccupantsToList insertion order")

    set_room = extract_method(town_rooms, "public void SetRoom(int npcID, Point pt)")
    require_ordered(set_room, (
        "_roomLocationPairs.RemoveAll((Tuple<int, Point> x) => x.Item1 == npcID)",
        "_roomLocationPairs.Add("
    ), "TownRoomManager SetRoom remove-then-append")

    kick_out = extract_method(town_rooms, "public void KickOut(int npcType)")
    require(kick_out,
        "_roomLocationPairs.RemoveAll((Tuple<int, Point> x) => x.Item1 == npcType)",
        "TownRoomManager KickOut removes ordered room pair")

    save_rooms = extract_method(town_rooms, "public void Save(BinaryWriter writer)")
    require_ordered(save_rooms, (
        "writer.Write(_roomLocationPairs.Count)",
        "foreach (Tuple<int, Point> roomLocationPair in _roomLocationPairs)",
        "writer.Write(roomLocationPair.Item1)",
        "writer.Write(roomLocationPair.Item2.X)",
        "writer.Write(roomLocationPair.Item2.Y)"
    ), "TownRoomManager save preserves pair order")

    load_rooms = extract_method(town_rooms, "public void Load(BinaryReader reader)")
    require_ordered(load_rooms, (
        "Clear()",
        "int num = reader.ReadInt32()",
        "_roomLocationPairs.Add(",
        "_hasRoom[num2] = true"
    ), "TownRoomManager load preserves serialized pair order")

    town_pet_members = ("637", "638", "656", "670", "678", "679", "680", "681", "682", "683", "684")
    town_pet_initializer = ""
    search_from = 0
    while True:
        town_pet_start = npc_id.find("IsTownPet", search_from)
        if town_pet_start < 0:
            break
        candidate = npc_id[town_pet_start:town_pet_start + 1000]
        if "CreateBoolSet" in candidate and all(member in candidate for member in town_pet_members):
            town_pet_initializer = candidate
            break
        search_from = town_pet_start + len("IsTownPet")
    if not town_pet_initializer:
        raise SystemExit("missing pinned town source contract: NPCID town-pet bool-set initializer")
    require_ordered(town_pet_initializer,
        town_pet_members,
        "NPCID town-pet membership used by room-aware selector")

    require(npc, "public static readonly int KickOutLookForHomeTimeout = 3600", "NPC kick-out look-for-home timeout constant")
    require(npc, "public static int safeRangeX = (int)((double)(sWidth / 16) * 0.52)", "NPC safeRangeX formula")
    require(npc, "public static int safeRangeY = (int)((double)(sHeight / 16) * 0.52)", "NPC safeRangeY formula")
    require(npc, "public static int sWidth => 1920", "NPC server screen width")
    require(npc, "public static int sHeight => 1200", "NPC server screen height")
    new_npc = extract_method(npc, "public static int NewNPC(IEntitySource source, int X, int Y, int Type")
    require_ordered(new_npc, (
        "nPC.SetDefaults(Type)",
        "GiveTownUniqueDataToNPCsThatNeedIt(Type, availableNPCSlot)",
        "nPC.Bottom",
        "new Vector2(",
        "nPC.active = true"
    ), "NPC.NewNPC bottom-center materialization")

    require(npc, "bool flag = Main.raining", "AI_007 rain return-home flag")
    require(npc, "if (!Main.dayTime) { flag = true; }", "AI_007 night return-home flag")
    require(npc, "if (Main.eclipse) { flag = true; }", "AI_007 eclipse return-home flag")
    require(npc, "if (Main.slimeRain) { flag = true; }", "AI_007 slime-rain return-home flag")
    require(npc, "Math.Abs(tileX - idealRestX) <= 7", "AI_007 night sitting tolerance X")
    require(npc, "Math.Abs(tileY - idealRestY) <= 7", "AI_007 night sitting tolerance Y")
    require(npc, "(type == 361 || type == 445 || type == 687) && wet", "AI_007 wet resting exclusion")
    require(npc, "TileID.Sets.Platforms[tile.type]", "AI_007 resting floor accepts platforms")
    require(npc, "int num2 = 7", "AI_007 chair search horizontal radius")
    require(npc, "int num3 = 6", "AI_007 chair search upward radius")
    require(npc, "int num4 = 2", "AI_007 chair search downward radius")
    require(npc, "int num6 = 2", "AI_007 chair search vertical step")
    require(npc, "TileID.Sets.CanBeSatOnForNPCs[tile.type]", "AI_007 NPC chair set")
    require(npc, "tile2.type == 497 || tile2.type == 15", "AI_007 chair coordinate normalization")
    require(npc, "Main.npc[j].ai[0] == 5f", "AI_007 occupied chair exclusion")
    require(npc, "tile.type != 15 || tile.frameY < 1080 || tile.frameY > 1098", "AI_007 forbidden chair style")
    require(npc, "ai[0] = 5f", "AI_007 forced sitting state")
    require(npc, "ai[1] = 900 + Main.rand.Next(10800)", "AI_007 forced sitting timer")
    require(npc, "direction = ((tile.frameX != 0) ? 1 : (-1))", "AI_007 forced sitting direction")
    sitting = extract_method(npc, "private void AI_007_TryForcingSitting(int homeFloorX, int homeFloorY)")
    require_ordered(sitting, (
        "ai[0] = 5f",
        "ai[1] = 900 + Main.rand.Next(10800)",
        "direction =",
        "base.Bottom",
        "new Vector2(",
        "homeFloorX",
        "homeFloorY",
        "velocity = Vector2.Zero",
        "localAI[3] = 0f"
    ), "AI_007 forced sitting anchor and reset")
    require(npc, "velocity = Vector2.Zero", "AI_007 forced sitting velocity reset")
    require(npc, "localAI[3] = 0f", "AI_007 forced sitting local timer reset")
    require(npc, "if (velocity.X > 0.1f) { velocity.X -= 0.1f; } else if (velocity.X < -0.1f) { velocity.X += 0.1f; }", "AI_007 home horizontal settling")
    require(npc, "AI_007_TownEntities_TeleportToHome(floorX, floorY)", "AI_007 server home teleport")
    teleport_home = extract_method(npc, "private void AI_007_TownEntities_TeleportToHome(int homeFloorX, int homeFloorY)")
    require_ordered(teleport_home, (
        "homeFloorX",
        "homeFloorY * 16 - height",
        "0.1f",
        "netUpdate = true",
        "AI_007_TryForcingSitting(homeFloorX, homeFloorY)"
    ), "AI_007 home teleport bottom anchor")
    require(npc, "if (type == 37 || !Collision.SolidTiles(num - 1, num + 1, homeFloorY - 3, homeFloorY - 1))", "AI_007 Old Man teleport obstruction exception")
    require(npc, "AI_007_TryForcingSitting(homeFloorX, homeFloorY)", "AI_007 post-teleport sitting attempt")

    print("town move-in/schedule source contract: OK")


if __name__ == "__main__":
    main()
