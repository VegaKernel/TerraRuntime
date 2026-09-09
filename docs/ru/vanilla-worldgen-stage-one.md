# Vanilla-генерация мира: ранний pipeline Terraria 1.4.5.8

`terraruntime:vanilla` теперь разворачивает первый этап генерации в отдельные проходы вместо прыжка из `Terrain` сразу в старый агрегат `Biomes`.

Для обычных миров канонического размера ранний граф идёт в source-порядке:

`Reset → Terrain → TerrainLayers → Dunes → OceanSand → SandPatches → Tunnels → MountCaves → DirtWallBackgrounds → RocksInDirt → DirtInRocks → Clay → SmallHoles → DirtLayerCaves → RockLayerCaves → SurfaceCaves → WavyCaves → GenerateIceBiome → Grass → Jungle`.

Исходный `WorldGenerator.RunPass` заново создаёт `Main.rand` перед каждым enabled pass. `VanillaSharedRng` сохраняет общий порядок вызовов внутри прохода, а не переносит поток из предыдущего прохода. Локальные генерационные проходы используют `IsolatedDeterministic`.

## Завершение Terrain state

Сам Terrain выполняет оба финальных броска `waterLine`/`lavaLine` из уже продвинутого RNG и публикует результат. TerrainLayers переносит эти значения и сохранённый Reset без новых случайных вызовов; отсутствие liquid state вызывает ошибку. Броски в отдельно reseeded bridge были подтверждённым багом, исправленным2026-09-08. Девять независимых официальных Terrain fixtures закрепляют клетки, liquid lines и следующий RNG.

## Ранние изменения мира

Stage-one pipeline теперь содержит source-shaped реализации дюн, океанического песка, песчаных карманов, тоннелей, mount caves, перемешивания dirt/rock, clay, small holes, dirt/rock layer caves, surface caves, ледяного биома, grass и первого Jungle.

Горячие tile-циклы работают напрямую с непрерывным `WorldTileStore` candidate-мира. Это особенно важно для большого Jungle `TileRunner`: кандидат остаётся непубличным и не создаёт live dirty backlog, но мы не платим за миллионы вызовов общего workspace-интерфейса.

`Wavy Caves` для обычного мира является явным no-op, потому что его изменения относятся к special-seed веткам, которые пока остаются на compatibility-пути.

## Граница compatibility

Этот документ описывает ранний overlay, а не весь shipping plan. `SourceBackedFinal1458` далее заменяет ordinary aggregate mutations вплоть до Final Cleanup; Biomes/Caves/Ores там являются немутирующими compatibility barriers. Наличие проходов не доказывает точную геометрию. [Полный аудит](vanilla-worldgen-pass-audit.md) перечисляет каждого владельца и оставшийся долг. Существующие special-seed/noncanonical paths остаются отдельными; ограниченный pure Remix slice не означает полную Remix parity.

## Проверка

Release-gate остаётся прежним: сборка, профильные worldgen-тесты, генерация реального canonical `terraruntime:vanilla` `.wld`, проверка загрузчиком TerraRuntime и затем запуск мира на pinned официальном TerrariaServer 1.4.5.8.
