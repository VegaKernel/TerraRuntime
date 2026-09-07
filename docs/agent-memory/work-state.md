# Work state

Updated: 2026-09-07.

This is the resume point for the next agent/session. Read it before reconstructing project state from source.

## Latest checkpoint / base provenance

- Latest clean WIP checkpoint: `/TZ/TerraRuntime-main-TZ-34-WIP.zip`. It is derived from the `TerraRuntime-main.zip` attached in the 2026-09-07 continuation session, which the user explicitly designated as the latest project base, through the source-correct TZ-32/TZ-33 checkpoints. Do not reconstruct this state by starting from TZ-29/30/31 library archives.
- Latest released clean checkpoint remains `/TZ/TerraRuntime-main-TZ-29.zip`; TZ-34 is still WIP because the external official-client and NativeAOT evidence gates listed below are unavailable in this environment.
- The attached base already contained the accumulated TZ-32 live-stability, bot and vanilla-worldgen work described below. TZ-32 added source-correct packet-50 player-buff presentation synchronization; TZ-33 closed admitted controlled-magic local-immunity/reacquisition; TZ-34 adds the admitted source-backed projectile-specific PvP status/packet-55 path. Hostile PvE projectile-debuff authority remains open.

## TZ-30 stabilization / network acceptance in progress

- Checkpoint: `/TZ/TerraRuntime-main-TZ-30-WIP.zip` (derived from `/TZ/TerraRuntime-main-TZ-29.zip`). Do not call TZ-30 released until the production NativeAOT gates are exercised.
- `RuntimeBotNetworkAcceptanceTests` now composes the real connection, server-player, projectile, world-item and NPC replication registries around one `ServerRuntimeState` rather than asserting only bot-internal snapshots.
- The acceptance set is 9/9 green in Release. It covers PlayerBot observer baseline, packet `30` PvP mirroring, target disconnect reset, Guard projectile ownership plus ammo consumption, exact-hitbox item removal plus inventory update, NpcBot spawn/despawn, PlayerBot/NpcBot body replacement, and late-join baselines for both body kinds. The complete Release runner discovers 2530 test cases and exits 0 in the normal parallel profile; the recorded wall time for this pass was 22.42 s with about 2.26 GiB peak RSS.
- TerrariaServer 1.4.5.8 re-check corrected the previous resume note about packet `55`: `AddPlayerBuffPvP` is targeted local-player PvP buff delivery. A client applies it only when the encoded slot is `Main.myPlayer`; it is not a remote fake-player buff replication mechanism. Supported bot Archery/Wrath effects remain internal trusted combat state and are explicitly acceptance-tested not to emit packet `55` to unrelated observers.
- Exact .NET `11.0.0-preview.7.26381.103` Linux runtime/NativeAOT packages are published upstream, but this container has no external DNS and the supplied offline cache still lacks the Linux ILCompiler/runtime packs. Linux NativeAOT therefore remains unexercised in this local pass; Windows NativeAOT also requires its Windows gate. Do not classify either AOT gate as green from ordinary Release build/tests.
- An official Terraria 1.4.5.8 GUI client is not present in this environment. The production-graph packet acceptance materially narrows the risk but does not replace independent official-client acceptance.
- Release build after the TZ-30 hardening changes is green with 0 warnings / 0 errors. `python3 tools/ci/check_documentation.py` is green for 92 mirrored RU/EN pages and 223 Markdown files.

## TZ-31 NpcBot actor hardening in progress

