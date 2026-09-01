# N4 rescue и catchability — TerrariaServer 1.4.5.8

Этот блок закрывает source-backed classic talk-rescue lifecycle для Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler, лежащего без сознания Tavernkeep и Golfer Rescue. Runtime transform сохраняет live NPC generation, применяет source-shaped перенос по нижней границе и масштабирование life, добавляет получившегося resident в persistent homeless town state и журналирует соответствующий `saved*` флаг заголовка мира.

Packet 70 (`CatchNPC`) принадлежит authoritative game loop. Runtime проверяет authenticated connection и live NPC slot, использует закреплённый `catchItem` mapping, резервирует world-item capacity до despawn, создаёт 12x12 captured-critter item у authoritative player center и резервирует его за этим игроком. Catchable NPC, появившиеся от statue, используют source no-item despawn branch.

`NPCID.Sets.CountsAsCritter` хранится отдельно от `catchItem`, потому что в исходнике есть critter, которых нельзя поймать, и catchable существа, которые не считаются critter.

## Явная следующая граница

Этот блок не заявляет Purification Powder projectile side effects. Demon Tax Collector (`534 -> 441`) и Mystic Frog powder transformation остаются в projectile-special-interaction блоке. Packet-70 Mystic Frog capture здесь fail-closed, потому что Terraria телепортирует лягушку вместо создания обычного пойманного предмета.
