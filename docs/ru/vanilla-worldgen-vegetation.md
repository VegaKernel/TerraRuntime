# Vanilla worldgen: растительный блок до Mushrooms

Этот документ описывает поздний растительный слой совместимого с Terraria 1.4.5.8 генератора `terraruntime:vanilla`.

## Объём реализации

Для ordinary canonical worlds production-план продвигается от уже сохраняемого стартового `Guide` через одиннадцать pinned-проходов:

1. `Sunflowers`
2. `Planting Trees`
3. `Herbs`
4. `Dye Plants`
5. `Webs And Honey`
6. `Weeds`
7. `Glowing Mushrooms and Jungle Plants`
8. `Jungle Plants`
9. `Vines`
10. `Flowers`
11. `Mushrooms`

Canonical production graph увеличивается с 89 до 100 entries. `terraruntime:flat` остаётся отдельным генератором, публичная vanilla-идентичность остаётся `terraruntime:vanilla`.

## Source identities и владение окружением

Реализация использует Terraria tile identities, проверенные по актуальным таблицам данных 1.4.5.x: Sunflower `27`, Trees `5`, Herbs `82`, Dye Plants `227`, Cobweb `51`, обычные/Jungle vines `52/62`, Jungle plants `61/74/233`, Mushroom grass `70` и Glowing Mushroom plants `71`.

Размещение привязано к уже сгенерированному terrain, а не к ещё одной придуманной карте биомов. Forest plants требуют grass, Jungle vegetation требует Jungle grass, glowing mushrooms требуют Mushroom grass, snow trees требуют Snow Block, а honey pockets ограничиваются областью вокруг Jungle origin, которым владеет Reset bootstrap.

Все одиннадцать проходов используют единый Terraria-compatible поток `UnifiedRandom`, предоставленный режимом `VanillaSharedRng`. Они включаются только для ordinary worlds трёх canonical Terraria dimensions; noncanonical и special-seed requests сохраняют compatibility plan до отдельного порта соответствующих веток.

## Безопасность frame-important объектов

Поздняя растительность должна сосуществовать с сундуками, дверями, горшками, ловушками, алтарями, fallen logs, floating-island house и другими framed objects, созданными раньше. Поэтому placement требует свободных target cells и избегает frame-important объектов поблизости там, где более крупным структурам, например деревьям, нужен запас места.

Часть сложного vanilla framing, прежде всего полное framing ветвей/верхушек деревьев и высокой Jungle vegetation, пока является source-shaped, а не byte-identical. Pass владеет правильным content family и placement domain, но не делает ложных заявлений об exact reference-world frames или exact RNG consumption для helper-методов, которые ещё не перенесены clean-room способом.

## Поведение проходов

- `Sunflowers` ставит framed 2x4 sunflower objects на соседние блоки surface grass.
- `Planting Trees` выращивает консервативные forest, Jungle и snow tree structures в свободных surface columns.
- `Herbs` выбирает семейства трав по совместимому soil/biome type.
- `Dye Plants` размещает редкие biome-aware dye plants с локальным интервалом.
- `Webs And Honey` добавляет cavern cobwebs и Jungle-biased honey pockets.
- `Weeds` заселяет ordinary grass короткой дикой растительностью.
- `Glowing Mushrooms and Jungle Plants` декорирует Mushroom/Jungle grass в подземных областях.
- `Jungle Plants` добавляет более поздние и плотные Jungle decoration identities.
- `Vines` выращивает цепочки обычных и Jungle vines от подходящей открытой травы.
- `Flowers` добавляет surface flower styles на ordinary grass.
- `Mushrooms` размещает обычные surface mushrooms как финальный проход этого блока.

## Проверки

Focused contracts закрепляют 100-entry graph, точный pass segment после `Guide`, принадлежность `VanillaSharedRng`, следующую source boundary (`Gems In Ice Biome`), canonical-size gating и special-seed fallback. Полный generated-world workflow затем собирает настоящий `.wld`, повторно загружает его через TerraRuntime и запускает pinned официальный TerrariaServer 1.4.5.8 с этим файлом.

## Следующая граница

Следующий source block начинается с `Gems In Ice Biome`, затем идут `Random Gems`, `Moss Grass`, `Muds Walls In Jungle` и `Larva`. Эти проходы снова относятся к underground material/biome decoration, поэтому их разумнее проверять отдельно от surface vegetation.
