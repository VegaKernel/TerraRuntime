# Ванильная генерация мира: Micro Biomes (Terraria 1.4.5.8)

`terraruntime:vanilla` продвинут ещё на одну source-backed границу и теперь владеет зарегистрированным проходом `Micro Biomes` для обычных миров канонического размера. Terraria наружу показывает его как один generation pass, поэтому TerraRuntime сохраняет ту же идентичность графа, а не выдумывает публичные подпроходы для каждого внутреннего биома.

## Закреплённый source contract

Реализация опирается на закреплённый официальный TerrariaServer 1.4.5.8. Workflow source-contract декомпилирует `WorldGen.AddPasses` через ilspycmd 11.0.0.9375 и извлекает встроенный ресурс `Terraria.GameContent.WorldBuilding.Configuration.json`.

Закреплённые отпечатки:

- `WorldGen.AddPasses`: `72e757af1fb0a7b565d397bbb0f9fd1d32a2960838ba636c593e122645ab9672`
- регистрация `Micro Biomes`: `2edda4081fc087d403859a905ae4ee5d92b3b574349790edad4f93a9eed2649e`
- конфигурация worldgen: `22a72bf1eadc9a6b6e48ef056bbdef700a290fb2967774b47928ae3332512da3`

Для обычного мира официальный delegate выполняет внутренние стадии в таком порядке:

1. превращение подходящих сундуков в Dead Man's Chest traps;
2. Thin Ice;
3. святыни Enchanted Sword;
4. Campsites;
5. Mining Explosives;
6. Living Mahogany trees;
7. зарезервированная в исходнике седьмая десятая прогресса;
8. длинные minecart tracks;
9. стандартные minecart tracks;
10. lava traps.

`CorruptionPitBiome` по-прежнему существует в сборке 1.4.5.8, но закреплённый ordinary `Micro Biomes` delegate его не вызывает. Поэтому TerraRuntime не добавляет выдуманную стадию corruption pit только потому, что она встречалась в старых конфигурациях.

## Закреплённая конфигурация

Встроенная конфигурация 1.4.5.8 задаёт:

| Ключ | Диапазон/значение | Масштабирование |
| --- | --- | --- |
| `DeadManChests` | 10..20 | ширина мира |
| `ThinIcePatchCount` | 3..5 | ширина мира |
| `SwordShrineAttempts` | 1..2 | ширина мира |
| `SwordShrinePlacementChance` | 0.5 | скаляр |
| `CampsiteCount` | 6..11 | площадь мира |
| `ExplosiveTrapCount` | 14..29 | площадь мира |
| `LivingTreeCount` | 6..11 | ширина мира |
| `LongTrackCount` | 1..2 | ширина мира |
| `LongTrackLength` | 400..1000 | ширина мира |
| `StandardTrackCount` | 4..7 | площадь мира |
| `StandardTrackLength` | 150..300 | ширина мира |

Clean-room реализация применяет ту же идею масштабирования по ширине/площади к трём каноническим размерам Terraria и расходует тот же общий vanilla RNG surface.

## Владение размещением

Проход теперь содержит source-shaped clean-room placers для всех перечисленных ordinary-стадий. Перед разрушительными изменениями он защищает зарегистрированные сундуки и любые frame-important объекты. Dead Man's Chest логика модифицирует уже зарегистрированные generated chests, а не создаёт сиротские container tiles. Minecart tracks прокладываются только по маршруту, который можно зарезервировать без пересечения защищённых объектов.

В этом слое используются закреплённые tile identities: Thin Ice `162`, Explosives `141`, Campfire `215`, Living Mahogany `383`, Living Mahogany Leaves `384`, Minecart Track `314` и семейство фоновых объектов Enchanted Sword на tile `187`.

## Граница паритета

Это не заявление о побайтово идентичной геометрии micro-biomes. Точно закреплены внешняя идентичность прохода, source order, ключи/диапазоны конфигурации, смысл retry budgets и владение shared RNG. Процедурная геометрия отдельных биомов остаётся clean-room source-shaped реализацией и может заменяться по одному биому по мере более глубокого переноса исходного поведения.

Special seeds и неканонические размеры пока продолжают использовать прежний compatibility plan. Следующая ordinary source-backed граница: `Settle Liquids Again`.

## Проверка

Vanilla generated-world acceptance собирает runtime, запускает focused contracts для 106-проходного графа, создаёт канонический `.wld`, перечитывает его через TerraRuntime и затем запускает закреплённый официальный TerrariaServer 1.4.5.8 с этим сгенерированным миром.
