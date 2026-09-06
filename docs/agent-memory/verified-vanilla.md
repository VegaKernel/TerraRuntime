# Verified vanilla facts

Last evidence refresh: 2026-09-06.

This file stores concise facts already checked against the locally decompiled official TerrariaServer **1.4.5.8**. It prevents repeated source archaeology, but it does not authorize guessing adjacent behavior. Unknown cases remain fail-closed until separately verified.

## Liquids

Primary evidence: `Terraria.Liquid.Update`, `Terraria.Liquid.LiquidCheck`, and the relevant liquid helper/check paths in TerrariaServer 1.4.5.8.

Verified runtime facts currently implemented/tested:

- Ordinary same-kind settling is gravity-first, then horizontal leveling when liquid remains in the source during the same update.
- Horizontal leveling uses the source-backed 2/3/4/5/7-cell averaging shape.
- The full-source downward-fill edge preserves the vanilla `255 -> 254` source case when the lower cell receives the final unit.
- 5/7-cell averaging preserves the fed source column in the verified branch where neighbours already equal the rounded level.
- Lava ordinary flow delay is 5 liquid updates.
- Honey ordinary flow delay is 10 liquid updates.
- Water does not own the material merge itself; it wakes adjacent lava/honey/shimmer cells and the foreign-liquid update owns `LiquidCheck` merge placement.
- Water + lava creates Obsidian tile `56`.
- Water + honey creates Honey Block tile `229`.
- Lava + honey creates Crispy Honey Block tile `230`.
- Shimmer contact wins the verified source-order selection and creates Shimmer Block tile `659`.
- The verified ordinary open-cell merge threshold is 24 liquid units.
- Left/right/up foreign-liquid handling and the lower-cell sub-24 source clear follow `LiquidCheck` ordering.
- Material mutations and the verified lower-cell sub-24 foreign-liquid clear use packet `20` tile-square replication, not packet `48` alone.
- `LiquidCheck` clears participating merge liquids before `CreateLiquidMergeTile`; the material merge is then synchronized by the tile-square packet rather than separate packet-48 updates for every cleared merge cell.
- TerrariaServer 1.4.5.8 packet-20 `TileChangeType` values for these merges are `LavaWater=1`, `HoneyWater=2`, `HoneyLava=3`, `ShimmerWater=4`, `ShimmerLava=5`, `ShimmerHoney=6`.
- the final initialized `Main.tileObsidianKill` set contains 276 tile types; TerraRuntime stores the exact source-pinned set instead of deriving it from a guessed category.
- `TileID.Sets.IsAContainer` for this path is exactly tiles `21`, `467`, and `88`.
- for non-water lower contact, `LiquidCheck` performs the verified `Main.tileCut` lower-cell kill before lower merge eligibility is evaluated.
- In dedicated-server `Liquid.UpdateLiquid`, stable active entries retire at `10 + activePlayersInSlots0To14 / 3`; Terraria 1.4.5.8 counts only player slots `0..14` for this liquid-cycle threshold.
- A changed liquid amount resets `kill` to zero and schedules the cell above; unchanged entries increment `kill`.
- A stable `254` liquid amount is normalized to `255` when the active entry retires.
- Water with `y > Main.UnderworldLayer` loses two liquid units per `Liquid.Update`; `Main.UnderworldLayer == Main.maxTilesY - 200`.
- While `WorldGen.isGeneratingOrLoadingWorld`, `Liquid.quickSettle` implies quick-fall scheduling: lava/honey delay gates are bypassed, the active-entry retirement threshold is the fixed value `8`, and a source left above `250` after downward transfer is restored to `255`.
- Loading-time `LiquidCheck(..., createMergeTilesDuringGen: false)` clears participating foreign liquids but does not create Obsidian/Honey/Crispy/Shimmer merge blocks.
- `Liquid.QuickWater` scans bottom-up with default y bounds `3..maxTilesY-3` and x bounds `4..maxTilesX-5`; `tilesIgnoreWater(true)` temporarily makes tiles `138`, `484`, `546`, `664`, `711..716` non-solid, while tile `379` (Bubble) is explicitly solid.
- `WorldFile.LoadWorld` invokes `Liquid.QuickWater(2)` where `2` is verbosity, not a y-coordinate or scan bound; the normal-world load therefore runs the complete QuickWater scan.
- The normal 1.4.5.8 post-load liquid sequence is `QuickWater -> WorldGen.WaterCheck -> quickSettle UpdateLiquid until the active queue is empty or 100000 iterations are reached -> WorldGen.WaterCheck`. Reaching the 100000 guard does not fail loading; vanilla continues to the final WaterCheck.
- `TileObjectData.UsesGlobalLiquidChecks` is initialized `true` and is not assigned `false` in the 1.4.5.8 source, so `CheckWaterDeath` / `CheckLavaDeath` resolve to the final global `Main.tileWaterDeath` / `Main.tileLavaDeath` tables for this version.
- The final 1.4.5.8 `tileWaterDeath` set contains 10 tile types and the final `tileLavaDeath` set contains 267 tile types. Loading-time supported liquid-death removal is itemless because the world is still in the generating/loading state.

Known liquid gaps that must not be guessed:

- complex `WorldGen.ReplaceTile` dependency/shape/actuator/object cases beyond the currently supported safe single-cell active-merge subset;
- Remix/Zenith load-time liquid remapping until the generation-only lava-line/ocean-depth inputs consumed by `SettleWaterAt` are represented in the load context;
- panic/forced-settle lifecycle beyond the verified loading quick-settle scheduler slice.

## Cross-world spawn echo

Primary evidence: TerrariaServer 1.4.5.8 `Player.Spawn(PlayerSpawnContext)` and `MessageBuffer` packet-12 handling.

Verified facts used by the live transfer gate:

- when `Player.Spawn` runs with `PlayerSpawnContext.SpawningIntoWorld`, it calls `FindSpawn` and validates the personal spawn; an invalid personal spawn may leave `SpawnX/SpawnY` as `-1/-1`;
- a multiplayer client sends packet 12 back after that spawn transition;
- therefore the immediate packet-12 `SpawningIntoWorld` response to TerraRuntime's synthetic replacement-world spawn is an acknowledgement/echo of the handoff, not a fresh authoritative respawn request;
- ordinary post-join packet-12 respawn remains a separate path and must not be globally suppressed.

## Working rule for new vanilla facts

When adding a fact:

1. name the official type/method or table used as evidence;
2. state only the behavior needed by TerraRuntime;
3. put literal IDs/constants here only after they are source-pinned and represented by typed production constants where appropriate;
4. add a regression test that fails under the previous/broken behavior;
5. update/remove the corresponding "known gap" entry.