- Checkpoint: `/TZ/TerraRuntime-main-TZ-31-WIP.zip`, derived from `/TZ/TerraRuntime-main-TZ-30-WIP.zip`. This is a clean WIP checkpoint, not a released TZ-31, because the external NativeAOT and official-client evidence gates are still unavailable in this environment.
- NpcBot presentation actors are now spawned with `DamageOverride=0` and `DontTakeDamage=true`. This closes the ordinary hostile-NPC death/loot/progression farming path while bot-specific death/drop semantics remain undefined.
- Generic actor-control coverage now admits four source-backed motion families: ground fighters, AI_002 flying-eye steering, AI_005 flyer pursuit, and ordinary AI_014 bat pursuit. The bat controlled helper deliberately exposes only the pre-wander pursuit/collision slice; it excludes Queen Slime's boss-owned purple minion and does not advance ordinary wander/shooter timers.
- Demon Eye, Eater of Souls and Cave Bat are exercised as real NpcBot Follow presets through the safe actor-control commit boundary. Controlled NPC projectile/attack side effects remain absent; NpcBot offensive Guard is still fail-closed.
- Current affected Release pass is green for 69/69 tests across bot authority/network acceptance, actor-control, bat source motion, AI coverage/production composition and architecture boundaries. The narrower bot/actor/preset focused set is 46/46 green.
- Complete Release in-process runner after the actor hardening changes is green: 3194 tests, 0 failures, 25.31 s test time / 25.74 s process wall time, about 2.0 GiB peak RSS, exit 0.

## TZ-32 live-stability WIP

- TZ-32 remains WIP; `/TZ/TerraRuntime-main-TZ-32-WIP.zip` is the latest clean checkpoint but is not a final/released TZ-32. The latest complete Release suite is green at 3270/3270 tests, and the Release build reports 0 warnings / 0 errors.
- Trusted explosive projectile termination now owns terrain mutation. Exact admitted defaults/motion cover Bomb `28`, Dynamite `29`, launcher aiStyle-16 types, Mini Nuke family `793..810` except `802`, Celebration Mk2 holder `714`, and child rockets `715..718` with their separate aiStyle `147` behavior. Holder `714` remains untrusted presentation state; children require exact volley provenance. Unknown types remain fail-closed.
- Terrain explosions use source-pinned strict radii and `CanExplodeTile`/wall gates. Celebration Rocket IV uses `< 5`; Mini Nuke II uses `< 7`. Chests, dungeon/temple tiles and protected walls are regression-tested. Matching owner packet-17 edits are bounded convergence echoes after the authoritative mutation; they are not a second mining path and cannot grant an untrusted projectile terrain authority.
- Packet-17 network admission is now pinned to the verified Celebration worst-case burst: 2314 frames per 60 ticks, with a 2400 per-message ceiling and 4096 aggregate ceiling. This removes the old false `RateLimit` for the admitted legitimate burst without an unbounded bypass.
- The live liquid scheduler uses the 1.4.5.8 dedicated-server budget (`curMaxLiquid = 25000 - players * 250`, `cycles = 10 + players / 3`, capped at 2500 entries per empty-server tick). Water/lava/shimmer 3000-entry backlog tests, equal-TPS multiworld scheduling, zero-liquid starvation prevention and immediate post-tile-mutation wake are green.
- World-item pickup now runs a conservative server-side `FindOwner` pass every five ticks, publishes packet 22 to the nearest eligible player with an empty ordinary slot, and accepts packet-21 removal only from that exact reservation owner. Unsupported stacking/magnets/alternate storage remain fail-closed.
- Cross-world transfer normalizes vanilla mouse-item slot `58` into one empty main slot `0..49` before detach, clears destination slot 58 and ignores landing-gate packet-5 echoes. A full main inventory aborts transfer without detaching; repeated transfer, item-total and disconnect-before-landing/reconnect generation regressions are green.
- The production-order mining regression now sends all 59 packet-5 slots before world request, then packet 13 and packet 17 at a position outside the vanilla 640-pixel edge band. It covers Copper Pickaxe, Cobalt/Nebula/Solar/Stardust drills and Drill Containment Unit mount type `8` with source-backed pick power `210`. This fixed missing drill catalog IDs and removed a false-positive synthetic test position.
- Sandbox GodMode administration now resolves the selected player's process-level runtime route instead of using primary telemetry as a liveness gate.
- Remaining external acceptance: reproduce the original live official-client mining/tool/mount and explosion flows, stress liquids in several simultaneous worlds, and exercise transfer around disconnect/reconnect boundaries before declaring TZ-32 final.

