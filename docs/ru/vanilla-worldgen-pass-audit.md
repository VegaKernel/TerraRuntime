# Полный аудит стадий vanilla worldgen — 2026-09-08

Проверка полного обычного Dungeon (2026-09-09): стадия36 переведена в ограниченный `R16`, не `E9`. Шестнадцать сохранённых сравнений подают одинаковый реальный префикс генерации неизменённому официальному Dungeon-проходу и runtime-проходу: совпадают все нормализованные клетки, упорядоченное содержимое/префиксы сундуков, точка старика и следующий RNG. Покрыты все канонические размеры, три вида входа и обе разновидности зла. Также совпали24 независимых плоских сценария и14 дополнительных Small seed. `R16` не доказывает сам предшествующий префикс, каждое поле GenVars, специальные seed или равенство конечного мира. Счётчик теперь **40P + 32C = 72 незавершённые стадии**; независимые многостадийные префиксы остаются **31 E9 / 279 проверок**. Итоговая приёмка записана в agent-memory; старые абзацы ниже исторические.

Шипы и варианты стен данжа (2026-09-09): попытки размещения, асимметричные проверки опор, порядок полос шипов и сохранение жидкости/кадров теперь следуют исходным правилам. Стены распространяются по связным областям базовой стены, окрашивая твёрдые границы без прохода сквозь них; склоны/активаторы сохраняют исходное поведение. Ранний выбор точек больше не включает позднее расширение области. Независимо совпали144 fixtures по всем клеткам/RNG;151 новый тест, Dungeon1467/1467; возврат очистки жидкости/отключение распространения проваливает124 теста, ошибки убраны. Release6480/6480 прошёл: сборка без предупреждений/ошибок, Windows NativeAOT/шесть smoke-проверок, загрузка свежей карты обоими серверами и прежние допуски сравнения. Linux NativeAOT и прохождение клиентом не проверены. Область графа ещё нужно сравнить с полным жизненным циклом границ crawler; оставшийся декор и равенство полного префикса открыты. Полного паритета и повышения E9 нет;40P+33C=73 незавершённые строки,31E9/279 без изменений.

Пакет поиска и установки дверей данжа (2026-09-09): кандидаты теперь используют исходные внутренние границы комнат и сохранённое осевое направление коридоров, порядок и дубликаты не теряются. Края комнат обходятся вперемежку без требования совпадения фоновой стены. Двери получают исходные выбор колонки, проверки прохода, кладку, порядок кадров/RNG и защиту опор. Независимо совпали234 дверных fixtures и8 наборов кандидатов;250 новых регрессий, focused1316/1316. Контрольные ошибки проваливают30 тестов выбора двери и2 теста старых границ, после чего убраны. Release6329/6329 прошёл: сборка без предупреждений/ошибок, Windows NativeAOT/шесть smoke-проверок, загрузка свежего мира обоими серверами и прежние допуски сравнения. Linux NativeAOT и прохождение клиентом не проверены. Неподдерживаемое разрушение frame-important объектов в этом этапе до мебели остаётся fail-closed. Полный Dungeon и равенство мира **не** подтверждены; счётчики прежние:40P+33C=73 незавершённые строки,31E9/279. Абзацы ниже относятся к предыдущим пакетам.

Локально проверенный связанный блок данжа (2026-09-09): основной renderer входа строит Dome/Tower по исходному алгоритму вместо прямоугольных оболочек. Независимое сравнение с оригинальным исполняемым файлом охватывает 216 полных зданий и 216 продолжений с платформами и предметами на полках: нормализованные поля клеток, общий RNG и упорядоченные параметры. Дополнительно проверены 30 обычных размещений платформ и 24 варианта колонн, клиньев и мозаик. Существующий генератор деревьев сохраняет завершающую очистку RangeFrame и явный режим игнорирования стен. Legacy-вход, layout и live authorities сохранены. Финальные Release6079/6079, сборка без предупреждений/ошибок, WindowsNativeAOT/шесть smoke-тестов, загрузка свежей карты обоими серверами и прежние допуски сравнения пройдены; LinuxNativeAOT и прохождение живым клиентом не проверены. Полный поиск мест для объектов данжа, двери/остальной декор и равенство настоящего префикса остаются открытыми; повышения до E9 и заявления о полном паритете нет.

[English](../en/vanilla-worldgen-pass-audit.md)

Итоговые проверки начала layout: Release5586/5586, ноль предупреждений/ошибок, WindowsNativeAOT и шесть smoke-тестов, загрузка свежей карты обоими серверами пройдены. В обычном seed1458 координаты входа Dungeon совпали с эталоном; клетки полного мира ещё различаются. Статус не повышен; далее остаются здания Dome/Tower и полные префиксы.

2026-09-09, начальная планировка: совпали18 официальных полных layout-fixtures и63 сравнения внутренних метаданных комнат;31 новая проверка входит в574 пройденных теста Dungeon. Возврат старых координат/внешнего контура проваливает20/22. Исправлены начальные координаты/RNG предварительно рассчитанного layout и начало соединения от внутренней границы, не весь Crawler/здания/декор. Dungeon36 остаётся `C`, **40P+33C=73 незавершённые строки;31E9/279 без изменений**. Далее: здания Dome/Tower и полные префиксы Dungeon. Итоговые проверки — в agent memory; абзацы ниже исторические.

