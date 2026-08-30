# Vanilla-генерация мира: ранний pipeline Terraria 1.4.5.8

`terraruntime:vanilla` теперь разворачивает первый этап генерации в отдельные проходы вместо прыжка из `Terrain` сразу в старый агрегат `Biomes`.

Для обычных миров канонического размера ранний граф идёт в source-порядке:

`Reset → Terrain → TerrainLayers → Dunes → OceanSand → SandPatches → Tunnels → MountCaves → DirtWallBackgrounds → RocksInDirt → DirtInRocks → Clay → SmallHoles → DirtLayerCaves → RockLayerCaves → SurfaceCaves → WavyCaves → GenerateIceBiome → Grass → Jungle`.

Проходы, участвующие в общем vanilla-потоке, используют один Terraria-compatible `UnifiedRandom`. Проходы с отдельной генерационной случайностью выполняются как `IsolatedDeterministic`, поэтому они не могут случайно сдвинуть общий vanilla RNG.

## Завершение Terrain state

Промежуточный проход после `Terrain` публикует уже рассчитанное состояние `WorldGen.Reset` для следующих проходов и завершает слой параметров глубины. В частности, для обычного пути 1.4.5.8 выполняются два post-terrain RNG-вызова для `waterLine` и `lavaLine`, вместо фиксированных приблизительных глубин.

## Ранние изменения мира

Stage-one pipeline теперь содержит source-shaped реализации дюн, океанического песка, песчаных карманов, тоннелей, mount caves, перемешивания dirt/rock, clay, small holes, dirt/rock layer caves, surface caves, ледяного биома, grass и первого Jungle.

Горячие tile-циклы работают напрямую с непрерывным `WorldTileStore` candidate-мира. Это особенно важно для большого Jungle `TileRunner`: кандидат остаётся непубличным и не создаёт live dirty backlog, но мы не платим за миллионы вызовов общего workspace-интерфейса.

`Wavy Caves` для обычного мира является явным no-op, потому что его изменения относятся к special-seed веткам, которые пока остаются на compatibility-пути.

## Граница compatibility

После source-backed Jungle старый `Biomes` остаётся только как residual-слой для ещё не перенесённых частей генерации. Он получает отдельный compatibility RNG и поэтому не портит source shared RNG. Его прежняя широкая перерисовка jungle типами 59/60 фильтруется и не может затереть новую source-backed джунглю.

Caves, ores, dungeon, special/secret-seed логика и финальные проходы ещё остаются следующими этапами миграции. Special seeds и неканонические размеры продолжают использовать прежний compatibility-план без изменений.

## Проверка

Release-gate остаётся прежним: сборка, профильные worldgen-тесты, генерация реального canonical `terraruntime:vanilla` `.wld`, проверка загрузчиком TerraRuntime и затем запуск мира на pinned официальном TerrariaServer 1.4.5.8.
