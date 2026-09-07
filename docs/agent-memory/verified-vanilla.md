# Verified vanilla facts

Last evidence refresh: 2026-09-07.

## TZ-35 targeting and mirror evidence

- `NPC.SetDefaults` resets friendly=false, chaseable=true, immortal=false. Source exceptions are pinned in `VanillaNpcChaseability1458`; in particular clone 440 and Ancient Doom 523 are unchaseable. `NPC.CanBeChasedBy` requires active, chaseable, lifeMax>5, !friendly, !immortal (ordinary debug settings), and !dontTakeDamage unless explicitly ignored. The runtime additionally excludes dead/catchable candidates conservatively; temporary catchable immunity remains incomplete.
- `AI_069_DukeFishron`: executed states 10/12 set chaseable=false, 11 sets true, other branches preserve it. Do not derive flags from the *resulting* ai[0]: the transition tick retains the old branch's flags.
- AI69 ordinary phase-three state10 uses horizontal hover offset 360 and selects dash11 for cycle 0/2/3/5/6/7, teleport12 for 1/4/8. State12 teleports only at incoming ai[2]==15: resolve ai[1]=300*sign(npc.Center.X-player.Center.X) when zero, then center=player.Center+(-ai[1],-200). Velocity multiplies by .98, then Y lerps toward zero by .02. Its outgoing 30-tick boundary increments the cycle and wraps at 9. State10/11 alpha changes by +25/-25, state12 by +17. Ocean/enrage and other phase details are not covered by this slice.
- `AI_084_LunaticCultist`: intro is unchaseable/invulnerable; movement state 1 is invulnerable but does not set the chaseability gate; ritual state 5 is unchaseable throughout and invulnerable while its incoming timer is below 120. Timer 119->120 is still invulnerable; 419->idle remains unchaseable for that tick. Ritual-hit abort occurs before branch evaluation.
- `Item.SetDefaults(50)`: Magic Mirror, width/height 20, useStyle=4, useTime/useAnimation=90. `Player.ItemCheck` invokes recall when itemTime==useTime/2. `Player.Spawn_SetPosition` maps floor tile to (x*16+8-width/2, y*16-height). Packet 12 transmits SpawnX/Y and PlayerSpawnContext.RecallFromItem=2 before the observer calls Spawn. Bot follow relocation is custom server policy, not a claim that vanilla mirrors target players; its recall presentation supplies the bot's landing floor instead of retaining an unrelated observer spawn.
- `Player.PickAmmo`: Magic Quiver scales arrow launch speed by 1.1. Random loadout tests must account for the equipped accessory, not assume every Platinum Bow + Unholy Arrow launches at 10 rather than 11.

This file stores concise facts already checked against the locally decompiled official TerrariaServer **1.4.5.8**. It prevents repeated source archaeology, but it does not authorize guessing adjacent behavior. Unknown cases remain fail-closed until separately verified.

## Runtime bot source facts

Primary evidence: TerrariaServer 1.4.5.8 `Player.QuickHeal`, `Player.QuickHeal_GetItemToUse`, `Player.UpdateBuffs`, `Player.ApplyEquipFunctional`, `Player.HorizontalMovement`, `Player.Update_NPCCollision`, `Player.PickAmmo`, `Player.WingMovement`, `Item.SetDefaults`, `Projectile.SetDefaults`, `Projectile.AI_001`, `Collision.CanHit`, `MessageBuffer.GetData` packet cases 13/23/30/55 and `NetMessage.SendData` packet cases 13/23/30/55.

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
- Fishron Wings are item `2609` with wing slot `26`; Soaring Insignia is item `4989`, Terraspark Boots are `5000`, Magiluminescence is `5107`, and Master Ninja Gear is `984`. Terraspark sets `accRunSpeed=6.75`, enables rocket boots and adds `0.08` to move speed. Grounded Magiluminescence multiplies run acceleration and slowdown by `1.75` and both run-speed caps by `1.15`. The admitted bot physics uses these exact horizontal parameters plus the existing Fishron-wing vertical slice; Master Ninja dash and other accessory side effects remain fail-closed.
- `Player.Update_NPCCollision` on the client skips only inactive, friendly or non-positive-damage NPC state, but packet 23 does not transmit a per-instance `friendly` or `damage` field. A hostile-type presentation actor with server-only `DamageOverride=0` must therefore remain spatially separated from the followed player to avoid local contact-hurt presentation; authoritative contact damage still resolves the stored override and remains zero.
- `Collision.CanHit` is a tile-aware rectangle visibility test. Bot Guard acquisition and projectile admission reuse the runtime's exact `VanillaWorldCanHit` port; a solid obstacle is not ignored merely because target range is valid.
- AI_002 (`FloatingEye`) performs collision rebound before target-direction steering and applies source-specific horizontal/vertical pursuit acceleration. The controlled NpcBot lane reuses only this verified steering/motion primitive; daylight/despawn/attack side effects remain outside actor-control unless separately admitted.
- AI_005 (`EaterOfSouls`) resolves a target and then applies source-specific pursuit velocity/collision behavior. Controlled flyers reuse the verified pursuit primitive but do not opt back into ordinary AI projectile or spawn side effects.
- ordinary AI_014 bats set no-gravity, rebound from `collideX/collideY`, call `TargetClosest`, apply directional pursuit acceleration, then advance `ai[1]`; the wander branch starts only after `ai[1] > 200`. TerraRuntime's controlled bat helper resets that ordinary AI clock for each call and therefore exposes the source-ordered pre-wander pursuit/collision slice only. Queen Slime's purple minion shares AI_014 machinery but is boss-owned and is explicitly excluded from the standalone bot-preset helper.