2026-09-09, соединение предварительно рассчитанного входа: 54 сравнения целого маршрута с официальным исполняемым файлом совпали по клеткам, состоянию каждого шага, платформам и RNG; прошли retained59/affected631, неверная длина проваливает 54 теста. Dungeon36 остаётся `C`: здания Dome/Tower, начальная позиция layout/метаданные верхней комнаты и декорации ещё расходятся. **40P + 33C = 73 незавершённые строки; 31 E9 / 279 checkpoints** не изменены. Доказательство компонента не повышает статус полного префикса.

Legacy-вход, 2026-09-09: 108 независимых официальных fixtures здания совпадают по всем клеткам, следующему общему RNG и метаданным входа; возврат production-прямоугольника проваливает 111/112 сохранённых тестов. Строка Dungeon36 остаётся `C` (старые абзацы местами называли её `P`): Dome/Tower, layout, decoration и доказательство полного префикса открыты. Счётчики прежние: **40P + 33C = 73 незавершённые строки**, **31 E9 / 279 checkpoints**.

Пакет поверхностных материалов 2026-09-09: стадии39/40/43 переведены из `P` в `C` после 54 сравнений полных официальных делегатов на синтетическом входе (все поля клеток и следующий RNG), 58 сохранённых проверок и 473 affected-тестов. Сейчас **40P + 33C = 73 незавершённые строки**; **31 E9 / 279 checkpoints** не изменились. Эти fixtures не обходят расхождения предшествующего Dungeon graph и не доказывают девять реальных префиксов. Числа ниже относятся к прежним checkpoints.

Текущая приёмка Lakes/Slush: все девять обычных случаев размера/seed совпали по полным клеткам, следующему RNG и сохранённым данным туннелей/озёр/снега. Regression-тест покрывает **31 стадию / 279 checkpoints**; стадии33/34 получили ограниченный `E9`, остаётся **43P + 30C** незавершённых строк. Пройдены Release `4908/4908`, восстановленный focused-набор `41/41`, Windows NativeAOT, шесть smoke-проверок и загрузка свежего Small/Classic/Corruption1458 обоими серверами. Allocation-harness прогревает тот же невстраиваемый цикл, который измеряет; нагрузка/лимит байт не изменены, внесённая аллокация проваливает тест. Прежние сбои задокументированы, точный объект малой аллокации не установлен. На прежнем эталоне tile L1 `0.196460` немного хуже предыдущего пакета, wall L1 `0.458101` лучше. Неизменённые бюджеты пройдены, полного равенства мира нет. Linux NativeAOT, специальные seeds и клиентское прохождение не подтверждены.

Текущая приёмка Corruption: все девять префиксов обычного Corruption совпали по полным клеткам и следующему RNG. Сохранённый префикс охватывает **29 стадий / 261 контрольную точку**; стадия32 получила ограниченный статус `E9`, остаются **45P + 30C** строк. Crimson отдельно имеет шесть совпавших проверок полного прохода на синтетическом входе, не девять реальных префиксов. Компоненты пещер совпали в36 официальных случаях; удаление RNG framing стен ломает36/36. Release4870/4870, сборка0/0, WindowsNativeAOT+шестьsmoke, загрузка свежегоSmallCorruption обоими серверами и расширенная перепроверка префикса/травы/пещер85/85 прошли. Прежние допуски сравнения проходят при tileL1 `0.195909`, wallL1 `0.461801`; равенство всего мира, специальные seed, LinuxAOT и прохождение клиентом не подтверждены. Счётчики предыдущих блоков ниже исторические.

Последний блок Underworld: все9 полных ordinary-префиксов совпали по16-байтовым клеткам, следующему RNG и порядку слотов/координат/пустому содержимому комодов. Префикс теперь охватывает28 стадий/252 контрольные точки; остаются46P и30C. Общий QuickWater исправляет контакты по исходным флагам, запрет внутри цельного блока, сброс типа при остатке меньше24 и generation-only преобразование LavaCheck возле стен пустыни в квадрате7x7; загрузка его не применяет.24 официальных проверки контактов проходят; отрицательные контроли ломают6/6 и12/24. Полный стандартный параллельный4728/4728 и последовательный4728/4728, восстановленная Release-сборка без предупреждений/ошибок, финальные проверки префикса/контактов44/44, WindowsNativeAOT+шестьsmoke и загрузка свежегоSmall1458 в обоих серверах прошли. Предыдущие падения аллокационных тестов записаны в work-state; финальный стандартный прогон зелёный, пороги не менялись. Сравнение с прежним эталоном проходит неизменные допуски (tileL1 .201211,wallL1 .530400,liquidRatio1.043167); полного равенства миров нет. Описания предыдущих блоков ниже исторические; таблица и определение E9 отражают актуальную проверку.

Это инвентаризация всех **109 регистраций** из TerrariaServer 1.4.5.8 `WorldGen.AddPasses`, сопоставленных с текущим production plan. **Генератор целиком ещё не совпадает 1:1.** Таблица не объявляет упрощённые реализации завершёнными.

Обычный canonical plan содержит 107 исходных позиций и 7 внутренних стадий, итого 114. Первая регистрация Jungle и Skyblock существуют только внутри `skyblockWorldGen`; их отсутствие в ordinary plan не является пропуском. Внутренние Reset, TerrainLayers, Biomes/Caves/Ores barriers, SecretSeeds barrier и Metadata не являются дополнительным контентом vanilla.

`SourceBackedFinal1458Tests.Complete_ordinary_plan_matches_every_applicable_source_registration_in_order` проверяет всю последовательность, а не только последний блок. Это проверка покрытия и порядка, не геометрии. Размеры: Small/Medium/Large; остальные профили не наследуют эти утверждения. Pure Remix имеет отдельный ограниченный Reset/Terrain/Dunes slice; смешанные special seeds и неканонические размеры всё ещё используют существующий compatibility path. Новый fallback не добавлен.

