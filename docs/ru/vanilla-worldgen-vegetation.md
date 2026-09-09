# Vanilla worldgen: растительный блок до Mushrooms

Этот документ описывает поздний растительный слой совместимого с Terraria 1.4.5.8 генератора `terraruntime:vanilla`.

## Объём реализации

Более ранний генератор пепельных деревьев Underworld также выполняет проверенную обработку свежих корней после роста из `GrowTreeWithSettings` / `CheckTreeWithSettings`. Первоначальный допуск корня остаётся общим; финальная опора должна быть пепельной травой633. Удаление неподходящего корня сохраняет исходные вызовы RNG для пыли и запись кадров неактивной клетки. Финальные кадры основания с одним корнем отличаются от первоначальных. Это не универсальная реализация разрушения деревьев.54 независимых официальных проверки полных клеток/RNG охватывают пепельную и смешанную травяную опору;12 отдельных проверок крепостей сохраняют входящие кадры кирпичей и окончания плоских платформ. Полный ordinary-префикс Underworld теперь совпал во всех9 canonical сочетаниях seed/размера, включая жидкости и порядок метаданных комодов; special seeds и полная parity мира не подтверждены.

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

Dye Plants `227` занимают одну плитку, но используют горизонтальный шаг атласа $34\,\text{пикселя}$ из `PlaceDye` и frameY `0`, а не обычный шаг $18\,\text{пикселей}$. Генерация и загрузка используют общий контракт кадров для ограниченного набора стилей `0..11`. Корректные растения могут уничтожаться лавой при загрузке без предоставления live mining/drop authority; повреждённые кадры остаются fail-closed. Исправление кадров не делает существующие приближения плотности размещения и выбора стиля source-exact.

Обычный рост кактусов теперь использует source-backed `PlantCactus`/`GrowCactus` вместо фиксированных коротких столбиков. За первой попыткой основания следуют все 150 случайных пар кандидатов, даже при отказе. Рост учитывает четыре conversion sands, порядок сканирования песка/соседних кактусов, целочисленный liquid-volume gate, случайное выравнивание slope, поиск основания/ветви, population cap, случайную высоту ствола, поднятые боковые ветви и paint/coatings. Область воды составляет $100\times50\,\text{tiles}$; отказ наступает только при целочисленном объёме больше 25 полных тайлов. Косметический `CactusFrame` намеренно не выполняется: source `TileFrame` пропускает его при `generatingWorld`, поэтому нулевые косметические frames сами по себе не дефект генерации. Scripted-тесты проверяют рост/RNG/лимиты; canonical Small seed `42` теперь требует поднятых ветвей. Отключение ветвей ломает и scripted shape test, и проверку полной карты. Плотность кандидатов/oasis scheduling смешанного прохода, special-seed и runtime growth остаются открытыми.

Поздний проход `Cactus, Palm Trees, & Coral` теперь использует ordinary-срез `GrowPalmTree` из 1.4.5.8 вместо коротких столбиков с нулевыми frames: четыре песчаных основания, сухое/ровное основание, проверки стен и полной высоты свободного места, порядок shared RNG для высоты/изгиба/сегментов, paint/coating и frames основания/верхушки. Всегда истинное условие изгиба в source по-прежнему расходует предшествующие short-circuit RNG-вызовы. Поиск поверхности начинается сверху мира и заканчивается перед `worldSurface-1`; прежнее узкое окно слоёв пропускало сухие пляжи. Исправление покрыто scripted/gate-тестами и canonical seed `42`. Плотность размещения, oasis scheduling, кораллы/ракушки и special-seed варианты остаются частичными; полный parity смешанного прохода не заявляется.

Поздняя растительность должна сосуществовать с сундуками, дверями, горшками, ловушками, алтарями, fallen logs, floating-island house и другими framed objects, созданными раньше. Поэтому placement требует свободных target cells и избегает frame-important объектов поблизости там, где более крупным структурам, например деревьям, нужен запас места.

Рост обычных деревьев tile-`5` теперь использует clean-room порт `WorldGen.GrowTree` из TerrariaServer 1.4.5.8. Version-pinned capability catalogs владеют полными наборами tree ground, common sapling, replaceable growth и plant-growth wall. Grower владеет source-гейтами высоты и clearance, порядком shared RNG, вариантами ствола, правилом неповторяющихся соседних ветвей, нормализацией корней, переносом paint/coating и полным framing верхушки. Raw content IDs и координаты sprite atlas не протекают в алгоритм роста: ими владеют typed catalogs и отдельный tree-frame catalog.

