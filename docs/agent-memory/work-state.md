# Work state

Updated: 2026-09-06.

This is the resume point for the next agent/session. Read it before reconstructing project state from source.

## Last clean checkpoint

- Checkpoint: `/TZ/TerraRuntime-main-TZ-29.zip`.
- Base clean checkpoint: `/TZ/TerraRuntime-main-TZ-28.zip`.
- TZ-29 closes the first production operator-bot slice, the fresh-sandbox worldgen memory reuse fix, packets/sec dashboard graph semantics, and headless Terminal.Gui smoke-test reliability.

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

- TZ-32 remains WIP; no final checkpoint archive has been produced. The latest complete Release suite is green at 3232/3232 tests, and the Release build reports 0 warnings / 0 errors.
- Trusted explosive projectile termination now owns terrain mutation. Exact admitted defaults/motion cover Bomb `28`, Dynamite `29`, launcher aiStyle-16 types, Mini Nuke family `793..810` except `802`, Celebration Mk2 holder `714`, and child rockets `715..718` with their separate aiStyle `147` behavior. Holder `714` remains untrusted presentation state; children require exact volley provenance. Unknown types remain fail-closed.
- Terrain explosions use source-pinned strict radii and `CanExplodeTile`/wall gates. Celebration Rocket IV uses `< 5`; Mini Nuke II uses `< 7`. Chests, dungeon/temple tiles and protected walls are regression-tested. Matching owner packet-17 edits are bounded convergence echoes after the authoritative mutation; they are not a second mining path and cannot grant an untrusted projectile terrain authority.
- Packet-17 network admission is now pinned to the verified Celebration worst-case burst: 2314 frames per 60 ticks, with a 2400 per-message ceiling and 4096 aggregate ceiling. This removes the old false `RateLimit` for the admitted legitimate burst without an unbounded bypass.
- The live liquid scheduler uses the 1.4.5.8 dedicated-server budget (`curMaxLiquid = 25000 - players * 250`, `cycles = 10 + players / 3`, capped at 2500 entries per empty-server tick). Water/lava/shimmer 3000-entry backlog tests, equal-TPS multiworld scheduling, zero-liquid starvation prevention and immediate post-tile-mutation wake are green.
- World-item pickup now runs a conservative server-side `FindOwner` pass every five ticks, publishes packet 22 to the nearest eligible player with an empty ordinary slot, and accepts packet-21 removal only from that exact reservation owner. Unsupported stacking/magnets/alternate storage remain fail-closed.
- Cross-world transfer normalizes vanilla mouse-item slot `58` into one empty main slot `0..49` before detach, clears destination slot 58 and ignores landing-gate packet-5 echoes. A full main inventory aborts transfer without detaching; repeated transfer, item-total and disconnect-before-landing/reconnect generation regressions are green.
- The production-order mining regression now sends all 59 packet-5 slots before world request, then packet 13 and packet 17 at a position outside the vanilla 640-pixel edge band. It covers Copper Pickaxe, Cobalt/Nebula/Solar/Stardust drills and Drill Containment Unit mount type `8` with source-backed pick power `210`. This fixed missing drill catalog IDs and removed a false-positive synthetic test position.
- Sandbox GodMode administration now resolves the selected player's process-level runtime route instead of using primary telemetry as a liveness gate.
- Remaining external acceptance: reproduce the original live official-client mining/tool/mount and explosion flows, stress liquids in several simultaneous worlds, and exercise transfer around disconnect/reconnect boundaries before declaring TZ-32 final.

## Operator bots at TZ-29

- Bot behavior/policy is isolated under `TerraRuntime.Application.Bots`; source-pinned bot item/NPC facts live under `TerraRuntime.Gameplay.Bots`. There is intentionally no bot-specific dependency from `TerraRuntime.Core`.
- `PlayerBot` owns only policy. Its body remains a normal server-owned player and reuses existing server-player, player, projectile and world-item authorities for state, physics, combat and item removal.
- `PlayerBot` supports `Idle`, `Follow` and `Guard`, clothing/armor presets, target selection, weapon policy, stuck/hard-distance recovery and bounded detached telemetry.
- A Follow/Guard target is considered live only when the exact `PlayerHandle(slot,generation)` resolves with an authoritative health baseline and positive life.
- Guard PvP is fail-closed unless the protected target and candidate opponent both have vanilla hostile/PvP enabled. PlayerBot ranged attacks use the trusted server-player projectile path and consume source-backed ammo.
- Pickup is deliberately narrow: only the currently required arrow/bullet ammo, supported healing potions, and combat potions whose effect TerraRuntime reproduces. Unsupported item semantics are ignored.
- Auto-heal mirrors the source-backed QuickHeal candidate ordering for the admitted healing subset and observes the ordinary 3600-tick potion delay. Auto-buff currently commits only Archery and Wrath because those outgoing combat effects are reproduced by the bot controller; other catalogued potion buffs remain fail-closed.
- `NpcBot` is a real authoritative NPC actor, not a fake-player disguise. It supports `Idle`, `Follow`, `Guard` positioning and stuck/hard-distance recovery through the existing NPC actor-control/physics path. Presets are admitted only for hostile, non-boss NPCs with a verified controlled-motion family; current coverage includes ground fighters, AI_002 flying eyes, AI_005 flyers and ordinary AI_014 bats. The bot body is invulnerable and has zero contact damage so it cannot become an ordinary NPC loot/progression farm.
- NpcBot offensive Guard remains fail-closed until NPC-owned attack/projectile provenance for the controlled-actor path is separately source-verified. Do not synthesize a player projectile owner for an NPC body.
- Switching bot body/preset replaces the authoritative actor through the existing server-player/NPC lifecycle boundary; the UI never mutates actor state directly.

## TUI and network presentation at TZ-29

- The Worlds / Players action row is exactly `+ Sandbox`, then `+ Bot`; the duplicate dashboard Settings button is removed. Runtime settings remain available from the top-level Settings menu.
- Bot rows render their body kind and support double-click settings plus typed despawn. The settings window exposes PlayerBot/NpcBot type, supported NPC preset, clothing, armor, weapon policy, target, mode, pickup and consumable toggles.
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

First complete TZ-32 official-client acceptance: Celebration Mk2 with Rocket IV and Mini Nuke II under sustained legal fire, Bomb/Dynamite, ordinary picks and all admitted drills/DCU, large water/lava/shimmer backlogs across multiple worlds, ordinary world-item pickup, cursor-item/full-inventory/repeated transfers, and disconnect/reconnect near the transfer boundary. Treat any mismatch as a source/production-path investigation; do not relax authority or add a legacy path. Then complete the still-external TZ-30 NativeAOT gates when the exact .NET 11 preview-7 packs are available. For further NpcBot coverage, keep offensive Guard fail-closed and presentation actors invulnerable/zero-contact until their source-backed combat/death/drop semantics exist.
