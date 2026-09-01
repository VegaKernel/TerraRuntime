# Town NPC move-in and home schedule

TerraRuntime owns a source-backed TerrariaServer 1.4.5.8 projection of the town-NPC move-in eligibility pass and carries eligible residents through an authoritative room/materialization path.

The eligibility evaluator covers the vanilla `Main.UpdateTime_SpawnTownNPCs` candidate flags and the `7200 / WorldGen.GetWorldUpdateRate()` cadence boundary. It consumes authoritative player facts rather than transport packets: aggregate coin value, maximum life, and source-pinned inventory predicates for the Arms Dealer, Demolitionist, and Dye Trader. Persisted rescue and unlock state for the supported candidate set survives both `.wld` parsing and the disposable prepared-world cache.

## Room discovery and move-in

`RuntimeTownHouseCandidateIndex1458` scans the world incrementally under a fixed tile budget. Only tile identities participating in the pinned housing `RoomNeeds` sets invoke the expensive room validator. Candidate rooms are deduplicated by their canonical home tile and are always revalidated against the requested NPC type and the live occupant set before use. A room broken after discovery therefore fails closed rather than remaining a stale spawn target.

On the source cadence, `RuntimeTownNpcMoveInCoordinator1458` evaluates the active authoritative player/inventory state, chooses the first currently eligible type for which a live valid room exists, allocates a generation-safe NPC slot, commits the resident into `RuntimeTownNpcStateStore`, and publishes packet 23 plus packet 60 through the existing NPC replication owner. Runtime-created residents are included in subsequent `.wld` NPC/TownRoom save snapshots.

The mutable roster no longer assumes that all town residents occupied slots `0..N-1` forever. New residents use the first free vanilla NPC slot, so a live hostile NPC cannot be overwritten by a town move-in.

## Home return, resting, and chairs

`RuntimeTownNpcSchedule1458` implements the verified server-side shelter/resting slice of AI_007. Night, rain, eclipse, Slime Rain, or the supplied storm-above-surface condition request a return home. The pinned night `ai[0] == 5` tolerance of seven tiles is preserved, while the water-sensitive town entities 361/445/687 fail the ordinary resting-position check while wet.

The home-floor probe now follows `SolidOrSlopedTileOrPlatform`: ordinary non-solid-top solid tiles and the pinned vanilla platform set can anchor a resting position. At night the runtime searches for an NPC-sittable chair within the source radii of seven tiles horizontally, six upward, and two downward with the two-tile vertical step. Chair types 15 and 497 use the same frame normalization as the official server, and a chair already occupied by another sitting town NPC is not selected.

When a resident reaches the selected resting tile, horizontal velocity settles toward zero in source-sized `0.1f` steps. A valid unoccupied chair then commits the vanilla forced-sitting transition: `ai[0] = 5`, `ai[1] = 900 + rand(10800)`, direction from the chair frame, zero velocity, the pinned bottom anchor, and `localAI[3] = 0`. The forbidden type-15 frame range `1080..1098` is preserved. Town Dog, Town Bunny, and all eight town-slime types remain excluded from this sitting path.

When a resident is outside the resting area, both the current and destination screen-sized safety rectangles must be clear of active players before a server teleport is allowed. The destination probes `homeX`, `homeX - 1`, then `homeX + 1`, preserves the Old Man obstruction exception, commits the pinned `homeFloorY * 16 - height - 0.1f` Y anchor, and immediately performs the source post-teleport sitting attempt.

## Deliberate remaining gaps

This is still not a full AI_007 claim. Exact randomized `WorldGen` house-priority/fallback placement, localized `Announcement.HasArrived` transport, NPC given-name generation, pet idle animations, social/emote/combat branches, and live weather/eclipse/invasion mutation ownership remain separate work. The current host can project persisted rain/eclipse/invasion state into the schedule, while future authoritative event systems must replace those initial facts when they become mutable at runtime. Bestiary-driven Zoologist and random Party Girl first-unlock inputs also remain fail-closed until those live sources are owned.
