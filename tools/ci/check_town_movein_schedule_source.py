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


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--worldgen", required=True)
    parser.add_argument("--npc", required=True)
    args = parser.parse_args()
    worldgen = compact(Path(args.worldgen).read_text(encoding="utf-8"))
    npc = compact(Path(args.npc).read_text(encoding="utf-8"))

    require(worldgen,
        "Main.eclipse || !Main.dayTime || (Main.invasionType > 0 && Main.invasionDelay == 0 && Main.invasionSize > 0) || prioritizedTownNPCType == 0 || homelessSpawnTimeout > 0",
        "SpawnHomelessNPC day/eclipse/invasion gate")
    require(worldgen, "homelessSpawnTimeout = 54000", "SpawnHomelessNPC timeout")
    require(worldgen, "FindNPCLookingForHomeThatCanMoveIn(num)", "existing homeless resident relocation")
    require(worldgen, "TownManager.HasRoom(num, out roomPosition)", "town pet assigned-room lookup")
    require(worldgen, "IsRoomConsideredAlreadyOccupied(num5, num6, npcTypeToSpawn)", "room occupancy gate")
    require(worldgen, "TownManager.CanNPCsLiveWithEachOther(npcTypeToSpawn, nPC)", "housing-category sharing gate")

    require(npc, "bool flag = Main.raining", "AI_007 rain return-home flag")
    require(npc, "if (!Main.dayTime) { flag = true; }", "AI_007 night return-home flag")
    require(npc, "if (Main.eclipse) { flag = true; }", "AI_007 eclipse return-home flag")
    require(npc, "if (Main.slimeRain) { flag = true; }", "AI_007 slime-rain return-home flag")
    require(npc, "Math.Abs(tileX - idealRestX) <= 7", "AI_007 night sitting tolerance X")
    require(npc, "Math.Abs(tileY - idealRestY) <= 7", "AI_007 night sitting tolerance Y")
    require(npc, "AI_007_TownEntities_TeleportToHome(floorX, floorY)", "AI_007 server home teleport")
    require(npc, "position.Y = (float)(homeFloorY * 16 - height) - 0.1f", "AI_007 home teleport Y")

    print("town move-in/schedule source contract: OK")


if __name__ == "__main__":
    main()
