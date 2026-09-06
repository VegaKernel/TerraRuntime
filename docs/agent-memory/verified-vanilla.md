# Verified vanilla facts

Last evidence refresh: 2026-09-07.

This file stores concise facts already checked against the locally decompiled official TerrariaServer **1.4.5.8**. It prevents repeated source archaeology, but it does not authorize guessing adjacent behavior. Unknown cases remain fail-closed until separately verified.

## Runtime bot source facts

Primary evidence: TerrariaServer 1.4.5.8 `Player.QuickHeal`, `Player.QuickHeal_GetItemToUse`, `Player.UpdateBuffs`, `Player.PickAmmo`, `Player.WingMovement`, `Item.SetDefaults`, `Projectile.SetDefaults`, `Projectile.AI_001`, `Collision.CanHit`, `MessageBuffer.GetData` packet cases 13/30/55 and `NetMessage.SendData` packet cases 13/30/55.

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
- Copper Broadsword is item `3508`, Wooden Bow is item `39`, Musket is item `96`, Wooden Arrow is item `40`, and Musket Ball is item `97`. Their admitted damage, use timing, projectile and ammo conversions come from the existing 1.4.5.8 item/weapon catalogs rather than bot-local guesses.
- Wooden Bow plus Wooden Arrow resolves through `Player.PickAmmo` to launch magnitude `9.1`; Musket plus Musket Ball resolves to `13`. aiStyle-1 arrow gravity begins after 15 projectile substeps at `+0.1` vertical velocity and caps at `16`; projectile `extraUpdates` controls the number of substeps per game tick.
- Packet 13 control bits are up `0`, down `1`, left `2`, right `3`, jump `4`, use-item `5`, direction-right `6`, and pulley/dash `7`. Movement flag bit `2` carries velocity. A server-owned PlayerBot must publish the actual selected hotbar slot and use-item bit for observers to render weapon switching/use.
- Fishron Wings are item `2609` with wing slot `26`; Soaring Insignia is item `4989`. The admitted bot flight step uses the source Fishron-wing vertical acceleration/cap slice and does not infer other accessory effects.
- `Collision.CanHit` is a tile-aware rectangle visibility test. Bot Guard acquisition and projectile admission reuse the runtime's exact `VanillaWorldCanHit` port; a solid obstacle is not ignored merely because target range is valid.
- AI_002 (`FloatingEye`) performs collision rebound before target-direction steering and applies source-specific horizontal/vertical pursuit acceleration. The controlled NpcBot lane reuses only this verified steering/motion primitive; daylight/despawn/attack side effects remain outside actor-control unless separately admitted.
- AI_005 (`EaterOfSouls`) resolves a target and then applies source-specific pursuit velocity/collision behavior. Controlled flyers reuse the verified pursuit primitive but do not opt back into ordinary AI projectile or spawn side effects.
- ordinary AI_014 bats set no-gravity, rebound from `collideX/collideY`, call `TargetClosest`, apply directional pursuit acceleration, then advance `ai[1]`; the wander branch starts only after `ai[1] > 200`. TerraRuntime's controlled bat helper resets that ordinary AI clock for each call and therefore exposes the source-ordered pre-wander pursuit/collision slice only. Queen Slime's purple minion shares AI_014 machinery but is boss-owned and is explicitly excluded from the standalone bot-preset helper.

## Dungeon entrance source facts

Primary evidence: TerrariaServer 1.4.5.8 `DungeonCrawler.SetupDungeonDataVariables`, `DungeonUtils.SetOldManSpawnAndSpawnOldManIfDefaultDungeon`, `WorldGen.beachDistance`, and the final `TileID.Sets.Clouds` initialization.

- An ordinary precalculated entrance search starts from y `10`, scans downward while the tile is inactive and has neither liquid nor wall, and accepts only x strictly inside `WorldGen.beachDistance == 380` from both world edges.
- The search initializes its countdown to `3000`, decrements before evaluating a candidate and stops at zero, so it can evaluate at most `2999` candidates. Each candidate x is drawn within 100 tiles of the Reset-owned dungeon location.
- Acceptance rejects a cloud-set tile within radius `15` of the surface exit or within radius `50` around `max(50, y - 50)`, and requires `y - 40 - RoughHeight > 0`. The final 1.4.5.8 cloud set used by this check is tiles `189`, `196`, `460`, `717`, `718`, and `719`.
- After acceptance, the horizontal dungeon location receives the source `+25-Next(50)` adjustment while the entrance position remains the accepted surface coordinate.
- For the default dungeon, Old Man is NPC type `37` at `dungeonX * 16 + 8, dungeonY * 16`, with `homeless=false` and home tile set to the dungeon anchor.

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
- `Liquid.maxLiquid` is `25000`. On an ordinary dedicated server, `UpdateLiquid` counts active players only in slots `0..14`, sets `cycles = 10 + activePlayers / 3`, and sets `curMaxLiquid = 25000 - activePlayers * 250` unless the reduced-max-liquid setting forces `5000`. Each cycle processes the corresponding `curMaxLiquid / cycles` slice, with the last cycle extending to the current active count.
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

## Mining, drills and join inventory ordering

Primary evidence: TerrariaServer 1.4.5.8 `MessageBuffer.GetData` case `3`, `NetMessage.SyncConnectedPlayer`, `Mount.UseDrill`, `Mount.drillPickPower`, `Player.PickTile`, and `ItemID.Sets.IsDrill`/item defaults.

