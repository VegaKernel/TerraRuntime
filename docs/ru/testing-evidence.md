# Testing, verification и evidence

[English](../en/testing-evidence.md) · [Документация](README.md) · [Reference policy](../reference-policy.md) · [Roadmap](../roadmap.md)

## 1. Зачем нужен этот документ

TerraRuntime восстанавливает наблюдаемое поведение TerrariaServer 1.4.5.8 без повторения оригинальной внутренней архитектуры. Обычный зелёный unit-test suite необходим, но сам по себе не доказывает vanilla parity.

Поэтому проект использует несколько уровней evidence. Каждый отвечает на свой вопрос.

```text
build/static checks
      |
unit/integration tests
      |
NativeAOT/CoreCLR smoke paths
      |
independent packet/world fixtures
      |
official-source contract probes
      |
real official-world/client/server behavior
```

Более сильные уровни применяются, когда нижние могут разделять ту же ошибочную гипотезу, что и implementation under test.

## 2. Source hierarchy

При расхождении источников порядок такой:

1. locally decompiled official `TerrariaServer.exe` **1.4.5.8** для gameplay behavior, `.wld` layout, ordering, constants и server semantics;
2. `VegaKernel/Multiplicity` 2.7.x для protocol **326** typed wire models;
3. `bybrooklyn/terrustia` как independent implementation cross-check и источник testing/performance ideas;
4. TShock/OTAPI только для historical compatibility и exploit lessons.

Secondary implementation никогда не переопределяет verified current official behavior.

Decompiled source остаётся local reference material. Нельзя коммитить copied method bodies, game assets или game text.

## 3. Политика roadmap checkboxes

Roadmap `[x]` означает: item verified на `main` implementation + tests/CI либо equivalent executable proof.

Нельзя ставить `[x]` только потому, что:

- существует type/interface;
- code compiles;
- stub возвращает нужное значение;
- self-round-trip successful;
- есть один architectural layer, а behavior incomplete;
- implementation просто похожа на decompiled source.

Partial/foundation-only work остаётся `[ ]`.

## 4. Build/test baseline

Main CI build/test job выполняет как минимум:

- bilingual documentation structure/link validation;
- .NET 11 restore;
- Release build с repository warning/analyzer policy;
- `TerraRuntime.Tests`;
- authoritative loop smoke;
- protocol smoke;
- network smoke;
- world smoke;
- Terminal UI smoke.

Failure здесь ломает базовый implementation baseline, но pass ещё не доказывает vanilla compatibility behavior-sensitive change.

## 5. Dual-host runtime gates

TerraRuntime поддерживает два runtime/shipping profiles и проверяет оба architectural direction.

### NativeAOT standalone

CI публикует и запускает Linux x64 и Windows x64 NativeAOT artifacts.

Artifact checks проверяют runnable deployment layout и required native sidecars до запуска smoke tests непосредственно из published directory.

NativeAOT smoke coverage включает loop, protocol, network, world и TUI paths.

### CoreCLR extensible host

CI также публикует self-contained CoreCLR extensible hosts для Linux x64 и Windows x64.

Drop-in host-module fixture собирается, устанавливается как trusted Vega-shaped host module и прогоняется через extensible-host smoke path.

Это защищает правило: core остаётся AOT-compatible, а managed extensible profile умеет грузить explicit trusted host modules.

## 6. Regression test обязан ловить regression

Bug-fix test полезен только если различает broken и fixed behavior.

Для нетривиального fix нужно убедиться, что test падает, если:

- убрать fix;
- вернуть previous behavior;
- повторно внести incorrect constant/order/path.

Test, проходящий и до, и после fix, почти ничего не доказывает про баг.

## 7. Ограничение self-round-trip

Protocol и persistence особенно подвержены ложной уверенности от self-round-trip.

Пример:

```text
our encoder -> our decoder -> same model
```