## Larva destruction source facts

Primary evidence: TerrariaServer 1.4.5.8 `WorldGen.Check3x3`, `WorldGen.KillTile`, `Projectile.CutTiles` and `Projectile.ExplodeTiles`.

- Larva is frame-important tile `231` and resolves as one complete 3x3 object with 18-pixel frame steps. A corrupt/incomplete footprint is not a valid object mutation target.
- After Larva destruction, `WorldGen.Check3x3` scans live, non-dead players in slot order, minimizes Manhattan distance from the tile source and calls `NPC.SpawnOnPlayer(player, 222)` only when the nearest distance is strictly below `4800` pixels.
- Tile `231` is in the final `tileCut` set and is not excluded by `Projectile.CanExplodeTile`; admitted server projectile cutting and trusted terrain explosions therefore reach the same framed-object/Queen Bee authority boundary as an accepted pick break.

## Dungeon entrance source facts

Primary evidence: TerrariaServer 1.4.5.8 `DungeonCrawler.SetupDungeonDataVariables`, `DungeonUtils.SetOldManSpawnAndSpawnOldManIfDefaultDungeon`, `WorldGen.beachDistance`, and the final `TileID.Sets.Clouds` initialization.

- An ordinary precalculated entrance search starts from y `10`, scans downward while the tile is inactive and has neither liquid nor wall, and accepts only x strictly inside `WorldGen.beachDistance == 380` from both world edges.
- The search initializes its countdown to `3000`, decrements before evaluating a candidate and stops at zero, so it can evaluate at most `2999` candidates. Each candidate x is drawn within 100 tiles of the Reset-owned dungeon location.
- Acceptance rejects a cloud-set tile within radius `15` of the surface exit or within radius `50` around `max(50, y - 50)`, and requires `y - 40 - RoughHeight > 0`. The final 1.4.5.8 cloud set used by this check is tiles `189`, `196`, `460`, `717`, `718`, and `719`.
- After acceptance, the horizontal dungeon location receives the source `+25-Next(50)` adjustment while the entrance position remains the accepted surface coordinate.
- For the default dungeon, Old Man is NPC type `37` at `dungeonX * 16 + 8, dungeonY * 16`, with `homeless=false` and home tile set to the dungeon anchor.

## Underground house source facts

Primary evidence: TerrariaServer 1.4.5.8 `WorldGen` pass `Underground Houses and Buried Chests`, `CaveHouseBiome`, `HouseUtils`, `HouseBuilder`, the seven house palette builders, `StructureMap`, `SetFactory`, and `WorldGenRange`.

- The pass draws its budgets before placement in this order: area-scaled `CaveHouseCount 35..40`, width-scaled `UnderworldChestCount 10..15`, area-scaled `CaveChestCount 35..40`, then area-scaled fixed `AdditionalDesertHouseCount 2`. Placement then runs cave chests, Underworld chests, ordinary cave houses and additional desert houses.
- Cave-house room search scans down by at most `200`, searches each side by `25` and upward by `10`, clamps room width to `15..30` and height to `8..12`, and admits the optional upper/lower rooms through their solid percentage plus `0.2` tests.
- `StructureMap` rejects the exact source tile set `225, 41, 43, 44, 226, 203, 112, 25, 151, 21, 467`; non-Granite houses also reject lava in the padded rooms. All ordinary `CaveHouseBiome` chest chances are `1.0`, so an admitted house without its configured persistent chest is invalid.
- The palette defaults used by the current structural slice are Wood `30/27/124/0/0/chest 21 style 1`, Ice `321/149/574/19/30/chest 21 style 11`, Desert `396/187/577/42/43/chest 467 style 10`, Jungle `158/42/575/2/2/chest 21 style 8`, Mushroom `190/74/578/18/6/chest 21 style 32`, Granite `369/181/576/28/34/chest 21 style 50`, and Marble `357/179/561/29/35/chest 21 style 51`; each tuple is tile/wall/beam/platform/door/chest. Decorative furniture and aging are not implied by these palette facts and remain unported.

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

