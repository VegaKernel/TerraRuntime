# Дифференциальная проверка ванильной генерации мира

TerraRuntime использует исполняемый differential-gate против зафиксированного официального TerrariaServer 1.4.5.8. Эта проверка отделена от unit-тестов: она отвечает на вопрос, остаётся ли полностью сохранённый мир `terraruntime:vanilla` в известных структурных границах, когда оба генератора получают одинаковый канонический размер и seed.

## Канонический reference-case

Workflow `.github/workflows/vanilla-worldgen-reference-differential.yml` использует:

- формат мира Terraria 1.4.5.8 `326`;
- Small, `4200 x 1200` тайлов;
- seed `8675309`;
- обычную сложность и corruption;
- официальный архив dedicated server `terraria-server-1458.zip`;
- зафиксированный SHA-256 `TerrariaServer.exe`: `d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e`.

Официальный мир и мир TerraRuntime загружаются собственным loader'ом TerraRuntime и сравниваются инструментом `tools/TerraRuntime.WorldCompare`.

## Что сравнивается

В отчёт входят размеры и версия формата, spawn и dungeon anchors, уровни world surface и rock layer, тип evil, суммарные active tiles/walls/liquids, нормализованные расстояния гистограмм tile/wall, количество сохранённых chest/sign/NPC/tile entity и детерминированный SHA-256 fingerprint декодированной сетки тайлов.

Ключ `--enforce` превращает отчёт в regression gate. Среди обязательных условий: одинаковые размеры и формат, dungeon на той же стороне мира для одинакового seed, допустимые структурные ratios и histogram distances, ограниченные отклонения слоёв и anchors, наличие ключевых материалов биомов/структур, сохранённых сундуков и стартового town NPC. Нарушение бюджета возвращает ненулевой exit code и валит CI.

Для того же seed есть быстрый unit-level checkpoint `WorldGen.Reset`. Для `8675309` source-backed bootstrap обязан выбрать правую сторону dungeon и reset-location `3364`; также зафиксировано первое RNG-значение, которое после Reset должен увидеть Terrain. Это ловит изменение порядка RNG-вызовов до дорогого полного прогона reference-world.

## Уровни доказанности

Зелёный CI сам по себе не означает полную vanilla parity. В проекте надо различать три утверждения:

1. **Implemented**: в runtime существует конкретная реализация pass/subsystem.
2. **Tested**: реализация покрыта unit/integration/live контрактами.
3. **Reference-proven**: реальный мир, созданный официальным TerrariaServer, прошёл differential gate для зафиксированного case.

Текущие differential budgets являются границами регрессии, а не требованием побайтового совпадения. Structural SHA-256 сохраняется как доказательство и индикатор изменения, но пока не обязан совпадать с официальным fingerprint. По мере перевода отдельных passes на точную source-backed реализацию бюджеты следует ужесточать, а не расширять ради зелёной галочки. Удивительно радикальная мысль для CI, но полезная.

## Артефакты

Каждый workflow-run сохраняет comparison JSON, официальный server log/config, официальный `.wld` и candidate `.wld` TerraRuntime. Поэтому красный gate можно разбирать по конкретным мирам, а не по одному числу в консоли.
