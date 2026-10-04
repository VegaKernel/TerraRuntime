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

Проход выбирает строки между серединой surface/rock-слоёв и lava line, затем колонки внутри сохранённых для каждой строки границ снега. Активный тайл семейства Ice предлагает небольшой прямоугольник открытых самоцветов (тайл `178`) в соседнем воздухе; шесть стилей и вариация кадра расходуют общий RNG. Прямые официальные differential зарегистрированного прохода при `600 × 800` и canonical `4200 × 1200` совпадают по клеткам и следующему RNG.

### Random Gems

Два последовательных потока предложений из исходника размещают открытые самоцветы (тайл `178`): глубокий поиск с ограничениями по стене/жидкости, затем рассеивание прямоугольников вокруг подходящих клеток с unsafe-стеной. Каждому объекту нужна опора; стиль и кадр выбираются в исходном порядке. Прямые официальные differential зарегистрированного прохода при `600 × 800` и canonical `4200 × 1200` совпадают по позициям/стилям самоцветов и следующему RNG.

### Moss Grass

Pass продолжает moss по exposed Stone и добавляет соответствующий moss-growth рядом с существующим moss. Используются Green, Brown, Red, Blue и Purple moss. Точный vanilla helper распространения moss остаётся целью parity; текущая реализация владеет правильным material family и underground domain, но не заявляет reference-world byte equality.

### Muds Walls In Jungle

Проход находит крайние слева и справа колонки поверхностной Jungle Grass, затем сканирует строки выше `worldSurface + 20` между ними. Только Dirt-стены (`2`/`59`) заменяются на небезопасную Mud-стену (`15`); на двух боковых границах RNG вызывается в исходном порядке. Прямые официальные differential зарегистрированного прохода при `600 × 500` и canonical `4200 × 1200` совпадают по координатам/числу стен и следующему RNG.

### Larva

Larva не представляется одним anchor tile. Terraria определяет её как frame-important background object размером 3x3. Pass ищет внутри существующих Hive regions свободные Hive-wall pockets, окружённые Hive material, и записывает все девять framed Larva cells с шагом frame coordinate 18 пикселей. Placement также запрещён рядом с другими frame-important objects.

Это важно и для валидности файла, и для gameplay semantics: частично записанная Larva является orphan framed object и отклоняется gameplay mutation path. Полный объект теперь может вызвать source-shaped Queen Bee spawn для ближайшего игрока.

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
