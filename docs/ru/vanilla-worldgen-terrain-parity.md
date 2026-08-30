# Паритет ванильной генерации Terraria 1.4.5.8

`terraruntime:flat` остаётся отдельным минимальным детерминированным генератором. Работа над ванильным worldgen ведётся только внутри уже существующего `terraruntime:vanilla`.

## Схема постепенного переноса

Ванильный генератор переносится по проходам. `SourceBackedVanillaWorldGenerationProvider1458` сохраняет прежние compatibility-проходы, добавляет нужные source-backed prerequisites и заменяет отдельные реализации под тем же идентификатором генератора.

Это не создаёт второй недоделанный «vanilla» и сохраняет compatibility-пути для специальных сидов, пока исходные алгоритмы Terraria переносятся по частям.

## Общий поток случайных чисел

TerrariaServer 1.4.5.8 последовательно расходует один world-generation поток `UnifiedRandom` через bootstrap и generation work. `WorldGenerationRngMode.VanillaSharedRng` теперь следует этой модели: TerraRuntime создаёт один адаптер `VanillaUnifiedRandom1458` на весь execution plan и передаёт его всем vanilla-shared проходам.

Повторное засевание RNG между проходами могло давать детерминированный результат, но принципиально не могло сохранить состояние Terraria между стадиями генерации.

## Source-backed bootstrap Reset

Для обычных сидов и трёх канонических размеров Terraria план теперь начинается с `terraria:1.4.5.8/Reset`. Bootstrap расходует RNG, который Terraria использует до Terrain, и сохраняет состояние для последующих проходов, включая:

- сторону и координату dungeon;
- origins jungle и snow;
- случайные левую и правую границы пляжей;
- выбранные ore tiers;
- tree styles и позиции переходов;
- cave и surface background styles;
- moon style и часть начального состояния нового мира;
- world-size-dependent generation counts.

Ключевая конфигурация пляжей закреплена по source: `BeachBordersWidth = 275`, `BeachSandRandomCenter = 320`, `BeachSandRandomWidthRange = 20`, `BeachSandDungeonExtraWidth = 40`, `BeachSandJungleExtraWidth = 20`.

Для контроля расхода RNG добавлен фиксированный checkpoint. Для seed `1458` в обычном малом мире $4200 \times 1200$ Reset должен получить границы пляжей `322 / 3830`, dungeon side `-1`, dungeon location `484`, а следующим значением общего RNG после Reset должно быть `289143048`.

`tools/ci/probe_worldgen_reset.py` декомпилирует закреплённый официальный TerrariaServer 1.4.5.8 `WorldGen.Reset` и отклоняет изменения проверенных Reset-констант, построения случайных границ пляжей и порядка tree/cave initialization. Отдельный workflow `Terraria Worldgen Reset Contract` запускает этот source-contract вместе с профильными тестами реализации.

## Source-backed Terrain

`terraria:1.4.5.8/Terrain` source-backed для обычных сидов на настоящих размерах Terraria:

| Размер | Тайлы |
| --- | ---: |
| Малый | $4200 \times 1200$ |
| Средний | $6400 \times 1800$ |
| Большой | $8400 \times 2400$ |

Перенесены state machine форм рельефа, заполнение колонок Dirt/Stone, история поверхности и её ретаргетинг у берега, квантование rock layer шагом в 6 тайлов и `FlatBeachPadding = 5`. Terrain теперь получает случайные границы пляжей от предыдущего Reset bootstrap вместо фиксированной compatibility-константы.

Нестандартные размеры и специальные/секретные сиды пока используют старый compatibility Terrain. Их ветви Reset ещё не объявляются source-exact, поэтому source-backed Reset намеренно не расходует дополнительный RNG в этих compatibility-сценариях.

## Метаданные

Source-backed Terrain сохраняет рассчитанные `worldSurface` и `rockLayer` в metadata workspace. Compatibility Metadata по-прежнему рассчитывает spawn, dungeon anchor и сохраняет профиль сида, после чего source-backed значения слоёв восстанавливаются. Reset state хранится внутри генератора, чтобы будущие Jungle, desert, ocean, structure и background проходы использовали те же исходные выборы, а не генерировали их заново.

## Проверка результата

`.github/workflows/terraria-vanilla-generated-world-acceptance.yml` для малого канонического мира:

1. собирает TerraRuntime и запускает профильные тесты worldgen;
2. создаёт настоящий `.wld` генератором `terraruntime:vanilla`;
3. загружает файл собственным verifier TerraRuntime;
4. запускает закреплённый TerrariaServer 1.4.5.8 с этим миром и требует открытия игрового порта.

Существующая проверка `flat` не изменяется.

## Что ещё не ванильное

Для обычного мира source-backed уже стали Reset и Terrain, но в provider всё ещё остаются compatibility-реализации биомов, пещер, руд, dungeon и модификаторов специальных сидов. В закреплённом каталоге Terraria 1.4.5.8 зарегистрировано 109 проходов. Настоящий паритет требует заменить эти compatibility-группы исходной последовательностью проходов и затем добавить reference-world comparison, а не наращивать новые приблизительные эвристики.