## Уровни доказанности

Текущий блок sky/deposit/web прошёл Release **4638/4638**, сборку без предупреждений/ошибок, все шесть Windows NativeAOT smoke и загрузку свежего Small/Classic/Corruption1458 в TerraRuntime и официальном1.4.5.8. Проверка полной карты обнаружила обломки личинки муравьиного льва после исходного DirtToMud; существующая финальная обработка теперь удаляет их согласно CheckSuper, без ослабления проверки загрузки. Добавлены двадцать независимых проверок вариантов/обломков; отключение исправления ломает16/28 тестов обработки объектов.

Fingerprint нетронутой новой карты: `00d191513552b72b52278249a0953bfa00b596f9d913979b7e5cd0630d4a5500`; прежний официальный reference: `a73ec6c799c5e6ce3f9684377e97b07770caf19f4369c63d237d20c4fb21fa26`. Прежние пороги сравнения проходят без изменений: tileL1 **0.201211** (было0.275338), wallL1 **0.530400**, active ratio0.966632, liquid ratio1.043254, silhouette NMAE0.007981/p95 0.044167/correlation0.922428; spawn delta(1,0), dungeon delta(85,-6), сундуки181→144, townNPC2→2. Это улучшение, не равенство всего мира и не подтверждение прохождения клиентом. Linux NativeAOT локально не проверен. Следующая цель полного дифференциального префикса — Underworld.

Следующий связанный блок расширяет независимое доказательство ordinary-цепочки до **Webs: 27 стадий × 9 сочетаний seed/размера, 243 контрольные точки**. Floating Islands сохраняет проверенную геометрию и теперь передаёт упорядоченные X/Y/style/lake. Dirt To Mud, Silt и Shinies используют исходные количества, глубины и существующий небольшой TileRunner вместо приблизительных пятен. Webs использует исходный поиск потолка/боковой опоры и сохранённые координаты горных пещер; координаты независимо проверяются на Mount Caves. Все пять новых стадий совпали по полным нормализованным клеткам и следующему RNG. Двадцать шесть изолированных mud/silt/ore/web fixtures проверяют metadata и правила замены. Финальные общие Release/NativeAOT/загрузка свежего мира записываются в agent memory, а не подразумеваются из доказательства префикса.

Marble и Granite совпали во всех девяти полных22-стадийных ordinary-префиксах, включая все нормализованные байты клеток, следующий RNG и упорядоченные прямоугольники структур с отступами. Девять компонентных fixtures Marble и пятнадцать Granite независимо совпадают с официальным executable. Общая локальная Windows-приёмка пройдена (4515 тестов, шесть NativeAOT smoke и загрузка свежего мира обоими серверами); ограниченное E9 не означает завершения генератора. Полевой RNG DesertHive перенесён без изменения для использования Granite, без добавления другого генератора.

Существующий MidPass напрямую вызывает алгоритмы плит и давления, восстановленные по исходникам. Размещение читает сохранённый `GenVars.rockLayer` (`TerrainState.CurrentRockLayer`), не опубликованную игровую границу. Количество Marble масштабируется по площади, Granite — по ширине. Общая обработка сохраняет ordinary-семантику руды, склонов, сталактитов, краски и жидкости; неизвестная обработка объектов прерывает генерацию вместо выдуманной замены.

Mushroom Patches совпадает с независимой официальной ordinary-цепочкой на всех девяти канонических случаях, включая полные нормализованные данные клеток, следующий RNG и сохранённые центры биомов. Существующий MidPass использует source-derived выбор мест, ShroomPatch и озеленение/очистку всей карты. Шесть изолированных ShroomPatch fixtures также совпали по полным данным клеток/RNG. Небольшие root runners переиспользуют существующий runner Underworld, теперь `SmallTerrainRunner1458`, без сохранённого alias. Это ограниченное E9, не полное совпадение генератора; финальная приёмка записана ниже и в agent memory.

Full Desert использует восстановленные по исходникам поверхность, все четыре генератора входа, поле кластеров и декорации в существующем MidPass. Все девять канонических цепочек совпали с официальным сервером, включая полное нормализованное 16-байтовое представление клеток на Full Desert, сохранённые границы пустыни/улья/поля плотности/структуры и следующий RNG. Независимые изолированные fixtures дополнительно проверяют пять поверхностей, пять полей кластеров, двадцать входов и пять декораций. Это ограниченное доказательство E9, не завершение генератора или финальной приёмки; актуальные проверки записаны в agent memory. `WorldGenerationRequest.ResolveVanillaSeed1458()` владеет общим для executor и потока материалов преобразованием seed: отрицательные числа следуют source-правилу модуля/минимального int, текстовый CRC32 использует младший байт каждого UTF-16 code unit, не UTF-8. Четырнадцать официальных значений включают кириллицу, emoji и числовые границы. Seed пользовательских генераторов не меняется.

