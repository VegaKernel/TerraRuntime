#!/usr/bin/env python3
from __future__ import annotations
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text)


def require(text: str, fragment: str, label: str) -> None:
    if fragment not in text:
        raise SystemExit(f"missing pinned Town NPC spawn-preselection contract: {label}: {fragment}")


def require_ordered(text: str, fragments: tuple[str, ...], label: str) -> None:
    position = -1
    for fragment in fragments:
        next_position = text.find(fragment, position + 1)
        if next_position < 0:
            raise SystemExit(f"missing ordered Town NPC spawn-preselection contract: {label}: {fragment}")
        position = next_position


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f"missing pinned method: {signature}")
    brace = text.find("{", start)
    if brace < 0:
        raise SystemExit(f"missing method body: {signature}")
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    raise SystemExit(f"unterminated pinned method: {signature}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--worldgen", required=True)
    parser.add_argument("--town-room-manager", required=True)
    args = parser.parse_args()

    worldgen = compact(Path(args.worldgen).read_text(encoding="utf-8"))
    town_rooms = compact(Path(args.town_room_manager).read_text(encoding="utf-8"))

    spawn = extract_method(
        worldgen,
        "public static TownNPCSpawnResult SpawnTownNPC(int x, int y, bool canSpawnNewTownNPC = true)")
    require_ordered(spawn, (
        "int num = prioritizedTownNPCType",
        "FindNPCLookingForHomeThatCanMoveIn(num)",
        "ScoreRoom(-1, num)",
        "if (hiScore <= 0)",
        "num = IsThereASpawnablePrioritizedTownNPC(bestX, bestY)",
        "if (TownManager.HasRoom(num, out var roomPosition2) && !currentlyTryingToUseAlternateHousingSpot)",
        "int num3 = bestX",
        "int num4 = bestY",
        "currentlyTryingToUseAlternateHousingSpot = true",
        "SpawnTownNPC(roomPosition2.X, roomPosition2.Y - 2)",
        "currentlyTryingToUseAlternateHousingSpot = false",
        "bestX = num3",
        "bestY = num4",
        "townNPCSpawnResult == TownNPCSpawnResult.Successful",
        "int num5 = bestX",
        "int num6 = bestY",
        "int npcTypeToSpawn = prioritizedTownNPCType",
        "IsRoomConsideredAlreadyOccupied(num5, num6, npcTypeToSpawn)",
        "return TownNPCSpawnResult.BlockedInfiHousing",
        "NPC.NewNPC(NPC.GetSpawnSourceForTownSpawn(), num5 * 16, num6 * 16, num, 1)"
    ), "SpawnTownNPC pre-materialization ordering")

    selector = extract_method(worldgen, "public static int IsThereASpawnablePrioritizedTownNPC(int x, int y)")
    require_ordered(selector, (
        "TownManager.AddOccupantsToList(x, y, list)",
        "for (int i = 0; i < list.Count; i++)",
        "return num",
        "for (int j = 0; j < NPCID.Count; j++)",
        "TownManager.HasRoomQuick(j)",
        "NPCID.Sets.IsTownPet[j]",
        "j == prioritizedTownNPCType",
        "return result"
    ), "room occupant, assigned-room, town-pet and prioritized fallback order")

    occupied = extract_method(
        worldgen,
        "private static bool IsRoomConsideredAlreadyOccupied(int spawnTileX, int spawnTileY, int npcTypeToSpawn)")
    require_ordered(occupied, (
        "nPC.active && nPC.townNPC && !nPC.homeless",
        "nPC.homeTileX == spawnTileX",
        "nPC.homeTileY == spawnTileY",
        "!TownManager.CanNPCsLiveWithEachOther(npcTypeToSpawn, nPC)",
        "result = true",
        "break"
    ), "final exact-home occupancy gate")

    share = extract_method(town_rooms, "public bool CanNPCsLiveWithEachOther(NPC npc1, NPC npc2)")
    require(share, "return npc1.housingCategory != npc2.housingCategory", "housing-category compatibility")

    print("town SpawnTownNPC preselection source contract: OK")


if __name__ == "__main__":
    main()