Обе стороны могут реализовать один и тот же неправильный byte layout и успешно пройти test.

Аналогично:

```text
our writer -> our reader -> same state
```

может быть зелёным, даже если resulting `.wld` Terraria интерпретирует иначе или вообще не принимает.

Round-trip tests полезны для internal consistency, но wire/file compatibility требует independent evidence.

## 8. Golden bytes и independent fixtures

Critical packet layouts по возможности фиксируются known-good bytes или independent captures.

Полезные independent fixtures:

- raw packet bytes официального client/server session;
- `.wld`, созданные official TerrariaServer;
- selected header/section values, полученные независимо;
- failure artifacts из live workflows.

Главное свойство: expected data не генерируется тем же code path, который тестируется.

## 9. Official-source contract workflows

Repository содержит dedicated workflows, использующие официальный TerrariaServer 1.4.5.8 reference согласно project reference policy и проверяющие узкие behavioral contracts.

Среди представленных областей:

- general Terraria source/reference probes;
- tile protocol/framing behavior;
- dirt/stone tile mutation behavior;
- projectile reference behavior;
- NPC reference behavior;
- sign behavior/persistence;
- chest behavior/persistence;
- world generation/load/save compatibility slices.

Workflow должен быть достаточно узким, чтобы failure указывал на реальный contract, а не превращался в двадцатиминутную чёрную коробку.

## 10. Live official-world verification

`Vanilla World Load` family является важным integration layer.

Workflow может создать настоящий world official TerrariaServer, затем прогнать TerraRuntime на этом artifact. Current probes покрывают важные end-to-end paths:

- world verification/loading;
- live join/bootstrap;
- movement relay;
- selected chest open/content behavior;
- warm runtime-snapshot startup;
- canonical `.wld` checkpoint restore/export;
- startup/cache timing evidence.

Это сильнее synthetic unit-test world, потому что assumptions проверяются на official-world artifact.

## 11. Differential behavior

Когда важен exact output/order, preferred approach — differential scenario:

```text
same input/scenario
   -> official TerrariaServer
   -> TerraRuntime
   -> compare observable output/state
```

Полезные comparison targets: packet sequences, world mutations, AI state transitions, projectiles, drops, persistence effects.

Difference классифицируется: implementation bug, known deliberate divergence, unsupported behavior или measurement noise.

## 12. Network/process tests

In-process tests могут скрыть OS scheduling, socket backpressure и process-lifecycle behavior.

Для networking/queue/shutdown claims по возможности используются real process/socket tests.

Важные scenarios:

- slow readers;
- connection admission churn;
- join bursts;
- queue saturation;
- SIGTERM/shutdown behavior;
- malformed traffic, после которого приходит valid connection;
- save during/after connection failure.

## 13. Malformed input и fuzzing

Trust-boundary tests должны доказывать bounded failure.

Targets:

- frame parser;
- variable-length packet decoders;
- section/tile input;
- `.wld` parsing;
- command/text parsing.

Сильный fuzz/adversarial test доказывает два свойства:

1. malformed input не валит process и не создаёт unbounded allocation/work;
2. после атаки server всё ещё способен обработать valid operation.

Permanent broad fuzz corpus пока incomplete и не должен описываться как finished coverage.

## 14. Persistence evidence

Persistence changes требуют более сильного evidence, чем обычное unit equality state.

В зависимости от change используются:

- official `.wld` inputs;
- preserved-section byte checks;
- interrupted/failed save recovery tests;
- atomic replacement checks;
- runtime-cache corruption fallback;
- cold/warm startup comparison;
- live chest/sign/tile persistence probes;
- reload verification resulting canonical checkpoint.

Unknown/newer layouts должны fail conservatively, а не получать green test за guessed offsets.

## 15. Gameplay evidence

Gameplay parity строится subsystem by subsystem.

Useful evidence:

- deterministic state-transition tests;
- verified definition/catalog constants;
- runtime slot/generation-reuse tests;
- collision/world-query tests;
- replication tests;
- official-source AI/default reference probes;
- real client/server behavior для ordering-sensitive interactions.

Generic NPC/projectile framework не доказывает coverage всех NPC/projectile types.

## 16. RNG-sensitive work

NPC AI, loot и world generation могут зависеть от RNG ordering, а не только от probability distribution.

Когда vanilla ordering важен:

- сохраняется sequence RNG consumption;
- избегается parallel execution, меняющий order;
- используются deterministic seeds/scenarios;
- outputs/state transitions сравниваются с official behavior, где возможно.

Statistically similar result не обязательно vanilla parity.

## 17. Performance evidence

Performance work требует before/after measurement на одном workload и world.

Измеряются релевантные:

- wall time;
- CPU time;
- allocation rate;
- GC counts/pauses;
- queue depth/backlog age;
- throughput;
- p50/p95/p99 tick/operation latency;
- RSS/working set при pooling/cache memory changes.

Нельзя merge optimization только потому, что она «должна быть быстрее».

## 18. CPU time и wall time

TerraRuntime использует оба типа timing, потому что они отвечают на разные вопросы.

Большой wall-time spike при низком authoritative-thread CPU может означать OS contention/scheduling, а не expensive simulation logic.

Нельзя сравнивать timing data из несовместимых clocks или workloads.

## 19. Production-like scale matrix

Scalability roadmap разделяет realistic player load и maximum connection stress.

Направление — проверять connection counts вроде:

```text
1
8
24
64
128
255
```

`24` — meaningful realistic optimization baseline; `255` — stress/scalability target, а не единственный заслуживающий внимания workload.

## 20. Failure artifacts

При падении live/reference workflow загружается небольшой artifact, объясняющий реальное отличие, а не только generic exit code.

Useful failure evidence:

- packet sequence summary;
- exact expected/observed counters;
- selected bytes/metadata;
- startup profile;
- world/checkpoint identity;
- runtime stop/rejection reason;
- relevant bounded logs.

Artifacts не должны копировать copyrighted decompiled source или ненужные большие game assets.

## 21. CI cancellation и concurrent main work

Main CI использует concurrency cancellation. Run может быть `cancelled` просто потому, что более новый push той же branch его supersede'нул.

Нельзя выдавать superseded cancelled run за test failure. И нельзя выдавать pending/queued run за green.

Для current head проверяется workflow run именно этого SHA.

## 22. Documentation validation

`tools/ci/check_documentation.py` является частью build-test gate.

Он проверяет:

- mirrored RU/EN page sets;
- required baseline documentation pages;
- repository-local relative Markdown link targets;
- запрет relative documentation links, выходящих за repository root.

Semantic translation equivalence checker не доказывает. За factual RU/EN parity отвечает human review.

## 23. Evidence для roadmap completion

Перед `[x]` надо определить proof, соответствующий claim.

Примеры:

```text
"interface exists"                  -> code + API tests могут быть достаточны
"packet bytes match Terraria"       -> independent bytes/live reference
"world saves safely"                -> real world + recovery/round-trip evidence
"AI matches vanilla"                -> state tests + official behavior reference
"optimization improves performance" -> reproducible before/after measurement
"NativeAOT deployable"              -> publish + run published artifact
```

Сила evidence должна соответствовать силе claim.

## 24. Checklist изменения tests/evidence

Test/evidence change не завершён, пока по необходимости:

- test способен упасть на regression, который якобы защищает;
- expected values independent при proof compatibility/parity;
- official reference version pinned;
- copyrighted local decompile material не committed;
- process/network behavior проверен на process/socket level, если in-process скрывает риск;
- performance claim имеет reproducible before/after data;
- roadmap checkboxes отражают evidence, а не оптимизм;
- эта страница и `docs/en/testing-evidence.md` обновлены вместе.
