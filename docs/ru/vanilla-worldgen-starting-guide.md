# Vanilla worldgen: сохранение стартового Guide

Этот документ описывает первую границу генерации NPC в совместимом с Terraria 1.4.5.8 пайплайне `terraruntime:vanilla`.

## Объём реализации

Обычный canonical pipeline теперь продвинут от `Grass Wall` через закреплённый исходным каталогом проход `Guide`. Идентичность встроенного генератора не меняется: `terraruntime:flat` остаётся отдельным минимальным генератором, а `terraruntime:vanilla` остаётся единственным постепенно переводимым на source-backed реализацию vanilla-профилем.

Для canonical ordinary worlds production-план увеличивается с 88 до 89 entries. `Guide` выполняется сразу после `Grass Wall` и до compatibility-барьера `SecretSeeds`, как в pinned-каталоге Terraria.

## Владение generated NPC

Сгенерированный town NPC не является тайлом. Если оставить Guide только во временном состоянии генерации, мир будет выглядеть правильным до сохранения, после чего NPC молча исчезнет.

Поэтому `Workspace` теперь владеет side table сгенерированных town NPC рядом с уже существующей side table сундуков. Generation регистрирует NPC до публикации; `RuntimeWorldCreationPersistencePipeline` забирает side table вместе с финализированным candidate; `WorldFileFreshComposer326` передаёт её существующему canonical `WorldFileNpcEncoder`.

После композиции готовый byte image снова загружается через `WorldFileLoader`, и composer проверяет, что количество NPC пережило encode/load transaction. Таким образом generated NPC находятся в той же атомарной границе candidate-to-file, что tiles и chests.

## Контракт Guide

Source-verified внутренний Terraria NPC ID для Guide равен `22`. В ordinary world Guide размещается в world spawn, опубликованном предыдущим проходом `Spawn Point`. Позиция NPC сохраняется в пиксельных координатах Terraria, home coordinates остаются тайловыми.

Стартовый Guide сохраняется как homeless, потому что новый обычный мир ещё не содержит построенного игроком валидного дома. Используется стабильное допустимое vanilla-имя Guide `Andrew`, причём shared worldgen RNG не расходуется. Выбор given name NPC относится к отдельному naming/random surface Terraria; имитация его через `WorldGen.genRand` сдвинула бы RNG для всех следующих worldgen-проходов.

Это ограничение зафиксировано явно, а не спрятано под ложным заявлением о byte-parity.

## Проверки

Focused contracts проверяют:

- canonical plan из 89 entries;
- порядок `Grass Wall -> Guide -> SecretSeeds`;
- принадлежность descriptor к `VanillaSharedRng` без выдуманного расхода RNG внутри Guide;
- compatibility fallback для noncanonical worlds;
- запрет дублей generated town NPC;
- размещение Guide в source-backed spawn coordinates;
- реальный round-trip NPC section через fresh `.wld` encode/load с сохранением ID, имени, позиции и homeless state;
- полную acceptance-загрузку мира официальным TerrariaServer 1.4.5.8.

## Следующая граница

Дальше pinned-порядок снова возвращается к tile/object vegetation: `Sunflowers`, `Planting Trees`, `Herbs`, `Dye Plants`, `Webs And Honey`, `Weeds` и последующие plant/biome decoration passes. Они могут использовать уже готовый NPC bridge без ещё одного отдельного persistence-механизма.