- **E9**: ordinary-цепочка Dunes–Underworld, 28 стадий × 9 сочетаний seed/размера, 252 контрольные точки. На каждой стадии совпадают независимые официальные хеши type/wall/frame/active/liquid, следующий RNG и кандидаты пирамид. Full Desert и каждая последующая стадия префикса дополнительно проверяют все поля нормализованной16-байтовой клетки; сохранённые данные пустыни, грибного/каменных биомов, островов и горных пещер проверяются на соответствующих стадиях. Это не доказывает полноту потребителей GenVars/StructureMap, непроверенных веток и special seeds.
- **R9**: независимый официальный Terrain probe, 3 seed × 3 размера: равны все type/frame/active клетки, waterLine/lavaLine и следующий RNG. Reset beach inputs переданы одинаково; это не независимая проверка полного Reset или всего мира.
- **R1**: независимый официальный Remove Water From Sand probe на изолированной сетке; равны amount/kind всех клеток. Это не проверка предыдущего Settle Liquids или всего post-settle блока.
- **C**: есть source-backed контракты/проверенные компоненты; полная стадия не доказана 1:1.
- **R16**: полный обычный Dungeon на16 одинаковых реальных префиксах; независимые оригинальные клетки/порядок сундуков/точка старика/RNG. Это не независимая генерация поданного префикса и не доказательство каждого глобального поля. Дополняется24 плоскими сценариями и14 дополнительными seed; специальные seed остаются открыты.
- **P**: реализация есть, точные helpers, состояние и RNG остаются открытыми.
- **N**: в ordinary profile ветка не мутирует мир; special-seed поведение не закрыто.
- **S**: условная Skyblock-регистрация, не часть ordinary plan.

Для всех C/P следующий обязательный шаг — одинаковый входной snapshot в candidate и official pass, сравнение всех изменённых клеток, side tables, GenVars и следующего RNG. Столбец проверки указывает область, а не заявляет, что каждый перечисленный helper отсутствует.

## Production owners

- Base: [SourceBackedProvider1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/SourceBackedProvider1458.cs).
- Early: [EarlyPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/EarlyPipeline1458.cs).
- Mid: [MidPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/MidPipeline1458.cs).
- Dungeon: [DungeonPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/DungeonPipeline1458.cs).
- JungleStructure: [JungleStructurePipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/JungleStructurePipeline1458.cs).
- PostSettle: [PostSettlePipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/PostSettlePipeline1458.cs).
- Chest: [ChestPlacementPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/ChestPlacementPipeline1458.cs).
- LateStructure: [LateStructurePipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/LateStructurePipeline1458.cs).
- SurfaceFinish: [SurfaceFinishPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/SurfaceFinishPipeline1458.cs).
- StartingNpc: [StartingNpcPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/StartingNpcPipeline1458.cs).
- Vegetation: [VegetationPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/VegetationPipeline1458.cs).
- UndergroundFinish: [UndergroundFinishPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/UndergroundFinishPipeline1458.cs).
- MicroBiomes: [MicroBiomesPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/MicroBiomesPipeline1458.cs).
- Final: [FinalPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/FinalPipeline1458.cs).

Special: существующий compatibility profile, не ordinary overlay.

## Исправления ранней геометрии в этом проходе

В существующем early owner исправлены целочисленные середины кривых дюн и source clear/reset metadata, double-границы количества каменных/земляных вкраплений, исключение Small Holes по текущей поверхности и запрет замены песка глиной. Rock Layer Caves выбирает параметры кисти перед координатами. Если поиск поверхности ничего не нашёл, поверхностная пещера не запускается; мокрая ветка Caverer снова заполняется водой, а клетки кисти тоннеля за границей мира сохраняют расход RNG. Mount Caves использует проверенное исключение трёх типов. Grass меняет центр при четырёх активных соседях земли, сохраняя данные центра; это не распространение травы по открытой поверхности.

`EarlyTerrainReference1458Tests` сохраняет все 252 официальные поэтапные контрольные точки для seed 1458, 42 и 8675309 на Small/Medium/Large, плюс изолированные регрессии Grass и отсутствующей поверхности. E9 — ограниченный differential, не завершение генератора. Обычные прямоугольники Dunes в StructureMap и side tables Tunnels ещё не сохраняются полностью; Corruption и последующие стадии не входят в это доказательство. Прежние измерения целого мира ниже явно помечены как исторические.

В Jungle восстановлены source-запрет перезаписи стен и RNG фрейминга, диапазон поиска стеновых отверстий, последняя итерация туннеля и порядок выбора самоцветов. Ограниченное generation-only разрушение природных клеток очищает тип/кадры/форму/покрытие блока как KillTile, включая RNG выбора пыли травы, без игрового лута и live tile authority. Неизвестные объекты и исчерпание бюджета поиска завершаются fail-closed; выдуманная mud-точка больше не подставляется.

Существующая Mid-стадия вызывает [JungleMudSurface1458](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/JungleMudSurface1458.cs): озеленение mud по всей карте и удаление четырёхсвязных компонентов меньше20 клеток. Допущенные материалы ранней цепочки solid/clearable; неизвестная активная семантика прерывает проход до мутаций. Замена59→60 сохраняет activity/solidity, RNG отсутствует, поэтому полный source-обход эквивалентен нерекурсивной проверке открытых соседей. Сохранены порядок lava-столбцов, граница10, очистка paint/coating и границы5 для компонентов. Девять полных официальных fixtures и изолированные тесты доказывают этот ordinary-результат, не последующий DesertBiome и special seeds.

Final Cleanup теперь вызывает ограниченную generation-only проверку `TileFrame / CheckDoorClosed` для обсидиановых дверей. Перекрывающиеся HellFort могут затереть одну клетку двери; оставшиеся фрагменты удаляются при повреждении трёхклеточного объекта или сплошных опор, без удаления замещающего кирпича. Исправление проверяют шесть независимых официальных fixtures и production integration tests. Полный фрейминг объектов, другие стили дверей и весь Final Cleanup этим не закрыты.

## Все регистрации в исходном порядке

