# Town NPC move-in and home schedule

TerraRuntime owns a source-backed TerrariaServer 1.4.5.8 projection of the town-NPC move-in eligibility pass and now carries eligible residents through an authoritative room/materialization path.

The eligibility evaluator covers the vanilla `Main.UpdateTime_SpawnTownNPCs` candidate flags and the `7200 / WorldGen.GetWorldUpdateRate()` cadence boundary. It consumes authoritative player facts rather than transport packets: aggregate coin value, maximum life, and source-pinned inventory predicates for the Arms Dealer, Demolitionist, and Dye Trader. Persisted rescue and unlock state for the supported candidate set survives both `.wld` parsing and the disposable prepared-world cache.

## Room discovery and move-in

`RuntimeTownHouseCandidateIndex1458` scans the world incrementally under a fixed tile budget. Only tile identities participating in the pinned housing `RoomNeeds` sets invoke the expensive room validator. Candidate rooms are deduplicated by their canonical home tile and are always revalidated against the requested NPC type and the live occupant set before use. A room broken after discovery therefore fails closed rather than remaining a stale spawn target.

On the source cadence, `RuntimeTownNpcMoveInCoordinator1458` evaluates the active authoritative player/inventory state, chooses the first currently eligible type for which a live valid room exists, allocates a generation-safe NPC slot, commits the resident into `RuntimeTownNpcStateStore`, and publishes packet 23 plus packet 60 through the existing NPC replication owner. Runtime-created residents are included in subsequent `.wld` NPC/TownRoom save snapshots.

The mutable roster no longer assumes that all town residents occupied slots `0..N-1` forever. New residents use the first free vanilla NPC slot, so a live hostile NPC cannot be overwritten by a town move-in.

## Home-return schedule

`RuntimeTownNpcSchedule1458` implements the verified server-side AI_007 shelter slice. Night, rain, eclipse, Slime Rain, or the supplied storm-above-surface condition request a return home. The pinned night `ai[0] == 5` resting tolerance of seven tiles is preserved. When a resident is outside the resting area, the runtime performs the same broad safety policy as the official server: both the current and destination screen-sized safety rectangles must be clear of active players before a server teleport is allowed. The destination probes `homeX`, `homeX - 1`, then `homeX + 1`, and the committed position uses the pinned `homeFloorY * 16 - height - 0.1f` anchor.

## Deliberate remaining gaps

This is not a full AI_007 claim. Exact randomized `WorldGen` house-priority/fallback placement, localized `Announcement.HasArrived` transport, NPC given-name generation, chair selection/sitting animation, social/emote/combat branches, and live weather/eclipse/invasion mutation ownership remain separate work. The current host can project persisted rain/eclipse/invasion state into the schedule, while future authoritative event systems must replace those initial facts when they become mutable at runtime. Bestiary-driven Zoologist and random Party Girl first-unlock inputs also remain fail-closed until those live sources are owned.
