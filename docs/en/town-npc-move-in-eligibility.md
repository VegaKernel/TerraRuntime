# Town NPC move-in eligibility

TerraRuntime now owns a source-backed TerrariaServer 1.4.5.8 projection of the town-NPC move-in eligibility pass.

The evaluator covers the vanilla `Main.UpdateTime_SpawnTownNPCs` candidate flags and the `7200 / WorldGen.GetWorldUpdateRate()` cadence boundary. It consumes authoritative player facts rather than transport packets: aggregate coin value, maximum life, and source-pinned inventory predicates for the Arms Dealer, Demolitionist, and Dye Trader.

Persisted rescue and unlock state is no longer discarded while reading a `.wld`. Goblin Tinkerer, Wizard, Mechanic, Angler, Stylist, Tax Collector, Golfer and Tavernkeep rescue state, together with Merchant, Demolitionist, Party Girl, Dye Trader, Arms Dealer, Nurse and Princess unlock state, survives both world parsing and the disposable prepared-world cache.

This slice intentionally stops at candidate eligibility. Physical house search, room-aware priority, actual NPC placement, move-in announcements, and day/night town schedules remain authoritative runtime work and are not claimed by this document.