- On receipt of the client slot assignment (packet 3), the vanilla multiplayer client sends all 59 ordinary inventory packet-5 entries before it sends packet 6 requesting world data. Pre-world-request inventory is therefore normal join ordering, not malformed gameplay traffic.
- Packet 13 carries `selectedItem`, `controlUseItem` and mount state; the client drill/pick path ultimately emits ordinary packet-17 tile manipulation.
- Drill Containment Unit is mount type `8`. `Mount.UseDrill` requires the drill ability to be active and `controlUseItem`, uses static `drillPickPower = 210`, and calls `Player.PickTile` for its selected beam target. The mount ability-active bit itself is not represented by packet 13, so TerraRuntime's admitted server slice additionally requires the DCU summon item `2768` in ordinary inventory and otherwise fails closed.
- Source-backed drill IDs needed by the current pick catalog include Cobalt Drill `385`, Nebula Drill `2779`, Solar Flare Drill `2784`, and Stardust Drill `3464`; the latter three inherit pick power `225` through the shared defaults branch.
- `Player.BordersMovement` reserves a 640-pixel edge band in normal-sized worlds. A mining regression whose movement sample sits inside that band has not proved selected-slot/control state reached the authoritative player; production-order tests must use an accepted position and assert the committed packet-13 snapshot before packet 17.

## Explosive projectiles and terrain destruction

Primary evidence: TerrariaServer 1.4.5.8 `Item.SetDefaults`, `Player.PickAmmo`, `Projectile.SetDefaults`, `Projectile.AI_016_Bombs`, `Projectile.AI_147_Celeb2Rocket`, `Projectile.Kill_ExplodeTiles`, `Projectile.ShouldWallExplode`, and `Projectile.CanExplodeTile`.

- Celebration Mk2 is item `3930`: damage `50`, knockback `10`, shoot speed `17`, use time/animation `6`, rocket ammo and holder projectile `714`. `Player.PickAmmo` transforms its rocket-ammo result as `715 + ammo.type - AmmoID.Rocket` rather than the ordinary launcher offset.
- Holder `714` is `22x22`, non-friendly, non-hostile, non-tile-colliding, ranged presentation/channel state. Children `715..718` are `14x14`, friendly ranged aiStyle `147`, penetrate `1`, timeLeft `1080`, tile-colliding and run with `extraUpdates = 2`. Their seven-pattern sequence emits one child except pattern 4 (two) and pattern 5 (three); pattern 3 launches at speed `9`, the others at `8`.
- Bomb `28` and Dynamite `29` are aiStyle `16`. The admitted Mini Nuke defaults cover `793..801` and `803..810` (`802` is not inferred): `14x14`, friendly ranged, penetrate `-1`, ordinary default lifetime and tile collision. Exact type membership, not aiStyle alone, decides the ordinary-fuse/grenade/mine/straight-rocket branch.
- `Kill_ExplodeTiles` is owner-local in vanilla (`owner == Main.myPlayer`) and sends packet 17 for successful tile/wall kills. TerraRuntime instead lets only a provenance-trusted authoritative termination perform that irreversible mutation and treats the matching client packet-17 stream as convergence echo.
- The destruction membership test is strict Euclidean distance `< radius`, not `<=`. Celebration Rocket IV `718` uses center-based radius `5`; Mini Nuke II rocket/grenade/mine `796/797/798` use position-based radius `7`. Celebration Rocket I/III `715/717` have no `Kill_ExplodeTiles` terrain branch.
- `CanExplodeTile` always rejects dungeon tile types (`Main.tileDungeon`), `TileID.Sets.BasicChest`, temple wall `350`, and the exact hard-protected tile switch. It conditionally gates hardmode ores, Demon/Crimson Altar, Underworld Hellstone, and the remaining world-state-dependent cases. Wall removal is enabled only when `ShouldWallExplode` finds an empty wall cell inside the same strict radius; wall `350` is never killed. Unrepresented object/world-state cases remain fail-closed.

## World-item reservation and cursor transfer

Primary evidence: TerrariaServer 1.4.5.8 `Main.UpdateServer`, `WorldItem.FindOwner`, `MessageBuffer.GetData` packet cases `21`/`22`, packet-5 inventory handling, and `Player.dropItemCheck`.

- `Main.UpdateServer` calls `FindOwner` for an unreserved active world item when its reservation timer modulo 5 equals 1. `FindOwner` skips inactive/dead players, consults `ItemSpace`/`CanPullItem`, compares candidates by source Manhattan-distance expression (including supported magnet modifiers), and checks grab-range intersection before changing the reservation.
- Server packet-21 handling ignores an existing item when `playerIndexTheItemIsReservedFor != whoAmI`; a different playing client therefore has no vanilla authority to remove or rewrite another player's reserved item. Packet 22 is a server-to-client item-owner projection in this path, not a client ownership grant.
- Packet-5 slot `58` maps to the owning client's `Main.mouseItem`. `Player.dropItemCheck` copies the mouse item into inventory slot 58 and, when the inventory closes, runs `GetOrDropItem`, clears `Main.mouseItem`, and clears inventory slot 58. A cross-world snapshot racing that client transition can therefore neither replay the old cursor image nor guess the full `GetItem`/special-storage semantics.
- TerraRuntime admits the conservative lossless subset: before detach it moves the whole cursor stack only into an empty ordinary main slot `0..49`; if none exists, it cancels the transfer while the source player is still attached. This is an intentional fail-closed policy built from the verified vanilla role of slot 58, not a claim that vanilla `GetItem` itself is limited to empty main slots.

## Working rule for new vanilla facts

When adding a fact:

1. name the official type/method or table used as evidence;
2. state only the behavior needed by TerraRuntime;
3. put literal IDs/constants here only after they are source-pinned and represented by typed production constants where appropriate;
4. add a regression test that fails under the previous/broken behavior;
5. update/remove the corresponding "known gap" entry.
