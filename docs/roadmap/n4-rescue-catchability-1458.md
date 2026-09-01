# N4 rescue and catchability slice — TerrariaServer 1.4.5.8

This delivery closes the source-backed classic talk-rescue lifecycle for Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler, unconscious Tavernkeep and Golfer Rescue. Runtime transformation preserves the live NPC generation, applies source-shaped bottom-edge repositioning and life scaling, adopts the resulting resident into persistent homeless town state, and journals the corresponding `saved*` header flag.

Packet 70 (`CatchNPC`) is owned by the authoritative game loop. The runtime validates the authenticated connection and live NPC slot, uses the pinned `catchItem` mapping, reserves world-item capacity before despawn, emits a 12x12 captured-critter item at the authoritative player center, and reserves it for that player. Statue-spawned catchable NPCs use the source no-item despawn branch.

`NPCID.Sets.CountsAsCritter` is represented independently from `catchItem`, because the source contains critters that cannot be caught and catchable entities that are not classified as critters.

## Projectile-special NPC interactions

The follow-up slice now owns Purification Powder (`projectile 10`) NPC side effects: Demon Tax Collector `534 -> 441` reuses the generation-safe rescue transaction and journals `savedTaxCollector`; Mystic Frog `687 -> 683` becomes the Yellow Town Slime and journals `unlockedSlimeYellowSpawn`. The powder hitbox is source-pinned at 64x64, expanding to 106x106 in infected-seed worlds.

Packet 70 now owns the Mystic Frog special path too. It searches the source-shaped 15-tile teleport range with an 8-tile player telefrag exclusion and preserves the NPC generation on a successful teleport; if no legal tile is found after the vanilla 100 attempts, the frog is authoritatively despawned without producing a captured item. Teleport/smoke visuals remain presentation-only and do not alter the authoritative gameplay transaction.