Интеграция Full Desert также выявила ошибки границ поздних стадий. Ordinary Settle Liquids оставляет `RollingCactus` (tile484) нетвёрдым; Smooth World теперь сохраняет этот source-признак вместо разрушения его клеток или копирования типа в пустоты грунта. Final Cleanup удаляет неполные объекты катящегося кактуса без стиля через ограниченный `Check2x2` slice, сохраняя чужие замещающие клетки; полные проверки опор и всего фрейминга объектов остаются открытыми. Loading-only разрушение жидкостью допускает целые катящиеся кактусы и четыре генерируемых стиля `AntlionLarva` (tile485); повреждённые и смешанные объекты по-прежнему отвергаются. Именованные типы доступны в `VanillaTileIds`; live authority для лута, NPC или снарядов не добавлена. Валидатор сгенерированного мира использует существующие точные settling-исключения симулятора, не запрещая жидкость внутри валунов. Обычные полные твёрдые блоки по-прежнему отвергаются. Исправления подтверждены независимыми официальными QuickWater fixtures и generation/loading TileFrame/KillTile fixtures; актуальная приёмка записана в agent memory.

| # | Исходное имя | Владелец | Статус | Следующая проверка / область |
| ---: | --- | --- | --- | --- |
| 1 | Terrain | Base | R9 | TerrainPass / layers / RNG |
| 2 | Jungle | Special | S | skyblockWorldGen |
| 3 | Skyblock | Special | S | skyblockWorldGen |
| 4 | Dunes | Early | E9 | Dunes / pyramid candidates / RNG |
| 5 | Ocean Sand | Early | E9 | OceanSand / TileRunner / RNG |
| 6 | Sand Patches | Early | E9 | TileRunner / selection / RNG |
| 7 | Tunnels | Early | E9 | digTunnel / RNG |
| 8 | Mount Caves | Early | E9 | Caverer / selection / RNG |
| 9 | Dirt Wall Backgrounds | Early | E9 | background scan / walls / RNG |
| 10 | Rocks In Dirt | Early | E9 | TileRunner / RNG |
| 11 | Dirt In Rocks | Early | E9 | TileRunner / RNG |
| 12 | Clay | Early | E9 | TileRunner / RNG |
| 13 | Small Holes | Early | E9 | TileRunner / liquid lines / RNG |
| 14 | Dirt Layer Caves | Early | E9 | TileRunner / liquid lines / RNG |
| 15 | Rock Layer Caves | Early | E9 | TileRunner / liquid lines / RNG |
| 16 | Surface Caves | Early | E9 | Caverer / selection / RNG |
| 17 | Wavy Caves | Early | E9 | special-seed branches |
| 18 | Generate Ice Biome | Early | E9 | snow bounds / lavaLine / full-pass snapshot |
| 19 | Grass | Early | E9 | cardinal dirt seeds / metadata / RNG |
| 20 | Jungle | Early | E9 | JunglePass / runners / tunnels / RNG |
| 21 | Mud Caves To Grass | Mid | E9 | jungle scan / SpreadGrass / RNG |
| 22 | Full Desert | Mid | E9 | полные данные клеток / сохранённые границы / RNG; общие потребители StructureMap открыты |
| 23 | Mushroom Patches | Mid | E9 | ShroomPatch / корни / трава / очистка / центры / полные клетки / RNG |
| 24 | Marble | Mid | E9 | плиты / склоны / сталактиты / полные клетки / структуры / RNG |
| 25 | Granite | Mid | E9 | давление / лава / очистка / декорации / полные клетки / структуры / RNG |
| 26 | Floating Islands | Mid | E9 | полные клетки / RNG / сохранённые XY, style, lake; поздние дома открыты |
| 27 | Dirt To Mud | Mid | E9 | количество runners по всему миру / игнорирование песка / скорость mud / RNG |
| 28 | Silt | Mid | E9 | две полосы глубин/количеств / отказ по стене / runner / RNG |
| 29 | Shinies | Mid | E9 | двенадцать упорядоченных ore bands / runner / RNG; special seeds не доказаны |
| 30 | Webs | Mid | E9 | сохранённые горные пещеры / поиск потолка-бока / runner / полные клетки / RNG |
| 31 | Underworld | Mid | E9 | Все9 полных префиксов: клетки, следующий RNG, порядок слотов/координат и пустое содержимое комодов совпадают;24 проверки контактов; только ordinary-профиль |
| 32 | Corruption | Mid | E9 | полный префикс обычного Corruption: клетки / RNG; Crimson: синтетические проверки полного прохода; специальные seed открыты |
| 33 | Lakes | Mid | E9 | Полные клетки / RNG / порядок озёр; обычные префиксы |
| 34 | Slush | Mid | E9 | Полные клетки / сохранённые границы снега / без RNG; обычные префиксы |
| 35 | Dual Dungeons Dither Snake | Dungeon | N | dualDungeons |
| 36 | Dungeon | Dungeon | R16 | полный обычный проход:16 одинаковых реальных префиксов; клетки / порядок сундуков+префиксы / старик / RNG;24 плоских случая +14 дополнительных seed; независимый сквозной префикс/специальные seed остаются открыты |
| 37 | Mountain Caves | Dungeon | C | Mountinater / complete-pass snapshot |
| 38 | Beaches | Dungeon | C | ocean depth / slopes / pass snapshot |
| 39 | Gems | Dungeon | C | 18 official delegate fixtures / remaining prefix proof |
| 40 | Gravitating Sand | Dungeon | C | 18 official gap-fill fixtures / remaining prefix proof |
| 41 | Create Ocean Caves | Dungeon | P | OceanCave / entrance / RNG |
| 42 | Shimmer | Dungeon | P | ShimmerBiome / Aether geometry / RNG |
| 43 | Clean Up Dirt | Dungeon | C | 18 official wall-cleanup fixtures / remaining prefix proof |
| 44 | Pyramids | Dungeon | P | Pyramid / walls / descent / RNG |
| 45 | Dirt Rock Wall Runner | JungleStructure | P | wall runners / RNG |
| 46 | Living Trees | JungleStructure | P | GrowLivingTree / roots / room / RNG |
| 47 | Wood Tree Walls | JungleStructure | P | wall spread / roots / RNG |
| 48 | Altars | JungleStructure | P | placement / exclusions / RNG |
| 49 | Wet Jungle | JungleStructure | P | water placement / scan order |
| 50 | Jungle Temple | JungleStructure | P | temple graph / rooms / door / RNG |
| 51 | Hives | JungleStructure | P | HiveBiome / honey / RNG |
| 52 | Jungle Chests | JungleStructure | P | candidate rooms / frames / RNG |
| 53 | Settle Liquids | JungleStructure | P | QuickWater(3) / WaterCheck / quickSettle |
| 54 | Remove Water From Sand | PostSettle | R1 | surface scan / six tile types / boundaries |
| 55 | Oasis | PostSettle | P | PlaceOasis / banks / placement / RNG |
| 56 | Shell Piles | PostSettle | P | all source branches / decoration / RNG |
| 57 | Smooth World | PostSettle | C | WorldSmoother / whole-pass differential |
| 58 | Waterfalls | PostSettle | P | source placement / RNG |
| 59 | Ice | PostSettle | P | snow bounds / thin ice / RNG |
| 60 | Wall Variety | PostSettle | P | wall helpers / RNG |
| 61 | Life Crystals | PostSettle | C | placement attempts / framing / RNG |
| 62 | Statues | PostSettle | C | styles / placement attempts / RNG |
| 63 | Buried Chests | Chest | C | AddBuriedChest / styles / loot / RNG |
| 64 | Surface Chests | Chest | C | AddBuriedChest / surface eligibility / RNG |
| 65 | Jungle Chests Placement | Chest | C | candidate order / loot / RNG |
| 66 | Water Chests | Chest | C | AddBuriedChest / liquid eligibility / RNG |
| 67 | Spider Caves | LateStructure | P | SpiderBiome / webs / walls / RNG |
| 68 | Gem Caves | LateStructure | P | GemCave / RNG |
| 69 | Moss | LateStructure | P | moss selection / growth / RNG |
| 70 | Temple | LateStructure | P | temple finishing / traps / RNG |
| 71 | Cave Walls | LateStructure | C | CaveWallVariety / regions / RNG |
| 72 | Jungle Trees | LateStructure | C | tree grower / placement / RNG |
| 73 | Floating Island Houses | LateStructure | C | IslandHouse / furniture / loot / RNG |
| 74 | Quick Cleanup | SurfaceFinish | P | scan order / cleanup rules |
| 75 | Pots | SurfaceFinish | C | styles / locations / RNG |
| 76 | Hellforge | SurfaceFinish | C | fort geometry / placement / RNG |
| 77 | Spreading Grass | SurfaceFinish | P | SpreadGrass / recursion |
| 78 | Surface Ore and Stone | SurfaceFinish | P | surface scan / TileRunner / RNG |
| 79 | Place Fallen Log | SurfaceFinish | C | PlaceFallenLog / placement / RNG |
| 80 | Traps | SurfaceFinish | C | trap families / wiring / RNG |
| 81 | Piles | SurfaceFinish | C | styles / placement / RNG |
| 82 | Spawn Point | SurfaceFinish | C | source spawn search / safety |
| 83 | Grass Wall | SurfaceFinish | P | wall spread / RNG |
| 84 | Guide | StartingNpc | C | Guide / seed profiles / NPC metadata |
| 85 | Sunflowers | Vegetation | C | placement / RNG |
| 86 | Planting Trees | Vegetation | C | tree grower / planting attempts / RNG |
| 87 | Herbs | Vegetation | C | biome selection / styles / RNG |
| 88 | Dye Plants | Vegetation | C | styles / substrate / placement / RNG |
| 89 | Webs And Honey | Vegetation | P | web / honey scans / RNG |
| 90 | Weeds | Vegetation | P | plant selection / RNG |
| 91 | Glowing Mushrooms and Jungle Plants | Vegetation | P | growth helpers / RNG |
| 92 | Jungle Plants | Vegetation | P | growth helpers / RNG |
| 93 | Vines | Vegetation | P | biome selection / lengths / RNG |
| 94 | Flowers | Vegetation | P | styles / growth / RNG |
| 95 | Mushrooms | Vegetation | P | growth helpers / RNG |
| 96 | Gems In Ice Biome | UndergroundFinish | P | snow bounds / gems / RNG |
| 97 | Random Gems | UndergroundFinish | P | placement / RNG |
| 98 | Moss Grass | UndergroundFinish | P | moss growth / RNG |
| 99 | Muds Walls In Jungle | UndergroundFinish | P | wall scans / RNG |
| 100 | Larva | UndergroundFinish | C | hive anchors / framing / RNG |
| 101 | Micro Biomes | MicroBiomes | C | TrackGenerator / houses / all biome helpers |
| 102 | Settle Liquids Again | Final | P | QuickWater / WaterCheck / quickSettle |
| 103 | Cactus, Palm Trees, & Coral | Final | C | growers / planting distribution / RNG |
| 104 | Tile Cleanup | Final | P | scan order / framing rules |
| 105 | Lihzahrd Altars | Final | C | temple anchors / placement / RNG |
| 106 | Water Plants | Final | P | liquid/substrate selection / RNG |
| 107 | Stalac | Final | C | styles / placement / RNG |
| 108 | Remove Broken Traps | Final | C | trap validation / source scan |
| 109 | Final Cleanup | Final | P | all cleanup branches / persisted state |