## Player buff synchronization and hostile projectile debuffs

Primary evidence: TerrariaServer 1.4.5.8 `Projectile.Damage`, `Projectile.Damage_EVP`, `Projectile.StatusPlayer`, `Projectile.ApplyBuffTo`, `MessageBuffer.GetData` packet cases `3`/`50`/`55`, `NetMessage.SendData` packet case `50`, and `Player.maxBuffs`.

- packet `50` (`PlayerBuff`) encodes `[player byte][zero or more buff ushort][zero ushort terminator]`; `Player.maxBuffs` is `44`. It carries buff **types only**, never durations.
- on dedicated-server ingress, packet `50` discards the claimed player byte and replaces it with `whoAmI`. Each reported nonzero type is mirrored with temporary server-side `buffTime = 60`, the remainder of the arrays is cleared, and packet `50` is relayed to peers. The client also sends packet `50` during the normal join handshake, and `SyncOnePlayer` includes it in late-join player baselines.
- hostile projectile player-status application is not a dedicated-server execution path: `Projectile.Damage()` calls `Damage_EVP` only when `Main.netMode != 2`. A multiplayer client that survives a hostile projectile `Hurt` runs `StatusPlayer` locally and later synchronizes the resulting active buff-type list through packet `50`.
- Cultist Fireball `467` is one concrete example: `StatusPlayer` locally calls `ApplyBuffTo(player, 24, Main.rand.Next(30, 150))`, while `Kill()` expands the projectile to a `176x176` damage area and calls `Damage()` again. The exact PvE debuff duration/RNG result is therefore not transmitted to the dedicated server by packet `50`.
- packet `55` must not be repurposed to manufacture those PvE durations. It is the separate targeted PvP `AddPlayerBuffPvP` path described above; independently rolling a server duration and delivering packet `55` would not be vanilla packet semantics and can extend or alter the client-owned result.
- TerraRuntime packet-50 state is consequently presentation/synchronization evidence only. It may be generation-scoped, relayed, late-join baselined and transferred, but it is not by itself proof that an authoritative combat modifier with a known remaining duration exists.

## World-item reservation and cursor transfer

Primary evidence: TerrariaServer 1.4.5.8 `Main.UpdateServer`, `WorldItem.FindOwner`, `MessageBuffer.GetData` packet cases `21`/`22`, packet-5 inventory handling, and `Player.dropItemCheck`.

- `Main.UpdateServer` calls `FindOwner` for an unreserved active world item when its reservation timer modulo 5 equals 1. `FindOwner` skips inactive/dead players, consults `ItemSpace`/`CanPullItem`, compares candidates by source Manhattan-distance expression (including supported magnet modifiers), and checks grab-range intersection before changing the reservation.
- Server packet-21 handling ignores an existing item when `playerIndexTheItemIsReservedFor != whoAmI`; a different playing client therefore has no vanilla authority to remove or rewrite another player's reserved item. Packet 22 is a server-to-client item-owner projection in this path, not a client ownership grant.
- Packet-5 slot `58` maps to the owning client's `Main.mouseItem`. `Player.dropItemCheck` copies the mouse item into inventory slot 58 and, when the inventory closes, runs `GetOrDropItem`, clears `Main.mouseItem`, and clears inventory slot 58. A cross-world snapshot racing that client transition can therefore neither replay the old cursor image nor guess the full `GetItem`/special-storage semantics.
- TerraRuntime admits the conservative lossless subset: before detach it moves the whole cursor stack only into an empty ordinary main slot `0..49`; if none exists, it cancels the transfer while the source player is still attached. This is an intentional fail-closed policy built from the verified vanilla role of slot 58, not a claim that vanilla `GetItem` itself is limited to empty main slots.


## Projectile-local NPC immunity and controlled-magic reacquisition

