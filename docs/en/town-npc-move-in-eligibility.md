# Town NPC move-in and home schedule

TerraRuntime owns a source-backed TerrariaServer 1.4.5.8 projection of the town-NPC move-in eligibility pass and carries eligible residents through an authoritative room/materialization path.

The eligibility evaluator covers the vanilla `Main.UpdateTime_SpawnTownNPCs` candidate flags and the `7200 / WorldGen.GetWorldUpdateRate()` cadence boundary. It consumes authoritative player facts rather than transport packets: aggregate coin value, maximum life, and source-pinned inventory predicates for the Arms Dealer, Demolitionist, and Dye Trader. Persisted rescue and unlock state for the supported candidate set survives both `.wld` parsing and the disposable prepared-world cache.

## Eligibility and exact source priority

Terraria does not select a resident by simply taking the first true `townNPCCanSpawn` flag. The same update pass independently computes `WorldGen.prioritizedTownNPCType`, and that ordering is now represented explicitly by `VanillaTownSpawnEligibility1458.PrioritizedType`. The prioritized type is moved to the front of the runtime candidate projection before room selection, while `CanSpawn` continues to expose the complete eligible set.

The 1.4.5.8 priority chain is source-pinned, including the seed-specific Dryad/Zoologist overrides, Guide/Merchant/Nurse progression, rescued residents, boss/progression residents, Princess, the non-numeric town-slime order, and Bunny/Cat/Dog tail ordering. Eligibility and priority remain deliberately separate where vanilla separates them: for example, the Tenth Anniversary seed can make Steampunker eligible, but Steampunker becomes `prioritizedTownNPCType` only after a mechanical boss has been defeated.

## Room discovery, relocation, and move-in

`RuntimeTownHouseCandidateIndex1458` scans the world incrementally under a fixed tile budget. Only tile identities participating in the pinned housing `RoomNeeds` sets invoke the expensive room validator. Candidate rooms are deduplicated by their canonical home tile and are always revalidated against the requested NPC type and the live occupant set before use. A room broken after discovery therefore fails closed rather than remaining a stale spawn target.

Existing homeless residents are processed before a new resident is materialized, matching the `WorldGen.UpdatePrioritizedTownNPC` relocation priority. A player kick-out starts the pinned 3600-tick look-for-home timeout. When relocation is allowed, a valid `TownManager`-assigned room gets the first attempt and the discovered-room index is the fallback.

`RuntimeTownNpcStateStore` now preserves the gameplay-visible `TownRoomManager` pair order instead of sorting rooms by NPC type. Loaded pairs stay in `.wld` order, `SetRoom` semantics remove the previous pair and append its replacement, and kick-out removes the pair. The same ordered view feeds the source `AddOccupantsToList` behavior.

For each tested room candidate, new-resident selection follows `IsThereASpawnablePrioritizedTownNPC`: eligible occupants already assigned to that exact room are considered first in TownRoomManager order; the numeric NPC-ID scan then prefers an eligible type that has any assigned room, then a town pet, and only then retains `prioritizedTownNPCType` as the fallback. If the selected type has its own assigned room, that room receives the source recursive first attempt before the currently tested candidate.

On the source cadence, `RuntimeTownNpcMoveInCoordinator1458` evaluates the active authoritative player/inventory state, applies the room-aware source selection above, allocates a generation-safe NPC slot when a valid room exists, commits the resident into `RuntimeTownNpcStateStore`, and publishes packet 23 plus packet 60 through the existing NPC replication owner. Runtime-created residents are included in subsequent `.wld` NPC/TownRoom save snapshots.

Physical materialization now follows the final `WorldGen.SpawnTownNPC` search rather than placing a new resident directly on the home resting anchor. The canonical home floor is first tested against Terraria's `1920x1200` server-screen rectangle expanded by `safeRangeX=62` and `safeRangeY=39`. If a surface home is inside an active player's rectangle, the search expands from radius 1 through 499, checks the right column before the left, clamps vertical scanning to tile 10 and `worldSurface`, requires an active `Main.tileSolid` floor plus a three-tile `Collision.SolidTiles` clearance, and re-applies the player rectangle at the candidate. The source's unusual exhausted-search behavior is preserved: lack of a safe point does not cancel `NPC.NewNPC`; the final scanned coordinates remain the materialization point. Underground homes skip this fallback search.

The NPC is materialized from the exact `NPC.NewNPC` bottom-center anchor (`tileX*16`, `tileY*16`) after its source-backed dimensions are known. A spawn left of home faces right, a spawn right of home faces left, and an equal-X spawn retains the zero construction direction. The home room remains the validated `bestX/bestY`, independent of the physical spawn point.

The mutable roster no longer assumes that all town residents occupied slots `0..N-1` forever. New residents use the first free vanilla NPC slot, so a live hostile NPC cannot be overwritten by a town move-in.

## Home return, resting, and chairs

`RuntimeTownNpcSchedule1458` implements the verified server-side shelter/resting slice of AI_007. Night, rain, eclipse, Slime Rain, or the supplied storm-above-surface condition request a return home. The pinned night `ai[0] == 5` tolerance of seven tiles is preserved, while the water-sensitive town entities 361/445/687 fail the ordinary resting-position check while wet.

The home-floor probe follows `SolidOrSlopedTileOrPlatform`: ordinary non-solid-top solid tiles and the pinned vanilla platform set can anchor a resting position. At night the runtime searches for an NPC-sittable chair within the source radii of seven tiles horizontally, six upward, and two downward with the two-tile vertical step. Chair types 15 and 497 use the same frame normalization as the official server, and a chair already occupied by another sitting town NPC is not selected.

When a resident reaches the selected resting tile, horizontal velocity settles toward zero in source-sized `0.1f` steps. A valid unoccupied chair then commits the vanilla forced-sitting transition: `ai[0] = 5`, `ai[1] = 900 + rand(10800)`, direction from the chair frame, zero velocity, the pinned bottom anchor, and `localAI[3] = 0`. The forbidden type-15 frame range `1080..1098` is preserved. Town Dog, Town Bunny, and all eight town-slime types remain excluded from this sitting path.

When a resident is outside the resting area, both the current and destination screen-sized safety rectangles must be clear of active players before a server teleport is allowed. The destination probes `homeX`, `homeX - 1`, then `homeX + 1`, preserves the Old Man obstruction exception, commits the pinned `homeFloorY * 16 - height - 0.1f` Y anchor, and immediately performs the source post-teleport sitting attempt.

## Deliberate remaining gaps

The move-in path still does not claim every `WorldGen.SpawnTownNPC` detail. Exact broader WorldGen house-candidate/random fallback behavior, localized `Announcement.HasArrived` transport, and NPC given-name/variation generation remain separate work. AI_007 still has pet idle animation, social/emote/combat branches, and live weather/eclipse/invasion mutation ownership outside this slice. Bestiary-driven live Zoologist updates and the random Party Girl roll also remain fail-closed until those mutable runtime sources are owned.