## TZ-32 bot and vanilla-worldgen follow-up

- The production TUI composition now receives the primary runtime's real `RuntimeBotOperations`; `+ Bot` is therefore actionable in the shipping dashboard rather than only in isolated UI fixtures.
- Player-only controls are hidden for NpcBot. The NPC type list is deduplicated by exact `NpcTypeId`. The presentation actor uses an offset escort `MoveTo` point while publishing vanilla target `255`, remains invulnerable and server-side zero-contact, and is kept outside the followed player's client collision rectangle. This spatial rule matters because packet 23 cannot override a hostile NPC type's client-side contact damage per instance.
- PlayerBot pickup, QuickHeal and the admitted Archery/Wrath combat-buff path are invariant capabilities. Their former UI/configuration toggles are removed. Clothing/armor presets are also removed: each bot receives a stable generated skin/hair/color/armor identity.
- PlayerBot owns a visible three-weapon automatic loadout: Copper Broadsword in slot 0, Wooden Bow in slot 1 and Musket in slot 2, with separate arrows and bullets. Attack commits publish the selected hotbar slot and packet-13 use-item bit. Automatic policy selects conservative melee at close NPC range, bow at ordinary range and musket for long/fast targets; manual policies remain exact single-weapon presets.
- Ranged aim uses the source-backed `PickAmmo` launch magnitude, projectile `extraUpdates`, and the admitted aiStyle-1 arrow gravity slice. It solves against target velocity, simulates projectile/target rectangles over time and rejects tile- or liquid-blocked paths. Guard target acquisition also requires exact `VanillaWorldCanHit` visibility, so ammo is not consumed through walls. Copper Broadsword attacks reuse the existing player-owned NPC damage/loot/progression finalizer.
- PlayerBot movement now publishes direction controls, auto-jumps low forward obstacles and follows per-bot offset formation points with different oscillation phases. Its five ordinary accessory slots contain Fishron Wings, Soaring Insignia, Terraspark Boots, Magiluminescence and Master Ninja Gear. The physics path admits the exact Terraspark run cap/acceleration and grounded Magiluminescence multipliers; dash remains unimplemented rather than approximated. A clear same-level route now targets the followed player's ground level and releases jump, so the bot walks instead of permanently low-hovering. A materially higher player or solid direct-route obstruction raises the formation target by a useful margin and holds jump long enough for the existing Fishron-wing path to climb; regression tests advance the real physics state and cover both ground walking and obstacle ascent.
- The vanilla dungeon entrance search now follows the 1.4.5.8 surface-exit loop, beach distance and exact cloud-tile rejection rather than selecting an arbitrary generated sky island. The dungeon pass registers the Old Man at the source anchor. A fresh large seed-1458 candidate passed the official reference-world structural budgets: spawn delta `(1,0)`, dungeon delta `(69,6)`, surface/rock deltas `0`, silhouette NMAE `0.021349`, and `181 -> 98` chests. This is structural evidence, not byte parity or complete vanilla worldgen.
- The ordinary `Underground Houses and Buried Chests` structural slice now draws all four source budgets in source order and places the area-scaled cave houses plus additional desert houses after cave/Underworld chests. Accepted houses use source room discovery, biome palette scoring, protected spacing, shells/walls, stairs, exits, doors, support beams and a persistent palette chest. Canonical seed `42` and the multi-seed full-integration class enforce the expected `35..40` ordinary houses and fixed `2` additional desert houses for Small worlds. Decorative furniture/aging and remaining unported worldgen passes are still open; this is not complete vanilla parity.
- Larva tile `231` now routes through a source-backed 3x3 framed object mutation. Successful client pick, server projectile cut and trusted terrain explosion destruction find the nearest live real/server-controlled player in slot order and invoke the existing authoritative boss spawn path for Queen Bee `222` only at strict Manhattan distance `<4800` pixels; corrupt frames and missing nearby players fail closed after the source-shaped tile mutation.
- The complete Release runner is 3258/3258 green; Release build is 0 warnings / 0 errors. Bot regressions cover generated visual diversity, distinct escort destinations, actual clear-route ground walking, actual obstacle ascent, NpcBot separation/no-contact damage, deduplicated UI types, five accessory replication, exact Terraspark/Magiluminescence movement constants and the real `+ Bot` Accept-to-operations path. The cave-house integration set is 25/25 green across canonical/multi-seed generation and chest placement; Larva has six focused object/runtime tests.
- Remaining scope is still material: official-client bot acceptance, richer obstacle route planning, additional weapon/projectile families, more NPC/boss AI, and the unported vanilla worldgen passes/features. Do not describe this follow-up as complete Sandbox Level 2.

