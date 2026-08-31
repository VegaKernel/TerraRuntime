# Встроенная vanilla-генерация мира

[English](../en/vanilla-world-generation.md) · [Генерация мира](world-generation.md) · [Roadmap](../roadmap/gameplay-worldgen-extensibility.md)

`terraruntime:vanilla` является runtime-owned clean-room генератором TerraRuntime для TerrariaServer 1.4.5.8. Публичный ID генератора остаётся стабильным, а точным составом generation plan владеет runtime.

## Pipeline обычного canonical world

Для трёх канонических размеров Terraria (`4200x1200`, `6400x1800`, `8400x2400`) и ordinary seed profile production-provider теперь собирает source-backed/source-shaped overlay-цепочку до конца закреплённого каталога из 109 проходов.

```mermaid
flowchart LR
    Reset["Reset"] --> Terrain["Terrain"]
    Terrain --> Early["ранний terrain / caves / biomes"]
    Early --> Structures["dungeon / jungle / temple / hives"]
    Structures --> Objects["liquids / chests / spawn / vegetation"]
    Objects --> Micro["Micro Biomes"]
    Micro --> Settle["Settle Liquids Again"]
    Settle --> Nature["Cactus, Palm Trees, & Coral"]
    Nature --> Cleanup["Tile Cleanup"]
    Cleanup --> Altar["Lihzahrd Altars"]
    Altar --> Water["Water Plants"]
    Water --> Stalac["Stalac"]
    Stalac --> Traps["Remove Broken Traps"]
    Traps --> Final["Final Cleanup"]
    Final --> Secrets["compatibility SecretSeeds barrier"]
    Secrets --> Metadata["Metadata + fresh .wld v326"]
```

Все source-backed проходы ordinary world, участвующие в vanilla generation, сохраняют `WorldGenerationRngMode.VanillaSharedRng`. Здесь `SharedRng` означает общий vanilla worldgen RNG API **внутри одного pass**, а не непрерывный поток через весь generation plan. Закреплённый TerrariaServer 1.4.5.8 в `WorldGenerator.RunPass` выполняет `Main.rand = new UnifiedRandom(_seed)` перед применением каждого enabled pass, поэтому TerraRuntime создаёт новый `VanillaUnifiedRandom1458` из разрешённого world seed для каждого такого прохода. RNG-вызовы внутри pass последовательно двигают его локальное состояние; перенос состояния RNG из одного зарегистрированного pass в следующий является ошибкой совместимости.

Постоянный source contract `terraria-worldgen-pass-catalog.yml` декомпилирует закреплённый официальный сервер и теперь падает, если reseed в `RunPass` перестанет происходить до применения pass. Так runtime-тесты больше не могут сами себе доказать неверное время жизни RNG.

## Финальный overlay из восьми проходов

`SourceBackedVanillaWorldGenerationFinal1458` завершает ordinary canonical sequence после `Micro Biomes` последними восемью регистрациями TerrariaServer 1.4.5.8:

1. `Settle Liquids Again`
2. `Cactus, Palm Trees, & Coral`
3. `Tile Cleanup`
4. `Lihzahrd Altars`
5. `Water Plants`
6. `Stalac`
7. `Remove Broken Traps`
8. `Final Cleanup`

Они намеренно остаются отдельными passes, а не сливаются в один огромный cleanup. Так сохраняются source order, граница per-pass RNG reseed, pass-level progress, диагностика зависимостей и точка замены каждого алгоритма при дальнейшем parity-порте.

Поздние passes выполняют детерминированное осаждение жидкостей, размещение пустынной/пляжной растительности и coral, нормализацию tile state, установку Lihzahrd altar в Temple, водные растения, cave stalactite/stalagmite decoration, удаление orphan traps и финальную проверку vanilla content/flags перед compatibility barrier секретных seed.

## Выбор реализации и fallback

Полная source-backed цепочка включается только если одновременно выполнены два условия:

- seed profile обычный/default;
- размер мира является одним из canonical Terraria sizes.

Special/secret seeds и synthetic noncanonical dimensions намеренно воспроизводят compatibility provider. Ordinary-world source-backed implementations не используются как приблизительная подмена их собственных правил генерации.

Production-регистрация в `BuiltInWorldGeneratorSource` разрешает `terraruntime:vanilla` в `SourceBackedVanillaWorldGenerationFinal1458`. Более ранние overlay-классы остаются внутренними слоями этой цепочки, а не отдельными публичными генераторами.

## Persistence и authority

Generation работает с неопубликованным `RuntimeWorldGenerationWorkspace` поверх contiguous `WorldTileStore`. Tiles, chests, metadata стартовых town NPC, spawn/dungeon anchors и layers остаются candidate state до успешной validation. Generation passes не изменяют live network-visible world.

`Final Cleanup` отвергает tile/wall ID вне закреплённых vanilla catalogs и неизвестные runtime tile flags до передачи результата обычному world-generation finalizer и fresh `.wld` v326 composer.

## Граница проверки

Здесь есть две разные вехи, и смешивать их нельзя:

- **полное source-pinned покрытие pass pipeline**: ordinary canonical plan проходит все 109 identity TerrariaServer 1.4.5.8 вплоть до `Final Cleanup`;
- **reference-world parity**: фиксированные официальные seeds дают reference-equivalent результат с доказанным per-pass RNG behavior и parity геометрии/content.

Первая веха реализована финальным overlay. Вторая остаётся задачей доказательства, пока reference-world differential tests её не подтвердят. Ряд уже существующих source-shaped алгоритмов сохраняет pass boundaries и deterministic ownership, но ещё требует более глубокого parity.

`terraria-vanilla-generated-world-acceptance.yml` остаётся executable production gate: сборка runtime, focused world-generation contracts, генерация реального canonical vanilla world, загрузка полученного `.wld` через TerraRuntime и запуск закреплённого официального TerrariaServer 1.4.5.8 с этим миром.
