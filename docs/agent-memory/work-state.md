# Work state

Updated: 2026-09-06.

This is the resume point for the next agent/session. Read it before reconstructing project state from source.

## Last clean checkpoint

- Checkpoint: `/TZ/TerraRuntime-main-TZ-28.zip`.
- Base clean checkpoint: `/TZ/TerraRuntime-main-TZ-27.zip`.
- TZ-28 closes the normal-world TerrariaServer 1.4.5.8 post-load liquid preparation/cache-admission slice and hardens runtime-cache layout 2 so an unprepared canonical world cannot be serialized as a prepared startup image.

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

## Validation recorded for TZ-28

- `TerraRuntime.World`, `TerraRuntime.Application` and the complete `TerraRuntime.Tests` production/test graph build on the local .NET 11 SDK with 0 warnings and 0 errors;
- affected liquid/load/cache/startup/checkpoint regression set: 92/92;
- cache/rebuilder/exporter focused set: 31/31 before the broader affected run;
- `python3 tools/ci/check_documentation.py`: green, 92 mirrored RU/EN pages and 223 Markdown files checked;
- an attempted nearly-full in-process test run excluding the known heavy worldgen acceptance classes exceeded the environment timeout after entering unrelated tests; it produced no failure attributable to this pass and is not counted as a green full-suite result.

## Next recommended pass

Stay on liquid parity unless a live bug has higher priority. The next source-backed targets are panic/forced-settle behavior and the remaining complex `WorldGen.ReplaceTile` cases. Remix/Zenith post-load liquid mapping should remain fail-closed until the exact `GenVars.lavaLine`/`WorldGen.oceanDepths` load inputs can be reconstructed without approximation. After those are closed, run a worldgen + post-load liquid acceptance pass on canonical 1.4.5.8 worlds rather than adding another parallel liquid implementation.