## TZ-33 controlled-magic local-immunity/reacquisition WIP

- Checkpoint: `/TZ/TerraRuntime-main-TZ-33-WIP.zip`, derived only from the 2026-09-07 attached `TerraRuntime-main.zip` working tree plus the source-correct TZ-32 packet-50 changes already carried in that tree. Do not reconstruct it from older TZ-29/30/31 archives.
- Flamelash `34` and Rainbow Rod `79` now share one `RuntimeProjectileNpcLocalImmunityRegistry` between authoritative NPC collision and AI target lookup. The registry is keyed by exact projectile and NPC generations, preserves the source `12`-tick positive cooldown boundary, and preserves permanent negative local immunity for admitted grenade variants.
- A committed released Flamelash/Rainbow Rod NPC hit now applies the source type-specific post-hit reset `ai[1] = -1`. The next AI_009 `FindTargetWithLineOfSight` lookup skips exact NPC generations still marked nonzero in projectile-local immunity, so another legal line-of-sight NPC can be acquired; the prior target becomes eligible again at the exact 12-tick boundary.
- This closes the previous roadmap gap named `localNPCImmunity-aware post-hit target reacquisition` for the admitted Flamelash/Rainbow Rod slice. It does not claim full `NPC.CanBeChasedBy` parity: transient friendly/chaseable/immortal flags not represented by the runtime remain an explicit fail-closed gap.
- Explosive self-hurt remains open. Terraria 1.4.5.8 `Projectile.SelfHurtPlayers` feeds self-damage through `Main.DamageVar(..., -localPlayer.luck)`, while TerraRuntime does not yet own exact player luck; substituting luck zero would not be source-correct.
- Current TZ-33 local verification before packing: Release build 0 warnings / 0 errors and complete Release test suite 3275/3275 green. Re-run both after documentation changes and before treating the checkpoint as clean.

## TZ-34 projectile-specific PvP status / packet-55 WIP

