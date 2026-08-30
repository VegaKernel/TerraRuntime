# Vanilla worldgen: underground finish до Larva

Этот документ описывает подземный завершающий блок совместимого с Terraria 1.4.5.8 генератора `terraruntime:vanilla`, добавленный после поздней растительности.

## Объём реализации

Для ordinary canonical worlds production graph увеличивается со 100 до 105 entries в закреплённом исходным каталогом порядке:

1. `Gems In Ice Biome`
2. `Random Gems`
3. `Moss Grass`
4. `Muds Walls In Jungle`
5. `Larva`

Блок намеренно останавливается перед `Micro Biomes`. Это уже отдельная граница сложности с несколькими генераторами структур, а не ещё один однородный этап декорации материалов.

`terraruntime:flat` остаётся отдельным и не меняется. Публичный vanilla ID остаётся `terraruntime:vanilla`.

## Source-backed content identities

Реализация использует проверенные Terraria identities:

- Ice Block: tile `161`;
- gem blocks: tiles `63`–`68`;
- moss on stone: tiles `179`–`183`;
- moss growth: tile `184`;
- Mud Wall unsafe: wall `15`;
- Jungle Wall unsafe: wall `64`;
- Hive: tile `225`;
- Hive Wall unsafe: wall `86`;
- Larva: tile `231`, frame-important объект размером 3x3.

Эти ID пока локальны clean-room pass, пока общий typed catalog не будет расширен по закреплённым source evidence. Код не выдумывает соседние ID только ради красивой большой таблицы.

## Поведение

### Gems In Ice Biome

Gem clusters ограничены snow span, которым владеет Reset bootstrap, и заменяют Ice Block ниже surface layers. Используются те же шесть семейств gem blocks, которые уже применяются ранним cavern gem stage.

### Random Gems

Редкие exposed stone cells в cavern layer превращаются в gem blocks. Placement требует открытого соседнего тайла, поэтому этот этап отличается от более ранних массивных `Gem Caves` clusters.

### Moss Grass

Pass продолжает moss по exposed Stone и добавляет соответствующий moss-growth рядом с существующим moss. Используются Green, Brown, Red, Blue и Purple moss. Точный vanilla helper распространения moss остаётся целью parity; текущая реализация владеет правильным material family и underground domain, но не заявляет reference-world byte equality.

### Muds Walls In Jungle

Пустые cave cells рядом с Mud или Jungle Grass внутри Reset-owned Jungle span получают естественные unsafe Mud/Jungle walls. Существующие structure, Hive, dungeon и decorative walls не перезаписываются.

### Larva

Larva не представляется одним anchor tile. Terraria определяет её как frame-important background object размером 3x3. Pass ищет внутри существующих Hive regions свободные Hive-wall pockets, окружённые Hive material, и записывает все девять framed Larva cells с шагом frame coordinate 18 пикселей. Placement также запрещён рядом с другими frame-important objects.

Это важно и для валидности файла, и для gameplay semantics: частично записанная Larva была бы orphan framed object и не могла бы считаться корректным Queen Bee trigger.

## RNG и gating

Все пять проходов используют один общий Terraria-compatible `UnifiedRandom` через `VanillaSharedRng`. Они включаются только для ordinary seeds и трёх canonical Terraria dimensions. Special seeds и synthetic dimensions сохраняют compatibility graph до отдельного порта их веток.

## Проверки

Focused contracts проверяют:

- canonical graph из 105 entries;
- точный pinned order от `Mushrooms` до `Larva`;
- принадлежность `VanillaSharedRng`;
- `Micro Biomes` как следующую source boundary;
- frame-important identity Larva и полный 3x3 framing contract;
- fallback для noncanonical и special-seed worlds.

Обычный vanilla generated-world acceptance после этого собирает настоящий format-326 `.wld`, повторно загружает его через TerraRuntime и запускает pinned официальный TerrariaServer 1.4.5.8 с этим файлом.