Primary evidence: TerrariaServer 1.4.5.8 `Projectile.SetDefaults`, `Projectile.Damage_PVE`, `Projectile.Damage_PVE_Inner`, `Projectile.FindTargetWithLineOfSight`, `Projectile.DecrementLocalImmuneTimeCounters`, and the type-specific post-hit branch for projectiles `34`/`79`.

- Flamelash `34` and Rainbow Rod `79` both set `usesLocalNPCImmunity = true` and `localNPCHitCooldown = 12`; their source penetration is `2` and `3` respectively.
- `Damage_PVE` rejects an NPC while the projectile's corresponding `localNPCImmunity[npcSlot]` cell is nonzero. After a successful ordinary local-immunity hit, `Damage_PVE_Inner` clears the owner's shared `NPC.immune` cell and writes the projectile's `localNPCHitCooldown` into that local cell.
- `DecrementLocalImmuneTimeCounters` decrements only positive local-immunity cells once per projectile update; negative values remain nonzero/permanent. TerraRuntime represents the admitted slice as tick-bound generation-safe state rather than copying a mutable 200-slot array per projectile, but preserves the exact positive cooldown boundary and permanent-negative semantics.
- The source `FindTargetWithLineOfSight` scans physical NPC slots and explicitly rejects any candidate whose projectile `localNPCImmunity[i] != 0`. Target acquisition and collision therefore must consult the same logical immunity state; maintaining separate mirrors can cause a released projectile to immediately reacquire an NPC that vanilla still considers locally immune.
- After a successful hit, projectile type `34` or `79` with exact released state `ai[0] == -1` sets `ai[1] = -1` and requests a net update. On the following AI_009 target search, the just-hit NPC is skipped while its local-immunity cell remains nonzero, allowing another line-of-sight target to be selected. At the positive 12-tick boundary the same exact NPC generation becomes eligible again.
- TerraRuntime scopes each local-immunity cell by both exact `ProjectileHandle` generation and exact `NpcHandle` generation. Reusing either physical slot cannot carry an old local-immunity decision into the replacement entity.

## Projectile PvP status and packet 55

Primary evidence: TerrariaServer 1.4.5.8 `Projectile.StatusPvP`, the PvP branch of `Projectile.Damage()`, `Player.AddBuff`, `MessageBuffer.GetData` case `55`, `NetMessage.SendData` case `55`, and `Main.Initialize` assignments to `Main.pvpBuff`.

- In the PvP damage branch, vanilla checks projectile/player immunity and then calls `StatusPvP(targetSlot)` before `TryDoingOnHitEffects` and before `Player.Hurt(..., pvp: true, ...)`. `Player.Hurt` can subsequently avoid the damage because of Creative GodMode, so a legal PvP hit may still roll/apply its status even when HP does not change. Do not move the status proc after the HP commit merely because that ordering looks cleaner.
- For the currently admitted authoritative projectile set, source-pinned projectile-specific `StatusPvP` rules are: Fire Arrow `2` -> `On Fire!` buff `24`, `180` ticks, chance `1/3`; Flamelash `34` -> `On Fire!` `24`, `240` ticks, chance `1/2`; Poisoned Knife `54` -> `Poisoned` buff `20`, `600` ticks, chance `1/2`.
- `StatusPvP` also contains melee-enchant/equipment-derived effects and many projectile families not currently admitted. Those effects depend on owner state TerraRuntime does not yet own completely and therefore remain fail-closed rather than being inferred.
- `Player.AddBuff(type,time)` on a multiplayer client, when targeting a remote player, sends packet `55` only for a `Main.pvpBuff[type]` entry and does not mutate that remote player's local buff array. Packet `55` is exactly `[player byte][buff ushort][time int32]`.
- Dedicated-server packet-55 handling relays only when both encoded target and sender are hostile and `Main.pvpBuff[buff]` is true. Client handling applies the packet only when the encoded target equals `Main.myPlayer`, using `AddBuff(..., fromNetPvP: true)`. This is targeted owner delivery, not observer replication.
- The exact 1.4.5.8 `Main.pvpBuff` true set is `{20,70,24,323,31,39,44,324,69,103,119,120,137,320,30,36,397,398,399,400}`. TerraRuntime's packet-55 egress must remain bounded to this source-pinned set.

## Working rule for new vanilla facts

When adding a fact:

1. name the official type/method or table used as evidence;
2. state only the behavior needed by TerraRuntime;
3. put literal IDs/constants here only after they are source-pinned and represented by typed production constants where appropriate;
4. add a regression test that fails under the previous/broken behavior;
5. update/remove the corresponding "known gap" entry.