- Checkpoint: `/TZ/TerraRuntime-main-TZ-34-WIP.zip`, derived from the current TZ-33 working tree whose provenance ultimately remains the user-designated 2026-09-07 attached `TerraRuntime-main.zip`. Do not replace it with an older library archive.
- TerrariaServer 1.4.5.8 source order is preserved: a legal PvP projectile hit calls `StatusPvP(target)` before `TryDoingOnHitEffects` and before `Player.Hurt(..., pvp: true, ...)`. Creative GodMode can therefore avoid the damage inside `Hurt` while the preceding status roll still succeeds; TerraRuntime intentionally reproduces that non-obvious ordering.
- The admitted type-specific `StatusPvP` subset is exact: Fire Arrow `2` -> `On Fire!` `24` for `180` ticks at `1/3`; Flamelash `34` -> `On Fire!` for `240` ticks at `1/2`; Poisoned Knife `54` -> `Poisoned` `20` for `600` ticks at `1/2`. Equipment/melee-enchant status effects and unsupported projectile families remain fail-closed.
- `VanillaPvpBuffFacts1458` pins the exact 1.4.5.8 `Main.pvpBuff` true set. `TerrariaPlayerPvpBuffCodec1458` owns packet `55` with exact payload `[target player byte][buff ushort][duration int32]` and rejects invalid IDs/durations.
- Authoritative combat publishes only a proven PvP status side effect. `PlayerAuthority` validates the exact target generation and relayable buff type, `RuntimePlayerEventDispatcher` carries the event, and `RuntimeConnectionRegistry` enqueues packet `55` only to that exact playing target generation. It is not broadcast to observers and does not become a server-owned buff-duration mirror.
- Focused validation: packet-55 codec/facts/relay classes are 11/11 green; Fire Arrow PvP ingress/status scenarios are 3/3 green; the Creative-GodMode ordering regression is included in that set. The known `ListenerManagerTests.Same_port_bind_address_change_preserves_existing_client` scheduler flake was observed once during a full run and then passed three consecutive isolated 3/3 class runs. Final candidate verification after documentation changes: Release affected production/test graph build 0 warnings / 0 errors; direct `dotnet vstest` on the built Release assembly 3284/3284 green in 27 s; documentation validation 92 mirrored RU/EN pages and 223 Markdown files.
- Remaining status gap is intentionally narrower, not falsely closed: hostile PvE projectile status such as Cultist Fireball `467` `On Fire!` still cannot be reconstructed from packet `50` because official dedicated server skips `Damage_EVP` and packet `50` has no duration. Exact equipment-derived PvP status also remains open until the corresponding owner state is authoritative.

## Operator bots at TZ-29

- Bot behavior/policy is isolated under `TerraRuntime.Application.Bots`; source-pinned bot item/NPC facts live under `TerraRuntime.Gameplay.Bots`. There is intentionally no bot-specific dependency from `TerraRuntime.Core`.
- `PlayerBot` owns only policy. Its body remains a normal server-owned player and reuses existing server-player, player, projectile and world-item authorities for state, physics, combat and item removal.
- `PlayerBot` supports `Idle`, `Follow` and `Guard`, generated per-bot appearance/armor, target selection, weapon policy, per-bot escort formation/phase, stuck/hard-distance recovery and bounded detached telemetry.
- A Follow/Guard target is considered live only when the exact `PlayerHandle(slot,generation)` resolves with an authoritative health baseline and positive life.
- Guard PvP is fail-closed unless the protected target and candidate opponent both have vanilla hostile/PvP enabled. PlayerBot ranged attacks use the trusted server-player projectile path and consume source-backed ammo.
- Pickup is always enabled but deliberately narrow: only the currently required arrow/bullet ammo, supported healing potions, and combat potions whose effect TerraRuntime reproduces. Unsupported item semantics are ignored.
- Auto-heal mirrors the source-backed QuickHeal candidate ordering for the admitted healing subset and observes the ordinary 3600-tick potion delay. Auto-buff currently commits only Archery and Wrath because those outgoing combat effects are reproduced by the bot controller; other catalogued potion buffs remain fail-closed.
- `NpcBot` is a real authoritative NPC actor, not a fake-player disguise. It supports `Idle`, `Follow`, `Guard` positioning and stuck/hard-distance recovery through the existing NPC actor-control/physics path. Presets are admitted only for hostile, non-boss NPCs with a verified controlled-motion family; current coverage includes ground fighters, AI_002 flying eyes, AI_005 flyers and ordinary AI_014 bats. The bot body is invulnerable and has zero contact damage so it cannot become an ordinary NPC loot/progression farm.
- NpcBot offensive Guard remains fail-closed until NPC-owned attack/projectile provenance for the controlled-actor path is separately source-verified. Do not synthesize a player projectile owner for an NPC body.
- Switching bot body/preset replaces the authoritative actor through the existing server-player/NPC lifecycle boundary; the UI never mutates actor state directly.