Проход `Planting Trees` по-прежнему использует консервативное число кандидатов и выбор surface columns в TerraRuntime, не заявляя byte-identical плотность размещения `WorldGen.AddTrees` или полные ветки palm/vanity trees. Это ограничения placement/content family, а не оставшийся дефект сегментированного ствола или отсутствующей верхушки у обычных деревьев, которые выращивает проход.

Декор джунглей `233` больше не публикуется ошибочной одиночной клеткой. Существующий проход выбора кандидатов вызывает source-backed часть `PlaceJunglePlant` для свободного footprint: styles `0..7`, полный framing $3\times2$, три плоские неактуированные опоры из Jungle grass, наследование block paint/coating, сохранение независимых жидкости/стен/проводов. При отказе полного размещения подстановка не создаётся. Полное распределение кандидатов, замена активных растений/каскады и генерация отдельной ветки декора $2\times2$ остаются открытыми. Тесты настоящего запуска сгенерированных миров проверяют целостность декора до liquid preparation; прежний одноклеточный код эти проверки проваливает.

## Пепельная растительность ада

Обычный проход `Underworld` теперь выполняет source-сканирование пепельной растительности непосредственно перед `AddHellHouses`. Исключены крайние 25 колонок; лес определяется строгим условием `x < width * 0.17 || x > width * 0.83`. Активный пепел `57` превращается в пепельную траву `633`, если хотя бы одна из восьми соседних клеток неактивна. Нижняя граница сканирования травы заново вызывает `Next(-1,2)` при каждой проверке условия цикла, включая завершающую. Отдельный последующий проход пытается вырастить дерево при `Next(3)==0` только над открытой пепельной травой.

`AshTreeGrower1458` реализует generation-time часть `GrowTreeWithSettings(Tree_Ash)`, а не обычный `GrowTree` с подменённым ID. Используются пепельная трава, существующие каталоги допустимых стен и заменяемой растительности, сухое основание из трёх клеток, высота $7..12\,\text{клеток}$, свободная область шириной пять колонок и четыре клетки запаса над кроной. Переиспользуется атлас деревьев с сохранением source-перебросов ветвей, краски/coatings, независимых бросков левого/правого корня даже при неподходящей стороне, общего набора почв для корней, settings-specific кадров основания и последних бросков лиственной/голой кроны. Runtime-рост и ветви Drunk/Remix/специальных seed этим helper не разрешаются.

Scripted tests проверяют отказ без мутации, точный RNG/кадры комбинаций корней и повторного выбора ветвей, границы высоты, область сканирования и открытость травы. Полные канонические Small/Medium/Large после финализации сохраняют краевую траву и кадры крон; отключение вызова растительности ломает все три теста. Это проверка реализованной растительности, а не точного рельефа ада или совпадения всей карты по seed.

## Поведение проходов

- `Sunflowers` ставит framed 2x4 sunflower objects на соседние блоки surface grass.
- `Planting Trees` выбирает консервативные forest, Jungle и snow candidates, затем применяет source-backed growth gates `WorldGen.GrowTree` и полные frames ствола, ветвей, корней и верхушки.
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

Focused contracts закрепляют 100-entry graph, точный pass segment после `Guide`, принадлежность `VanillaSharedRng`, следующую source boundary (`Gems In Ice Biome`), canonical-size gating и special-seed fallback. Tree-тесты дополнительно закрепляют exact scripted frames и расход RNG, reroll соседних повторяющихся ветвей, growth rejection gates, replaceable vegetation и размеры всех четырёх source capability sets. Canonical generated-world test требует настоящие frames верхушек, ветвей и корней в собранном workspace. `tools/ci/probe_worldgen_tree_growth.py` независимо сверяет runtime catalogs и framing routes с pinned decompile 1.4.5.8. Полный generated-world workflow затем собирает настоящий `.wld`, повторно загружает его через TerraRuntime и запускает pinned официальный сервер с этим файлом.

## Следующая граница

Следующий source block начинается с `Gems In Ice Biome`, затем идут `Random Gems`, `Moss Grass`, `Muds Walls In Jungle` и `Larva`. Эти проходы снова относятся к underground material/biome decoration, поэтому их разумнее проверять отдельно от surface vegetation.
