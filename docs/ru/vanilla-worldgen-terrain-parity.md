# Паритет ванильной генерации Terraria 1.4.5.8

`terraruntime:flat` остаётся отдельным минимальным детерминированным генератором. Работа над ванильным worldgen ведётся только внутри уже существующего `terraruntime:vanilla`.

## Схема постепенного переноса

Ванильный генератор переносится по проходам. `SourceBackedVanillaWorldGenerationProvider1458` сохраняет прежние compatibility-проходы, добавляет нужные source-backed prerequisites и заменяет отдельные реализации под тем же идентификатором генератора.

Это не создаёт второй недоделанный «vanilla» и сохраняет compatibility-пути для специальных сидов, пока исходные алгоритмы Terraria переносятся по частям.

## Общий поток случайных чисел

`WorldGenerationRngMode.VanillaSharedRng` означает точный Terraria world-generation RNG API, общий для всей работы **внутри одного pass**. Закреплённый TerrariaServer 1.4.5.8 в `WorldGenerator.RunPass` создаёт `Main.rand = new UnifiedRandom(_seed)` перед каждым enabled pass, поэтому TerraRuntime начинает каждый vanilla-shared pass из его локального seed и сохраняет порядок вызовов только внутри pass. Перенос состояния между зарегистрированными passes был бы ошибкой совместимости; так же, как и параллелизация RNG-sensitive работы внутри pass.

## Source-backed bootstrap Reset

Для обычных сидов, а также чистого профиля `Don't Dig Up`/Remix, на трёх канонических размерах Terraria план начинается с `terraria:1.4.5.8/Reset`. Bootstrap расходует RNG, который Terraria использует до Terrain, и сохраняет состояние для последующих проходов, включая:

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

Чистая Remix-ветка намеренно мала и закреплена по source: `Reset` заменяет hell-chest item `112` на `683` и выбирает jungle origin из диапазона $20\%$–$35\%$ вместо обычных $15\%$–$30\%$. Zenith не допускается только потому, что включает Remix: он также активирует другие special-ветви, ещё не перенесённые в source-backed виде.

## Source-backed Terrain

`terraria:1.4.5.8/Terrain` source-backed для обычных сидов и чистого профиля `Don't Dig Up`/Remix на настоящих размерах Terraria:

| Размер | Тайлы |
| --- | ---: |
| Малый | $4200 \times 1200$ |
| Средний | $6400 \times 1800$ |
| Большой | $8400 \times 2400$ |

Перенесены state machine форм рельефа, заполнение колонок Dirt/Stone, история поверхности и её ретаргетинг у берега, квантование rock layer шагом в 6 тайлов и `FlatBeachPadding = 5`. Terrain теперь получает случайные границы пляжей от предыдущего Reset bootstrap вместо фиксированной compatibility-константы. Для чистого Remix он также использует Terraria alternate surface-offset distribution и глубокую инициализацию/потолок rock layer.

Нестандартные размеры, Zenith, комбинации special switches и secret switches пока используют старый compatibility Terrain. Их ветви Reset ещё не объявляются source-exact, поэтому source-backed Reset намеренно не расходует дополнительный RNG в этих compatibility-сценариях. После pure Remix Terrain все последующие source-shaped overlays также остаются на compatibility-пути: это ограниченный slice `Reset + Terrain`, а не полный Remix world parity.

## Метаданные

Source-backed Terrain сохраняет рассчитанные `worldSurface` и `rockLayer` в metadata workspace. Compatibility Metadata по-прежнему рассчитывает spawn, dungeon anchor и сохраняет профиль сида, после чего source-backed значения слоёв восстанавливаются.

Для source-backed ordinary и pure Remix Terrain миров Reset bootstrap теперь также переносится в `RuntimeWorldGenerationMetadataSnapshot`. Fresh `.wld` persistence записывает полученные Reset значения moon type, tree/cave transition positions и styles, primary/secondary background styles, cloud timer/count, wind, slime-rain countdown и pre-hardmode ore choices. `flat` и custom generators не имеют такого bootstrap и продолжают использовать консервативные defaults нового мира.

Этот persistence bridge нужен следующим проходам: Jungle, desert, ocean, structures и decoration смогут использовать один и тот же результат Reset во время генерации, а сохранённый мир после restart не откатит эти исходные выборы обратно к compatibility defaults.

## Проверка результата

`.github/workflows/terraria-vanilla-generated-world-acceptance.yml` для малого канонического мира:

1. собирает TerraRuntime и запускает профильные тесты worldgen;
2. создаёт настоящий `.wld` генератором `terraruntime:vanilla`;
3. загружает файл собственным verifier TerraRuntime;
4. запускает закреплённый TerrariaServer 1.4.5.8 с этим миром и требует открытия игрового порта.

Существующая проверка `flat` не изменяется.

## Что ещё не ванильное

Для обычного мира source-backed уже стали Reset и Terrain, а для pure Remix добавлена ограниченная ветка, но в provider всё ещё остаются compatibility-реализации биомов, пещер, руд, dungeon и поздних модификаторов специальных сидов. В закреплённом каталоге Terraria 1.4.5.8 зарегистрировано 109 проходов. Настоящий ordinary или Remix parity требует заменить эти compatibility-группы source-backed последовательностями passes и затем добавить reference-world comparison, а не наращивать новые приблизительные эвристики.
