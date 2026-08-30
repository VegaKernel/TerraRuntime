# Владение намеренными литералами

[English](../en/gameplay-literal-ownership.md) · [CI-гейт доменных литералов](gameplay-domain-literal-gate.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

Не каждое число является gameplay identity. TerraRuntime оставляет намеренные raw values у явно определённых владельцев и запрещает переносить их в несвязанный gameplay-код.

| Владелец | Намеренные raw values | Правило границы |
|---|---|---|
| `Vanilla*Ids` и definition catalogs | version-pinned content numbers, counts и выбранные metadata | gameplay использует typed IDs или definitions без копирования литералов |
| packet/frame codecs и protocol projections | message fields, bit layouts, sentinel values и primitive capacities | decode/validation выполняются до gameplay, encode — только из semantic state |
| `WorldFile*`, prepared-state и snapshot codecs | порядок полей `.wld`, raw enums, section markers и format limits | persistence primitives остаются внутри именованного adapter |
| passes `Vanilla*WorldGeneration*1458` | RNG bounds в source order, pass-local tile aliases, dimensions и thresholds | значения принадлежат version-pinned generation pass и не становятся общими gameplay constants |
| владельцы behavior/physics | ticks, pixels, speeds, probabilities и local arithmetic | observable/non-obvious значения получают named constants или parameter records, tests закрепляют управляемую ветвь |
| tests и verification tools | точные fixtures, invalid sentinels и official reference values | литералы являются evidence/input, а не runtime identity API |

Сейчас lexical audit не содержит suppressions в production source. Если позже исключение действительно понадобится, та же строка должна указывать имя rule и содержательную причину; suppression сам становится частью записи о владении.

Маркеры имён файлов `Generation`, `Packet`, `Protocol`, `Codec`, `WorldFile`, `Snapshot` и подобные являются объявлениями audit boundary, а не безусловными исключениями. Даже такой файл не должен принимать несвязанные gameplay-решения по raw identities.

Текущий textual gate высокосигнален, а его self-tests покрывают каждый enforced pattern. Roslyn analyzer остаётся optional, пока развитие синтаксиса или повторяющиеся false negatives не покажут недостаточность lexical enforcement.
