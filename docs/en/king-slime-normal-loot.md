# King Slime normal-mode loot parity

TerraRuntime now owns the source-backed normal-mode King Slime NPC-specific loot path through the same generation-safe death and world-item transaction used by existing imported NPC loot.

## Imported rule order

The TerrariaServer 1.4.5.8 normal-mode sequence represented by this slice is:

1. King Slime Trophy, `1/10`;
2. Slimy Saddle, `1/4`;
3. King Slime Mask, `1/7`;
4. exactly one Ninja Hood/Shirt/Pants selection;
5. Slime Hook, `1/3`, with Slime Gun chained on the failed roll;
6. Solidifier, guaranteed;
7. Slime Staff, `1/30`.

The Hook rule is deliberately not converted to a luck-scaled `CommonDrop`: its primary chance consumes the raw RNG stream and only the failed branch enters the Common Slime Gun rule. Successful world items are materialized before the next rule is evaluated so item-spawn RNG remains interleaved with loot RNG.

## Transaction safety

`RuntimeNpcLootWorldItemTransaction` preflights every possible item identity, including all three Ninja options and both Hook/Gun branches, before reserving capacity or consuming loot RNG. It reserves capacity for the maximum seven emitted stacks and despawns only the exact dead NPC generation after all drops are staged.

Normal-mode King Slime can no longer bypass imported loot through `RuntimeNpcDeathLifecycleFinalizer`.

## Deliberate difficulty boundary

Expert and Master delivery are not represented as ordinary world drops. Treasure-bag delivery and Master per-player drops remain explicit unsupported work until their ownership and recipient semantics exist in the runtime. The context-aware lifecycle fallback can still finalize those dead generations without falsely claiming that their loot is implemented.
