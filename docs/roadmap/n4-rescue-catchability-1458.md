# N4 rescue and catchability slice — TerrariaServer 1.4.5.8

This delivery closes the source-backed classic talk-rescue lifecycle for Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler, unconscious Tavernkeep and Golfer Rescue. Runtime transformation preserves the live NPC generation, applies source-shaped bottom-edge repositioning and life scaling, adopts the resulting resident into persistent homeless town state, and journals the corresponding `saved*` header flag.

Packet 70 (`CatchNPC`) is owned by the authoritative game loop. The runtime validates the authenticated connection and live NPC slot, uses the pinned `catchItem` mapping, reserves world-item capacity before despawn, emits a 12x12 captured-critter item at the authoritative player center, and reserves it for that player. Statue-spawned catchable NPCs use the source no-item despawn branch.

`NPCID.Sets.CountsAsCritter` is represented independently from `catchItem`, because the source contains critters that cannot be caught and catchable entities that are not classified as critters.

## Explicit next boundary

This slice does not claim the Purification Powder projectile side effects. Demon Tax Collector (`534 -> 441`) and Mystic Frog powder transformation remain in the projectile-special-interaction slice. Packet-70 Mystic Frog capture is fail-closed here because Terraria teleports the frog instead of producing the ordinary caught item.
