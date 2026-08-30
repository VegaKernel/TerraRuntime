# Семантика зон мира

[English](../en/world-zone-semantics.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime предоставляет глубину и принадлежность к биомам через типизированную gameplay-семантику, а не через разрозненные числовые проверки или persistence/network flags.

## Классификация глубины

`VanillaWorldDepthZoneResolver` воспроизводит взаимоисключающую вертикальную классификацию TerrariaServer 1.4.5.8 `SceneMetrics`: небо заканчивается на `worldSurface * 0.35f`, поверхность — на `worldSurface`, земля — на `rockLayer`, каменный слой — на `maxTilesY - 200`, ниже расположен underworld. Включение граничных значений закреплено исполняемыми тестами. Некорректные координаты и противоречивая геометрия слоёв отклоняются.

## Принадлежность к биомам

`VanillaWorldBiomeFlags` именует независимые gameplay-признаки, включая Corruption, Crimson, Hallow, Jungle, Snow, Desert, Dungeon и Shimmer. `VanillaWorldZoneState` проверяет известные биты и хранит их отдельно от единственной зоны глубины. Комбинации допустимы: одна позиция может одновременно удовлетворять нескольким scene-условиям.

Эти flags являются семантической выходной границей для будущего source-backed подсчёта тайлов `SceneMetrics`. Они не являются битами пакета или world-файла.

## Граница возможностей

Этот slice завершает типизированную семантику biome/zone, но не заявляет реализацию tile-count threshold scanning или полный паритет определения биомов. Радиус census, thresholds, соседние структуры, исключения special seeds и интеграция с player scene остаются отдельной source-backed работой.