## Приоритеты по фактическим расхождениям

- [x] Terrain: перенести два liquid roll из reseeded TerrainLayers в конец Terrain. Для Small seed1458 исправлены water 778→745 и lava846→801. TerrainLayers только переносит сохранённые значения; отсутствие состояния — ошибка, не новые броски.
- [x] Remove Water From Sand: вместо очистки жидкости внутри active-песка по всей карте — исходный поверхностный scan: x400..width-401, y100..<worldSurface-1, первый active tile, шесть типов53/396/397/404/407/151, очистка вверх до100. Подземные и океанские жидкости не затрагиваются; liquid kind сохраняется.
- [x] Ordinary-цепочка Dunes–Mud Caves To Grass: сохранены 162 независимые официальные контрольные точки клеток/RNG/кандидатов пирамид. Закрыт этот differential на девяти случаях, не все ветки и сохранённое состояние.
- [ ] Ранние side tables: сохранять и проверять оставшиеся GenVars/StructureMap и их потребителей. Full Desert уже совпадает в документированном differential полных клеток/RNG на девяти случаях; это не закрывает отсутствующие ранние side tables или последующих потребителей.
- [x] Обычный Dungeon: исходные вход/лестницы, спуск, комнаты, двери/платформы, locked/biome chests, мебель, картины, свет, знамёна и ловушки; клетки/сундуки/точка старика/RNG полного прохода совпадают в описанной проверке `R16`.
- [ ] Dungeon за этой границей: независимое равенство сквозного префикса, все оставшиеся потребители GenVars/StructureMap и специальные seed; равенство конечной карты/клиентского прохождения не выводится из отдельного прохода.
- [ ] Pyramids, Living Trees, Crimson, Oasis, Aether, Jungle Temple/Hives: точные builders, стены, входы, комнаты и состояние для последующих стадий; не рисовать корректирующую оболочку поверх ошибки.
- [ ] Floating Islands: сохранить доказанные CloudIsland/CloudLake компоненты; отдельно проверить исходные placement/IslandHouse/мебель/loot и результат после поздних проходов.
- [ ] Underworld: сохранить доказанные roof/basin/runner/QuickWater компоненты; проверить всю последовательность с HellFort, Hellforge и декорациями. Видимая лава после финала не равнозначна раннему basin probe.
- [ ] Settle Liquids/Again: первая стадия всё ещё использует упрощённые sweeps, вторая — vertical column compaction. Это не vanilla QuickWater + WaterCheck + bounded quickSettle orchestration; ранее закрыто только удаление embedded liquid. Нужна точная последовательность без альтернативного live authority path.
- [ ] Micro Biomes: сохранить исправленные rail frames/clearance; переносить TrackGenerator origin/route/history/tunnel/smoothing и остальные biome builders. House budgets/frames не доказывают исходную топологию.
- [ ] Все оставшиеся C/P строки: доказать placement attempts, RNG, frames, side tables, loot и порядок cleanup. Не расширять differential budgets для прохождения изменившейся карты.
- [ ] Матрица полного мира: corruption/crimson × Small/Medium/Large × несколько seed; отдельные special-seed ветки. Official-load, structural budgets, playthrough и точное сравнение — разные критерии.

