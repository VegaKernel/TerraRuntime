# Town NPC SpawnTownNPC preselection

TerraRuntime mirrors the source-visible pre-materialization control flow of `WorldGen.SpawnTownNPC` from TerrariaServer 1.4.5.8 before the already-owned physical spawn-point search begins.

## Prioritized room scoring before resident selection

Vanilla does not validate a candidate room for whichever NPC is eventually selected. `SpawnTownNPC` starts with `prioritizedTownNPCType`, runs `StartRoomCheck`, `RoomNeeds`, stinkbug handling and `ScoreRoom(-1, prioritizedTownNPCType)`, and only after a positive `hiScore` calls `IsThereASpawnablePrioritizedTownNPC(bestX, bestY)`.

`RuntimeTownNpcMoveInCoordinator1458` now preserves that ordering. Every candidate is first revalidated against the global `VanillaTownSpawnEligibility1458.PrioritizedType`. The room-aware selector then consumes the **scored** home tile returned by that validation for `TownRoomManager.AddOccupantsToList` semantics instead of using the candidate index's cached discovery-time home tile.

## Guarded assigned-room recursion

If the selected type has a `TownManager` room, vanilla recursively calls `SpawnTownNPC(room.X, room.Y - 2)` while `currentlyTryingToUseAlternateHousingSpot` is set. That recursive call reruns room scoring and `IsThereASpawnablePrioritizedTownNPC`; it is not a forced spawn of the type that triggered the recursion. Therefore the assigned room can legitimately select another eligible room occupant.

The runtime now models this as a one-level guarded recursive candidate resolution. Only a successful recursive selection replaces the outer candidate. A blocked alternate-room attempt falls back to the original tested room, matching the source control flow. The recursive room is validated from the source seed `room.Y - 2`; it is not required to reproduce the stale cached canonical home exactly.

## Final exact-home occupancy gate

Immediately before physical materialization, vanilla calls `IsRoomConsideredAlreadyOccupied(bestX, bestY, prioritizedTownNPCType)`. The compatibility query intentionally uses the global prioritized type even when the actual selected spawn type differs. It only considers active, housed Town NPCs whose `homeTileX/homeTileY` exactly equal the scored spawn home and blocks when `TownRoomManager.CanNPCsLiveWithEachOther` reports the same housing category.

TerraRuntime preserves that quirk explicitly instead of normalizing it to selected-type compatibility.

## Verification and remaining boundary

Focused regressions cover recursive assigned-room reselection and the final prioritized-category occupancy gate. A dedicated CI source contract pins the exact `SpawnTownNPC` ordering, `IsThereASpawnablePrioritizedTownNPC`, `IsRoomConsideredAlreadyOccupied`, and `TownRoomManager.CanNPCsLiveWithEachOther` against the official 1.4.5.8 server assembly.

This slice does **not** claim exact production of the initial house probe coordinates. TerraRuntime still uses its bounded house-candidate index rather than reproducing the full `UpdateWorld` random `TrySpawningTownNPC` stream, `CheckForHousesNearAPlayer` 300-point sampling, and `SpawnHomelessNPC`/`LastFoundHouse` random fallback. Those remain the next WorldGen/Town integration boundary.
