# King Slime Expert and Master loot semantics

TerraRuntime now owns the source-backed gameplay semantics for King Slime's difficulty-only loot rules from TerrariaServer 1.4.5.8. This slice deliberately keeps the three delivery shapes distinct instead of flattening them into ordinary shared world-item drops.

## Player interaction ownership

Terraria's boss-bag and per-player Master rules use `NPC.playerInteraction[playerSlot]`. TerraRuntime projects that state through `RuntimeNpcPlayerInteractionLedger`:

- an interaction is recorded only after a player item/projectile damage transition has committed;
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

`IKingSlimeDifficultyLootDeliverySink` models that as one logical instanced item plus an ordered recipient span and an explicit `54000`-tick slot lease. The gameplay evaluator materializes the bag before evaluating later Master rules so `Item.NewItem` RNG remains interleaved in source order.

The packet-90 encoder and the concrete leased-slot transport adapter are **not** part of this slice yet. The contract stays explicit so production code cannot falsely substitute a globally visible packet-21 world item for the boss bag.

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

This closes the authoritative gameplay rule semantics and interaction accounting. Remaining work is the concrete packet-90/leased-slot adapter plus the still-open King Slime death-time world effects such as Slime Rain termination and the first-kill Nerdy Slime unlock/spawn path.