## TZ-32 packet-50 player-buff presentation synchronization

- TerrariaServer 1.4.5.8 source verification corrected an initially considered but rejected design: hostile projectile `Damage_EVP` is skipped when `Main.netMode == 2`, so dedicated server does not independently roll the client-side PvE status duration. Packet `55` is targeted PvP buff delivery and must not be repurposed as a PvE duration fallback.
- Packet `50` is now a bounded typed ingress/egress path. The accepted shape is one claimed player byte, at most 44 nonzero version-pinned buff IDs, and one explicit zero `ushort` terminator. Invalid length, missing/floating terminator and invalid IDs fail as malformed protocol.
- `PlayerBuffFrameSink` discards the claimed player byte after slot assignment, uses the exact connection-owned `PlayerHandle` generation, and posts an owned replaceable snapshot through the existing authoritative queue. Queue saturation may drop this presentation sample; malformed protocol does not.
- `PlayerAuthority` stores only the client-reported type list in the generation-scoped transfer/presentation profile. It is not wired into authoritative combat modifiers because packet `50` has no duration and the client's active list can reflect local `AddBuff` immunity/timing semantics the dedicated server cannot reconstruct from this packet.
- `RuntimeConnectionRegistry` retains the exact generation's encoded packet-50 frame, suppresses duplicate snapshots, relays changes to playing peers and exchanges the retained state during late-join baseline. A never-observed snapshot remains distinct from an observed empty snapshot.
- Cross-world transfer carries packet-50 presentation state only when it was actually observed; generation reuse cannot expose a prior player's retained frame.
- Final local validation for this checkpoint: .NET `11.0.100-preview.7.26381.103`, Release affected production/test graph build 0 warnings / 0 errors; complete `TerraRuntime.Tests` 3270/3270 passed in 23 s test time / 25.34 s process wall time, peak RSS about 128 MiB; `python3 tools/ci/check_documentation.py` passes 92 mirrored RU/EN pages and 223 Markdown files.
- Authoritative projectile buff/debuff mutation remains open in `docs/roadmap.md`. Do not infer exact remaining PvE debuff duration from packet `50` and do not create a parallel packet-55 authority path to make the checklist green.

## TUI and network presentation at TZ-29

- The Worlds / Players action row is exactly `+ Sandbox`, then `+ Bot`; the duplicate dashboard Settings button is removed. Runtime settings remain available from the top-level Settings menu.
- Bot rows render their body kind and support double-click settings plus typed despawn. The settings window exposes PlayerBot/NpcBot body, a deduplicated supported NPC type, weapon policy, mobility accessories, target and mode. Appearance/armor are generated and pickup/heal/buffs are automatic, so those old controls no longer exist.
- The Network graph history is packets/second, with independently scaled IN and OUT axes. The numeric line reports both packet rate and byte throughput (`KiB/s` or `MiB/s`).
- Terminal.Gui framebuffer smoke tests use the `.NET System.Console` driver in tests. The ANSI driver performs terminal-capability handshakes and can block indefinitely in a non-TTY CI/container; production driver selection is unchanged.

## Fresh generated sandbox memory invariant at TZ-29

- `WorldFileFreshComposer326.TryCompose` validates the freshly composed canonical bytes while preserving the already generated `WorldTileStore`; the returned validated `WorldFileData.Tiles` is the same instance as the generation candidate.
- `SandboxWorldMaterializer` passes that validated world directly to bootstrap instead of loading the canonical bytes a second time. Existing `.wld` sandbox sources still use the normal loader.
- This removes the second full tile decode/allocation from the fresh-generation path.
- Acceptance measurements in this environment: small `4200x1200` completed at about 204 MiB peak RSS in Debug; large `8400x2400` completed in Release in about 10.3 s process wall time at about 462 MiB peak RSS, with zero swap/OOM.

## Authoritative liquid runtime state

