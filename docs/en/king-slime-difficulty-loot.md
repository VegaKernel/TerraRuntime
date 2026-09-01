# King Slime Expert and Master loot semantics

TerraRuntime owns the source-backed gameplay semantics for King Slime's difficulty-only loot rules from TerrariaServer 1.4.5.8. The three delivery shapes remain distinct instead of being flattened into ordinary shared world-item drops.

## Player interaction ownership

Terraria's boss-bag and per-player Master rules use `NPC.playerInteraction[playerSlot]`. TerraRuntime projects that state through `RuntimeNpcPlayerInteractionLedger`:

- after the exact NPC generation is accepted, a player item/projectile attack records interaction before the later strike outcome, matching packet-28 source order;
- an invulnerable generation-valid target can therefore still record the player interaction even when the damage transition itself is rejected;
- stale NPC generations and malformed damage requests never receive interaction credit;
- the NPC side is keyed by the exact generation-safe `NpcHandle`, so a reused NPC slot cannot inherit old interactions;
- the source player identity remains the Terraria player slot;
- death-time delivery re-checks which recorded slots currently contain active players;
- qualifying slots are processed in ascending `0..254` order, matching the source loops.

Environment/server/NPC damage does not grant player interaction credit.

## Expert boss bag

The pinned King Slime rule is `BossBag(3318)`. On the server it resolves to `DropLocalPerClientAndResetsNPCMoneyTo0` and consumes raw RNG rather than `RollLuck`:

1. `Next(1)` for the guaranteed rule chance;
2. `Next(1, 2)` for the shared stack;
3. one no-broadcast item materialization at the NPC;
4. packet 90 to each currently active interacting player;
5. the server turns its copy to air but keeps that world-item slot unavailable for `54000` ticks.

`RuntimeKingSlimeDifficultyLootDeliverySink` now implements that transport boundary. It materializes the item through the same source-backed world-item materializer, reserves an unpublished exact slot, encodes packet 90 with the byte-for-byte packet-21 payload shape, and sends it only to the requested playing player slots. The reservation is represented by `RuntimeWorldItemInstancedLeaseStore`, so ordinary item allocation cannot reuse the slot while the instanced client copy exists.

When a lease reaches zero, `TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease` emits the five-byte packet 151 contract carrying the released item slot. Production advances these leases once per authoritative item phase, after NPC and projectile phases, so a Boss Bag created during NPC death consumes its first lease tick in the same world update just like the source item loop.

## Master relic

`MasterModeCommonDrop(4929)` resolves to a raw-RNG `CommonDropNotScalingWithLuck` in Master mode:

1. `Next(1)` chance roll;
2. `Next(1, 2)` stack roll;
3. immediate ordinary world-item materialization at the King Slime death origin.

The materialization occurs before the Master pet rule begins.

## Master pet item

`MasterModeDropOnAllPlayers(4797, 4)` is not a direct inventory grant. TerrariaServer 1.4.5.8:

1. chooses the shared stack once with `Next(1, 2)`;
2. loops active interacting player slots in ascending order;
3. performs raw `Next(4)` for each slot;
4. on success, immediately creates an ordinary world item at that player's center before rolling the next player.

TerraRuntime preserves that ordering, including the interleaving between successful item materialization RNG and the next player's `Next(4)` roll.

## Finalization boundary

`RuntimeKingSlimeDifficultyLootFinalizer` accepts only dead King Slime generations in Expert/Master contexts. It captures active interacting players, executes the ordered difficulty rules and despawns the exact NPC generation only after delivery succeeds. Normal mode remains owned by the existing normal-loot transaction.

The rule semantics, packet-90/151 wire representation, leased-slot storage and live runtime boundary are now connected. Packet 28 enters the bounded gameplay ingress, records `playerInteraction` before strike resolution, executes the implemented Expert/Master loot before King Slime death effects, then relays the strike and death sync in source order. Slime Rain stop, first-kill Nerdy Slime and progression remain part of the same authoritative death transaction.