## Воспроизводимое evidence

`TerrainReference1458Tests` хранит девять официальных hashes для seed1458/42/8675309 и проверяет production Terrain и bridge. `SurfaceSandDrain1458Tests` хранит официальный hash600000 клеток с граничными и отрицательными случаями. Официальный Linux executable SHA-256: `4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034`. Локальные reflection probes и decompile остаются ignored в .cache; production не загружает Terraria assembly.

Полные-world fingerprints пока не обязаны быть равны в [reference differential gate](vanilla-worldgen-reference-differential.md). Его зелёный результат нельзя переименовывать в 1:1. См. также [визуальные долги](../roadmap/vanilla-worldgen-visual-parity-audit-2026-08-31.md).

### Текущие и исторические измерения целого мира

Текущий пакет Marble+Granite: component86/86, affected221/221 и восстановленный full4515/4515 проходят без ошибок/пропусков; Release — без предупреждений/ошибок. Windows NativeAOT и все шесть smoke проходят. Свежая Small/Classic/Corruption1458 загружается обоими серверами; официальный выходит без сохранения, runtime сохраняет только checkpoint своего fixture, оба процесса остановлены. С тем же эталоном и неизменёнными бюджетами: tileL1=0.275338 (прежде0.289948), wallL1=0.532758 (прежде0.591555), active ratio=0.964576, liquid ratio=1.039093, silhouette NMAE=0.007985, p95=0.044167, correlation=0.922362. Spawn delta(+1,0), dungeon delta(+85,-6), сундуки181→142 и town NPC2→2 всё ещё отличаются. Fingerprint кандидата `7aec2ba77dd20f939008743fb06cb052ace6ac20565e629f3ceff8f03113cbd3`; ignored evidence `stone-world-compare.json/log`. Linux NativeAOT, remote CI и прохождение официальным клиентом не проверены. Все измерения ниже исторические; бюджеты не расширялись, равенство целого мира не заявляется.