Source-backed against TerrariaServer 1.4.5.8 `Liquid.Update`, `Liquid.UpdateLiquid`, `Liquid.LiquidCheck`, `Liquid.CreateLiquidMergeTile`, the final `Main.tileObsidianKill` table and `TileID.Sets.IsAContainer`:

- ordinary same-kind flow, partial downward continuation, 2/3/4/5/7-cell horizontal leveling and the `255 -> 254` edge are implemented;
- lava/honey delays are source-backed at 5/10 logical liquid updates;
- one active liquid entry advances at most once per TerraRuntime server tick even when the work budget is larger than one;
- dedicated-server stable-entry retirement uses `10 + activePlayersInSlots0To14 / 3`, changed amounts reset `kill`, stable `254` retires to `255`, and Underworld water evaporates by two units per update below `maxTilesY - 200`;
- open-cell material reactions use the verified 24-unit threshold and source order for Obsidian `56`, Honey Block `229`, Crispy Honey Block `230` and Shimmer Block `659`;
- packet-20 material replication carries the source-backed `TileChangeType` and replaces redundant packet-48 fanout for merge-cleared cells;
- lower `tileCut`, the final 276-entry `tileObsidianKill` set and the `21/467/88` container set cross the authoritative tile side-effect boundary;
- unsupported complex `WorldGen.ReplaceTile` dependency/shape/actuator/object cases remain fail-closed before participating liquid is cleared.

## Normal-world loading liquid state at TZ-28

Source-backed against TerrariaServer 1.4.5.8 `WorldFile.LoadWorld`, `Liquid.QuickWater`, `Liquid.UpdateLiquid`, `WorldGen.WaterCheck`, `TileObjectData.CheckWaterDeath`, `CheckLavaDeath`, `Liquid.tilesIgnoreWater` and loading-time `LiquidCheck`:

- `quickFall` and `quickSettle` are separate modes; both bypass ordinary lava/honey delays while `quickSettle` uses the fixed loading retirement threshold `8` and the verified `>250 -> 255` refill behavior;
- loading-time foreign-liquid contact clears liquids without creating runtime merge blocks because vanilla passes `createMergeTilesDuringGen: false`;
- `QuickWater` uses the bottom-up vanilla scan/horizontal path search and the source-backed temporary solidity exceptions for tiles `138`, `484`, `546`, `664`, `711..716`; Bubble `379` remains a barrier;
- `Liquid.QuickWater(2)` in `WorldFile.LoadWorld` uses `2` as verbosity, not as a scan bound; TerraRuntime therefore runs the complete normal-world load scan;
- `TileObjectData.UsesGlobalLiquidChecks` remains true throughout TerrariaServer 1.4.5.8 initialization, so loading water/lava death resolves to the final global tables: 10 water-death tile types and 267 lava-death tile types;
- `WaterCheckLoading` preflights unsupported liquid-death objects before mutation, removes supported single-cell death targets without drops, normalizes near-full cells and rebuilds the bounded active/buffered liquid queue through the loading AddWater gates;
- the canonical startup sequence is now `QuickWater -> WaterCheck -> quickSettle UpdateLiquid until active queue empty or 100000 iterations -> WaterCheck`;
- reaching the vanilla 100000-iteration guard does not fail loading; the final WaterCheck still runs;
- post-load mutations on the unpublished candidate use initial-population tile writes so network/persistence dirty queues are not manufactured before publication;
- Remix/Zenith loading remains fail-closed before mutation because `SettleWaterAt` depends on generation-only lava-line/ocean-depth inputs not yet represented in the load context.

## Runtime-cache layout 2 invariant

