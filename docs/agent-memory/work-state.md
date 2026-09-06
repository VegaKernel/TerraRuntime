# Work state

Updated: 2026-09-06.

This is the resume point for the next agent/session. Read it before reconstructing project state from source.

## Last clean checkpoint

- Checkpoint: `/TZ/TerraRuntime-main-TZ-26.zip`.
- Base: TZ-25 repository-local agent memory plus ordinary dedicated-server liquid lifecycle.
- TZ-26 adds the active-cell `LiquidCheck` side-effect/protocol slice, fixes cross-world packet-12 transfer echo handling, replaces the Network point graph with independent-scale IN/OUT block columns, and removes redundant `running` text from live sandbox rows.

## Liquid state at TZ-26

Source-backed against TerrariaServer 1.4.5.8 `Liquid.Update`, `Liquid.UpdateLiquid`, `Liquid.LiquidCheck`, `Liquid.CreateLiquidMergeTile`, the final `Main.tileObsidianKill` table and `TileID.Sets.IsAContainer`:

- all ordinary TZ-25 flow, delay, retirement and Underworld evaporation behavior remains in place;
- the final source-pinned `tileObsidianKill` capability contains 276 Tile IDs;
- the container set is exactly tiles `21`, `467` and `88`;
- supported lower `tileCut` goes through the authoritative tile-break/drop/NPC boundary and emits packet 17 before the later merge packet 20;
- supported active `tileObsidianKill` replacement and the lower container override use a prepare/commit side-effect boundary so unsupported targets fail before participating liquid is cleared;
- material merge clears participating liquids in `LiquidCheck` order and emits one packet-20 tile square with the source-backed `TileChangeType`, without redundant packet-48 fanout for the cleared merge cells;
- active replacement is intentionally limited to source-backed simple/frame-important single-cell break paths that can be replaced safely without unimplemented `ReplaceTile` neighbour/shape/actuator semantics.

Still open/fail-closed:

- the remainder of complex `WorldGen.ReplaceTile` object/dependency cases beyond the safe active merge subset;
- `quickFall` / `quickSettle`;
- panic/forced-settle behavior;
- complete post-load liquid initialization parity.

## Sandbox transfer fix at TZ-26

Cross-world TUI moves already attach the authoritative player at the destination world's spawn. The reported upper-left/correction loop came from the client echo of the synthetic transfer packet 12:

1. TerraRuntime sends packet 12 with `SpawnContext=SpawningIntoWorld` after replacement-world bootstrap.
2. Terraria 1.4.5.8 `Player.Spawn(SpawningIntoWorld)` runs `FindSpawn`/`CheckSpawn`; when the personal spawn is not valid it may set `SpawnX/SpawnY` to `-1/-1`.
3. The multiplayer client sends packet 12 back to the server.
4. TerraRuntime previously treated that transfer echo as a new authoritative respawn, converted `-1/-1` to world position near the upper-left edge and overwrote the landing correction target.

`PlayerBootstrapFrameSink` now consumes only packet-12 `SpawningIntoWorld` echoes while the cross-world landing gate is active. The destination attach remains authoritative and stale movement is corrected to the destination spawn until the first valid landing sample arrives. Ordinary post-join respawn handling is unchanged.

## Terminal UI state at TZ-26

- Live sandbox rows omit the redundant `running` text and display `[sandbox]`; non-running lifecycle statuses remain visible, for example `[sandbox · stopping]`.
- The compact Network chart is a custom block-column view instead of point-based `GraphView`.
- IN throughput uses the left axis and its own scale; OUT throughput uses the right axis and a separate scale.
- Histories are overlaid in one plot using coloured `█` / `▓` columns and `▒` overlap cells, so a quiet direction stays visible when the opposite direction is orders of magnitude larger.

## Validation recorded for TZ-26

- test/production graph build with local .NET 11: 0 warnings, 0 errors;
- transfer + dashboard + bootstrap + sandbox + Terminal.Gui focused set: 43/43;
- combined liquid/tile/replication/projectile-cut/transfer/TUI affected set: 134/134;
- ANSI framebuffer smoke verifies both `IN`/`OUT` axes and rendered block columns;
- network independent-scale regression verifies a 2048 KiB/s vs 2 KiB/s pair remains visible in both directions.
- `python3 tools/ci/check_documentation.py`: green, 92 mirrored RU/EN pages and 223 Markdown files checked.

## Next recommended pass

After TZ-26 is packaged, stay on liquid parity unless a live bug has higher priority. The next clean boundary is `quickFall` / `quickSettle` and forced-settle/post-load initialization. Do not broaden the safe active replacement subset by approximating `WorldGen.ReplaceTile`; map and test each remaining dependency/shape/container branch from 1.4.5.8 first.