Новая карта после Mushroom Patches: восстановленные focused50/50 и affected147/147 проходят; Windows NativeAOT publish и все шесть smoke проходят. Свежая Small/Classic/Corruption1458 загружается в TerraRuntime и official1.4.5.8; официальный сервер выходит без сохранения. Сравнение с тем же эталоном проходит неизменённые бюджеты: tileL1=0.289948 (прежде0.308584), wallL1=0.591555 (прежде0.589932), active ratio=0.966202, liquid ratio=1.036840, silhouette NMAE=0.007992, p95=0.044167 и correlation=0.922356. Spawn delta(+1,0), dungeon delta(+85,-6), сундуки181→142 и town NPC2→2 всё ещё отличаются. Fingerprint кандидата `8c28f8a9c64587660d1d4a6f92167a713be23822d95fa22f9ffdafd9294a7d1d`; ignored evidence `mushroom-world-compare.json/log`. Гистограмма стен слегка ухудшилась; это записано, а не скрыто изменением бюджетов. Полный Release4475/4475 проходит без ошибок/пропусков; сборка имеет ноль предупреждений/ошибок, проверки документации/diff проходят. Linux NativeAOT и прохождение официальным клиентом не проверены. Метрики ниже относятся к прежним кандидатам.

Последний Full Desert candidate: Release build без warnings/errors; полный suite4457/4457 и финальные восемь повторных проверок framing-fixture прошли. Все шесть Windows NativeAOT smoke-проверок прошли. Свежая Small/Classic/Corruption1458 загружается в TerraRuntime и официальном1.4.5.8; официальный сервер завершён без сохранения. С тем же неизменённым official reference: tileL1=0.308584 (прежде0.355396), wallL1=0.589932 (прежде0.719732), active ratio=0.964312, liquid ratio=1.040876, silhouette NMAE=0.007998, p95=0.044167 и correlation=0.922285. Spawn delta(+1,0), dungeon delta(+85,-6), сундуки181→142 и town NPC2→2 не означают полного совпадения. Структурные бюджеты пройдены без изменений. Fingerprint candidate `43ae2acd6b88b05195354560eea3fe458503cf4d9c5119ba51e08d0111bc0786`; ignored evidence `full-desert-world-compare.json/log`. Linux NativeAOT и прохождение официальным клиентом не проверены. Все следующие абзацы описывают предыдущие candidate.

После исправления обсидиановых дверей финальная native-сборка заново создала Small1458; карта загружена и TerraRuntime, и официальным1.4.5.8. Её fingerprint и следующие метрики Jungle/surface не изменились (`worldgen-prefix-world-compare.json/log`); повреждённую дверь покрывает отдельная регрессия Medium42. Финальный Release suite4354/4354, affected133/133 и шесть Windows NativeAOT smoke-проверок прошли. Linux NativeAOT и GUI playthrough не заявляются.

В новом Jungle/surface-проходе также создана свежая native Small1458; неизменённая копия до checkpoint загрузилась в официальном1.4.5.8. С тем же независимым official reference текущие tileL1=0.355396, wallL1=0.719732, liquidratio=1.081689, silhouettecorr=0.926584, сундуки181→142 и dungeon delta(+85,-6). Существующие budgets проходят без изменений; fingerprint candidate `337af1242e5fbf1cc79412b2000a227b4e3ef2892dff8646a722408410762e3a` всё ещё отличается. Evidence: ignored `jungle-surface-world-compare.json/log`. Старые измерения ниже сохранены для истории, не как показатели новой карты.

Свежий Small/Classic/Corruption seed1458 создан итоговым Windows NativeAOT candidate и независимо официальным Windows1.4.5.8 с copied seed `1.1.1.1458`. Candidate прошёл cold startup до listening; его копия до checkpoint также достигла официального `Server started` и завершилась через `exit-nosave`. Новый официальный reference сравнивался с этой неизменённой копией, а не с миром после runtime checkpoint.

Существующие структурные бюджеты `WorldCompare --enforce` пройдены без изменения. Однако tile histogram L1=0.390148, wall L1=0.759073, liquid ratio=1.139229, silhouette correlation=0.926982; сундуков181→145, dungeon anchor delta(+69,+6). WorldSurface337 и rockLayer457 совпали. Исходный fingerprint `a73ec6c799c5e6ce3f9684377e97b07770caf19f4369c63d237d20c4fb21fa26` отличается от candidate `39be088b0f443925ecab82880e378c27d7c0a1e2415896e0fc265a5c475c2085`. Это измеренный оставшийся долг полного мира, а не равенство или GUI playthrough acceptance. Логи/миры/report находятся в ignored `.cache/terrain-parity-*`; дополнительный локальный seed не означает прогон remote CI case8675309.

Выбранные количества active tiles локализуют следующие проверки; это не квоты для дорисовки карты:

| Материал (source IDs) | Оригинал | Candidate |
| --- | ---: | ---: |
| Кирпичи данжа (41/43/44) | 84043 | 120549 |
| Sandstone/hardened sand (396/397) | 56692 | 41297 |
| Снег/лёд (147/161) | 135503 | 111249 |
| Living wood/leaves (191/192) | 4603 | 783 |
| Lihzahrd brick (226) | 25684 | 1121 |
| Ebonstone (25) | 51746 | 30363 |
| Ash (57) | 386135 | 382736 |
| Cloud/rain cloud (189/196) | 8053 | 8049 |

Расхождения Temple и Living Tree делают их builders приоритетными продолжениями. Близкие количества Ash/Cloud не доказывают итоговую геометрию; больше кирпичей данжа не означает больший или правильно связанный внутренний объём.