- `.wld` remains canonical; `.runtime-world` is disposable derived state.
- `RuntimeWorldSnapshotCache.TryWriteAtomic` rejects any world whose `WorldTileStore.IsPostLoadLiquidPrepared` marker is false with `PostLoadLiquidNotPrepared`.
- `WorldTileStore.MarkPostLoadLiquidPrepared` is internal to `TerraRuntime.World`; application code cannot forge the marker.
- `RuntimeWorldSnapshotCache.TryLoad` restores the marker only after schema/layout, source binding, tile/liquid/prepared payload integrity, world format and dimensions all pass.
- `WorldStartupPreparation` runs the normal-world liquid initializer after canonical decode and before runtime-cache/bootstrap admission.
- `RuntimeWorldSnapshotRebuilder` replays the same initializer after every successful canonical save before publishing a replacement runtime cache.
- `RuntimeWorldSnapshotCache.TrySaveCanonicalCheckpointAtomic` reconstructs post-load-prepared state from the embedded canonical bytes before refreshing cache source identity, then restores the exact scheduler snapshot carried by the validated cache.
- runtime cache layout version is `2`; older/preparation-unaware caches are deterministic misses rather than migration candidates.

## Sandbox transfer fix retained from TZ-26

Cross-world TUI moves attach the authoritative player at the destination spawn. The client packet-12 `SpawningIntoWorld` echo generated after the synthetic replacement-world spawn is consumed by the landing gate and cannot replace the correction target with `SpawnX/SpawnY=-1/-1`. Ordinary post-join respawn handling remains separate.

## Terminal UI state retained from TZ-26

- live sandbox rows show `[sandbox]` without redundant `running`; non-running lifecycle statuses remain visible;
- Network uses a block-column overlay with IN on the left independently scaled axis, OUT on the right independently scaled axis, and a distinct overlap cell/color.

## Validation recorded for TZ-29

- local .NET 11 Release build of the test/production graph: 0 warnings and 0 errors;
- focused bot authority set: 11/11; NPC actor-control set: 4/4; dashboard interaction: 15/15; network/operations cache: 7/7;
- affected sandbox/worldgen/player/projectile/TUI suites are green, including `Level1SandboxRuntimeTests` and `WorldFileFreshComposer326Tests`;
- complete Release `TerraRuntime.Tests` in-process suite exits 0 in 53.69 s wall time in this environment; peak process RSS was about 1.24 GiB and swap remained zero;
- the previous full-suite hang was isolated to Terminal.Gui ANSI smoke tests in a non-TTY runner. Test harnesses now use `DriverRegistry.Names.DOTNET`; production TUI driver selection is unchanged.
- small and large generated-sandbox materialization paths are both green; large Release generation no longer exhibits the former second-decode OOM behavior.
- local Linux NativeAOT publish was attempted after the green test pass, but the supplied minimal offline NuGet cache does not contain the required `runtime.linux-x64.Microsoft.DotNet.ILCompiler`, `Microsoft.NETCore.App.Runtime.linux-x64`, `Microsoft.AspNetCore.App.Runtime.linux-x64` or NativeAOT runtime packs. Restore therefore fails with `NU1100` before compilation; this is an environment/cache limitation, not a recorded green AOT result. Windows NativeAOT is likewise not publishable from this Linux host.

## Next recommended pass

First complete TZ-32 official-client acceptance: Celebration Mk2 with Rocket IV and Mini Nuke II under sustained legal fire, Bomb/Dynamite, ordinary picks and all admitted drills/DCU, large water/lava/shimmer backlogs across multiple worlds, ordinary world-item pickup, cursor-item/full-inventory/repeated transfers, disconnect/reconnect near the transfer boundary, and packet-50 buff-list convergence around ordinary hostile-projectile hits. Treat any mismatch as a source/production-path investigation; do not relax authority or add a legacy path. Then continue the roadmap from authoritative combat/projectile side effects, with projectile buff/debuff mutation still explicitly open until a source-correct server authority model can represent the required duration/immunity semantics. Complete the still-external TZ-30 NativeAOT gates when the exact .NET 11 preview-7 packs are available. For further NpcBot coverage, keep offensive Guard fail-closed and presentation actors invulnerable/zero-contact until their source-backed combat/death/drop semantics exist.
