# Town NPC housing and shop authority

TerraRuntime owns persistent town-NPC household state instead of treating the `.wld` NPC and `TownRoomManager` sections as bootstrap-only data.

`TownNpcAuthority` is the concrete world-scoped owner for town-NPC composition and authoritative lifecycle work. It owns housing validation, rescue/progression transforms, move-in scheduling, home scheduling, shop-session resolution, shimmer processing and town combat orchestration while remaining called only by the existing world writer. `ServerRuntimeState` routes typed commands and preserves tick order; it no longer stores the individual town services as unrelated fields.

## Implemented 1.4.5.8 slice

The runtime loads the persistent town roster and room mapping into `RuntimeTownNpcStateStore`, reserves stable runtime NPC slots for the loaded roster, and materializes source-pinned TerrariaServer 1.4.5.8 defaults for ordinary residents, town pets and town slimes. Old Man, Traveling Merchant and Skeleton Merchant remain outside this persistent household catalog.

Client packet `60` (`UpdateNpcHome`) is decoded as the exact seven-byte Terraria 1.4.5.8 payload. A client may request an assignment (`status = 0`) or kick-out (`status = 1`). `status = 2` remains server-authored state. Requests are accepted only from a playing connection and are committed on the authoritative game thread.

Housing assignment uses a clean-room source-shaped room check with the pinned room flood bounds, minimum/maximum room size, safe-wall continuity, `RoomNeeds` chair/table/torch/door sets, stinkbug restriction, evil-room score, standing-position selection and TownRoomManager housing-category occupancy. Ordinary residents cannot occupy the same room; a town pet/slime may share a room with an ordinary resident, matching the source housing-category rule. Truffle assignment follows the pinned 1.4.5.8 gate: a first move-in requires a functional surface room unless `Main.NoFunctionalSurface`, every accepted room needs at least 100 active mushroom tiles (`70`, `71`, `72`, `528`) inside the source-tested bounds, and the successful unlock is journaled into the lossless `.wld` header patch path.

Home-state commits are replicated as packet `60` to playing peers and retained as reconnect baselines. Save snapshots now detach both `WorldNpcPersistence` and `WorldTownRoom[]`; the lossless world rewriter replaces the NPC and town-room sections together with tiles/chests/signs instead of preserving stale household bytes.

## Shop inventory slice

The protocol-neutral shop catalogs, happiness calculation, town-spawn eligibility evaluator and its source-backed item predicates live in `TerraRuntime.Gameplay.Npcs`. The mutable per-world town-spawn cadence remains in Core because it is authoritative runtime state. Shop inputs use the shared `VanillaMoonPhase` identity from Contracts; out-of-range enum values fail before inventory resolution.

`VanillaTownShopCatalog1458` owns source-pinned `Chest.SetupShop` inventory membership for all ordinary vendor branches `1..18`, from Merchant through Stylist: Merchant, Arms Dealer, Dryad, Demolitionist, Clothier, Goblin Tinkerer, Wizard, Mechanic, Santa Claus, Truffle, Steampunker, Dye Trader, Party Girl, Cyborg, Painter, Witch Doctor, Pirate and Stylist.

The resolver preserves source order and the implemented progression inputs, including Hardmode, boss/event progression, Blood Moon/Eclipse/day/night, biome/graveyard/sky/beach state, secret-seed flags, world ore choice, live-town-NPC presence, golfer score, player life/mana/team/coin state and player-owned-item unlocks. Every resolved inventory is bounded to the vanilla 40-slot shop capacity.

`VanillaSpecialTownShopCatalog1458` owns the source-shaped special branches `19..25`: Traveling Merchant, Skeleton Merchant, Tavernkeep, Golfer, Zoologist, Princess and Painter's secondary decor shop. The model preserves sparse vanilla slots, custom coin prices and Defender Medal currency instead of flattening those shops into an ordinary item list. Traveling inventory, moon/time state, progression, bestiary completion, Golfer score and pylon eligibility are explicit inputs.

`VanillaTownHappiness1458` implements the numeric `ShopHelper` price path: biome preferences, the full admitted NPC relationship table, Princess loneliness/relationship handling, nearby-house/village crowding, homeless/far-home and evil-biome penalties, LoveStruck, and the vanilla `0.75..1.5` clamp/rounding behavior. Localized mood-report text remains outside this numeric primitive.

## Not claimed

This slice does **not** claim full town AI parity. AI style `7` defaults are admitted so persistent residents have correct hitbox/life/wire state, but day/night schedules, teleport-home behavior, doors/chairs, attacks, conversations, localized happiness dialogue, rescue/transform lifecycle and special-seed/shimmer branches remain separate parity gates.

## NPC talk / shop session sync

Packet 40 (`SetNpcTalk`) is now decoded on the connection boundary, posted to the authoritative loop and re-encoded with the authenticated player slot before replication. The client-provided player byte is never accepted as authority. NPC slot `-1` closes the conversation; live wire slots are bounded to `0..199`.

The ordinary vendor inventory table is guarded by a pinned TerrariaServer 1.4.5.8 `Chest.SetupShop` source contract in CI. Cases `1..18`, including the Santa and Painter range loops, must keep their exact source item sequence.

### Authoritative talk-to-shop mirror

Packet 40 now mirrors the server side of `Player.SetTalkNPC`: after authenticating the player slot, the authoritative game thread resolves the live NPC, snapshots packet-5 inventory/vitals/team state, scans the pinned `169x124` SceneMetrics window around the player, computes source-shaped housing crowding and numeric happiness, and resolves the ordinary `Chest.SetupShop` catalog or supported special shop into an immutable per-player session. Closing the conversation clears the session, and disconnect cannot leak it across a reused player generation.

The mirror is deliberately honest about still-unowned inputs. `LoveStruck`, live wind/weather, Golfer score, full Bestiary/Fairy Torch state, Artisan Bread and Traveling Merchant `travelShop` data are represented as explicit missing-fact flags rather than fabricated defaults being advertised as parity.

### Rescue and critter lifecycle

TerrariaServer 1.4.5.8 bound talk-rescue is now authoritative for Golfer Rescue, Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler and unconscious Tavernkeep. The transform keeps the NPC slot/generation, repositions from the old bottom edge like `NPC.Transform`, converts the slot into a persistent homeless resident and journals the corresponding `saved*` world flag for the lossless `.wld` header patch.

Packet 70 (`CatchNPC`) is decoded as the exact signed `Int16` NPC slot and committed on the game-loop owner. The runtime pins the complete `NPCID.Sets.CountsAsCritter` set separately from all verified `catchItem` mappings, reserves world-item capacity before despawning the NPC, creates the 12x12 captured-critter item at the authoritative player center with vanilla spawn velocity and reserves it for that authenticated player. Statue-spawned critters follow the no-item despawn branch. Mystic Frog remains fail-closed here because vanilla teleports it instead of catching it; Demon Tax Collector remains the separate Purification Powder projectile-10 transform path.
