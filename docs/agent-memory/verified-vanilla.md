# Verified vanilla facts

Last evidence refresh: 2026-09-06.

This file stores concise facts already checked against the locally decompiled official TerrariaServer **1.4.5.8**. It prevents repeated source archaeology, but it does not authorize guessing adjacent behavior. Unknown cases remain fail-closed until separately verified.

## Runtime bot source facts

Primary evidence: TerrariaServer 1.4.5.8 `Player.QuickHeal`, `Player.QuickHeal_GetItemToUse`, `Player.UpdateBuffs`, `Player.PickAmmo`, `Item.SetDefaults`, `MessageBuffer.GetData` packet cases 30/55 and `NetMessage.SendData` packet cases 30/55.

Verified facts used by the TZ-29 bot slice:

- `Player.defaultItemGrabRange` is `42` pixels before accessory modifiers. Bot pickup uses that base range and does not invent accessory bonuses.
- ordinary potion delay is `Item.potionDelay = 3600` ticks. `Player.QuickHeal` refuses healing while dead, full-life or already under potion delay.
- QuickHeal candidates require `stack > 0`, `type > 0`, `potion == true` and `healLife > 0`. Its candidate ordering prefers the largest still-underhealing potion while all candidates under-heal, otherwise the smallest non-negative overheal. The special Restoration Potion type `227` adjustment is not part of the admitted bot healing subset.
- the source-pinned healing subset used by bots is Healing Potion `188` / `100 HP`, Greater Healing Potion `499` / `150 HP`, and Super Healing Potion `3544` / `200 HP`; each has the ordinary potion flag and a `14x24` item hitbox. Absence from the catalog is fail-closed.
- packet `30` encodes `[player byte][hostile bool]`. On a dedicated server an incoming player index is replaced with the sending connection's player slot before the authoritative hostile flag is stored and rebroadcast.
- packet `55` (`AddPlayerBuffPvP`) encodes `[player byte][buff ushort][time int32]`. The dedicated-server ingress path admits network PvP buffs only when the target and sender are both hostile and `Main.pvpBuff[buff]` is true. On a multiplayer client the packet is applied only when the encoded player slot equals `Main.myPlayer`; vanilla therefore uses packet `55` as targeted local-player PvP buff delivery, not as observer-visible remote-player buff replication. Server-owned fake-player bot combat buffs must not be broadcast to unrelated observers through packet `55`.
- Archery buff `16` sets the archery state and multiplies arrow damage by `1.1`; `Player.PickAmmo` additionally multiplies arrow speed by `1.2` when below `20`, capped at `20`.
- Wrath buff `117` adds `0.1` to melee, ranged, magic and minion damage multipliers. TZ-29 reproduces only the outgoing combat effects it actually owns; catalogued potion buffs without an implemented authoritative effect remain fail-closed for bot pickup/use.
- supported bot ammunition/weapon facts come from the existing source-backed projectile weapon/ammo catalog. TZ-29's ranged presets pair Wooden Bow with Wooden Arrow and Musket with Musket Ball; it does not infer arbitrary Terraria ammo compatibility.
- AI_002 (`FloatingEye`) performs collision rebound before target-direction steering and applies source-specific horizontal/vertical pursuit acceleration. The controlled NpcBot lane reuses only this verified steering/motion primitive; daylight/despawn/attack side effects remain outside actor-control unless separately admitted.
- AI_005 (`EaterOfSouls`) resolves a target and then applies source-specific pursuit velocity/collision behavior. Controlled flyers reuse the verified pursuit primitive but do not opt back into ordinary AI projectile or spawn side effects.
- ordinary AI_014 bats set no-gravity, rebound from `collideX/collideY`, call `TargetClosest`, apply directional pursuit acceleration, then advance `ai[1]`; the wander branch starts only after `ai[1] > 200`. TerraRuntime's controlled bat helper resets that ordinary AI clock for each call and therefore exposes the source-ordered pre-wander pursuit/collision slice only. Queen Slime's purple minion shares AI_014 machinery but is boss-owned and is explicitly excluded from the standalone bot-preset helper.

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
