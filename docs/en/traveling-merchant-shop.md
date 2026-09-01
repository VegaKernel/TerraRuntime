# Traveling Merchant shop authority (1.4.5.8)

This slice pins the Traveling Merchant inventory algorithm and packet-72 wire image to TerrariaServer 1.4.5.8 without pretending that TerraRuntime already owns every input required to regenerate the shop at runtime.

## Source-backed inventory primitive

`VanillaTravelingMerchantShop1458` mirrors `Chest.SetupTravelShop` and its helper methods. It preserves the source RNG order, including independent overwrite-style item rolls, the hardmode minimum-rarity search, the 5,000-attempt rarity relaxation, the ordinary unbounded selection loop, the painting pass, duplicate rejection and bundled vanity/decorative sets. The result is the exact 40-slot `Main.travelShop` image with a zero-filled tail.

World/progression inputs are explicit in `VanillaTravelingMerchantWorldFacts1458`. Player luck is also explicit: the caller supplies the current highest active player luck selected by vanilla `Player.GetPlayerWithHighestLuck`. `Luck.RollLuck` itself is implemented inside the primitive over an injected `Main.rand`-shaped random source, so positive/negative luck consumes random calls in source order rather than being approximated by a simple probability multiplier.

## Packet 72

`TerrariaTravelShopCodec` owns protocol-326 packet `72`. Its payload is exactly 40 little-endian signed `Int16` item identities, 80 payload bytes and an 83-byte complete Terraria frame. Negative item identities are rejected on the server encoding boundary; zero remains the vanilla empty-slot sentinel.

The pinned source contract verifies `Chest.SetupTravelShop`, `Player.GetPlayerWithHighestLuck`, `Player.RollLuck`, `Luck.RollLuck`, the Traveling Merchant spawn-time shop refresh, packet-72 broadcast, packet-72 client receive path and join-time travel-shop synchronization against TerrariaServer 1.4.5.8.

## Runtime ownership gate

The existing town-commerce resolver still treats the live Traveling Merchant inventory as an explicit missing fact. That is deliberate. Vanilla regenerates the inventory when the Traveling Merchant spawn path runs and bases `RollLuck` on the highest-luck active player at that moment. TerraRuntime does not yet own authoritative player-luck state, so wiring this primitive with a fabricated `0f` luck value would create false parity.

The next runtime step is therefore narrow: project authoritative player luck, invoke this generator from the Traveling Merchant lifecycle, retain the generated 40-slot image, publish packet 72 to playing peers/joiners, and pass the stored image into `VanillaSpecialTownShopCatalog1458`. Until that input is owned, the core and wire layers are complete while the runtime lifecycle remains fail-closed.
