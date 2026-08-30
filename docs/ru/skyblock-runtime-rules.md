# Runtime-правила Skyblock

TerraRuntime не определяет геймплей по тому, каким генератором когда-то был создан мир. Генерация выполняется один раз, а runtime-поведение выводится из сохранённого состояния Terraria и текущего набора тайлов.

В Terraria 1.4.5.8 нужным сохранённым свойством является vanilla-флаг `SkyblockWorld`. TerraRuntime уже читает и сохраняет его в `WorldFileRuntimeMetadata`. Встроенный путь создания `terraruntime:skyblock` теперь публикует мир с этим же vanilla-флагом, поэтому после любых перезапусков семантика мира сохраняется без хранения `WorldGeneratorId`.

## `lowTiles`

`VanillaSkyblockRuntimePolicy1458` повторяет source-backed проверку плотности Skyblock:

- у мира должен быть установлен vanilla-флаг `SkyblockWorld`;
- активными тайлами должно быть заполнено менее 10% всех ячеек мира;
- ровно 10% заполнения уже не считается `lowTiles`.

Проверка выполняется по текущему authoritative `WorldTileStore`. Поэтому состояние может измениться по мере застройки мира игроками и не фиксируется навечно при генерации.

При активном `lowTiles` policy выдаёт правила Terraria 1.4.5.8 для следующих gameplay-подсистем:

- порог Snow biome: 300 тайлов вместо 1500;
- порог Desert biome: 300 тайлов вместо 1500;
- Hardmode conversion пропускается.

Значения защищены workflow `Skyblock Runtime Policy Source Contract`: он декомпилирует закреплённый официальный TerrariaServer 1.4.5.8 и проверяет `SceneMetrics`, `WorldGen.Skyblock.lowTiles` и `WorldGen.GERunner`.

Policy намеренно не зависит от `WorldGeneratorId`. Разреженный обычный мир остаётся обычным, а загруженный vanilla Skyblock получает те же правила, даже если TerraRuntime его не создавал.
