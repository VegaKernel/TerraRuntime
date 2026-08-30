# Source-backed правила NPC loot

Первый NPC-loot срез TerraRuntime реализует NPC-specific правила standard-slime группы для Blue Slime по закреплённому TerrariaServer 1.4.5.8. Это **не** заявление о том, что уже реализованы все global, world-condition, event, bestiary и chained vanilla drop layers.

## Source contract

Отдельный workflow `NPC Loot Source Contract` скачивает официальный сервер 1.4.5.8, выбирает точную Windows assembly с SHA-256

`d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e`,

и декомпилирует только item-drop классы, необходимые этому срезу.

Проверенная последовательность NPC-specific правил Blue Slime:

1. `Gel(1, 1, 2)`;
2. `NormalvsExpert(SlimeStaff, 10000, 7000)`.

Named identities также фиксируются из официальных ID tables:

- `BlueSlime = 1`;
- `Gel = 23`;
- `SlimeStaff = 1309`.

```mermaid
flowchart LR
    Official["TerrariaServer 1.4.5.8"] --> Probe["NPC Loot Source Contract"]
    Probe --> IDs["typed NPC/item IDs"]
    Probe --> Tables["typed ordered loot table"]
    Probe --> Semantics["Common / ExtraGel / Expert semantics"]
    IDs --> Runtime["VanillaNpcLootEvaluator"]
    Tables --> Runtime
    Semantics --> Runtime
    Runtime --> Tx["RuntimeNpcLootWorldItemTransaction"]
```

## Почему это не плоская таблица вероятностей

`ItemDropRule.Gel(1, 1, 2)` создаёт два luck-scaled `CommonDrop` и оборачивает их в `DropBasedOnExtraGel`:

- normal branch: stack range `1..2`;
- branch при выполненном `DropExtraGel`: stack range `2..4`.

Поэтому loot engine получает результат семантического условия `DropExtraGel` явно. Он не пытается угадывать, почему Terraria включила это condition.

`NormalvsExpert` выбирает между двумя `CommonDrop` через `DropAttemptInfo.IsExpertMode`:

- normal denominator: `10000`;
- expert-mode denominator: `7000`.

Denominator передаётся в `Player.RollLuck`. Поэтому фактическая вероятность Slime Staff зависит от Terraria player-luck semantics; TerraRuntime **не** заменяет её скрытым приближением вида `Random.Next(denominator)`.

## Порядок CommonDrop

Pinned source contract подтверждает следующий порядок:

```mermaid
flowchart TD
    Rule["CommonDrop"] --> Luck["Player.RollLuck(denominator)"]
    Luck -->|failed| NoDrop["no item"]
    Luck -->|success| Stack["rng.Next(min, max + 1)"]
    Stack --> Drop["NpcLootDrop"]
```

Для успешного stack roll верхняя граница vanilla включительна, потому что random call получает `max + 1` как exclusive bound.

Для Gel wrapper диапазоны равны

$$
S_{normal} \in \{1,2\}, \qquad
S_{extra} \in \{2,3,4\}.
$$

## Typed table boundary

`VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable` является authoritative support boundary. Он возвращает `VanillaNpcLootTable`, содержащую:

- typed `NpcTypeId` владельца таблицы;
- immutable представление rules в исходном registration order;
- `RuleCount`;
- `MaximumDropCount`, вычисляемый из семантики допущенных rules вместо предположения, что число rules всегда равно необходимой world-item capacity.

Разница принципиальная. Ошибка lookup означает **unsupported**. В будущем source-verified NPC может законно иметь импортированную таблицу с нулём NPC-specific rules, и такой случай нельзя путать с неизвестным NPC только потому, что оба дают пустой rule span.

`GetNpcSpecificRules` оставлен только как compatibility view. Новый authoritative код использует typed table boundary.

## Runtime evaluation и transaction ownership

`VanillaNpcLootEvaluator` не создаёт heap allocations на evaluation path:

- caller передаёт `Span<NpcLootDrop>`;
- `INpcLootRollSource.RollLuck` владеет player-luck semantics;
- `INpcLootRollSource.NextInt32` владеет RNG stream;
- `VanillaNpcLootContext` передаёт `IsExpertMode` и семантический результат `DropExtraGel`;
- `TryEvaluateNpcSpecificTable` принимает уже проверенную typed table.

`RuntimeNpcLootWorldItemTransaction` разрешает ту же typed table **до** потребления loot RNG. Он заранее проверяет materializer support и резервирует `MaximumDropCount` world-item capacity, после чего evaluates/materializes успешные rules строго в registration order. Так сохраняется проверенный общий порядок `Main.rand` между loot rules и поведением `Item.NewItem`.

Сам evaluator по-прежнему не владеет world-item state. Transaction владеет slot reservation/commit, а `VanillaNpcLootWorldItemMaterializer` — source-backed spawn defaults и prefix/velocity RNG.

## Fail-closed scope

NPC без импортированной NPC-specific table считается unsupported вместо получения выдуманного generic loot. Сейчас catalog намеренно содержит только Blue Slime.

Этот срез также не объявляет полный vanilla loot parity для Blue Slime. Global и conditional rules вне проверенного standard-slime registration layer должны быть отдельно импортированы из official source до участия в authoritative drops.

## Проверка

`VanillaNpcLootRuleTests` проверяет:

- точного владельца table, rule order, constants и maximum-drop capacity;
- явное поведение unsupported table;
- normal и `DropExtraGel` stack ranges;
- выбор normal/expert denominator для Slime Staff;
- вызов `RollLuck` до stack RNG;
- порядок output при успехе обоих rules;
- fail-closed поведение для unsupported NPC и слишком маленького output buffer.

`RuntimeNpcLootWorldItemTransactionTests` дополнительно проверяет generation safety, capacity exhaustion, unsupported materialization, shared RNG ordering и точное staging/commit поведение world items.

Постоянный gameplay acceptance workflow выполняет `NpcLoot` tests, а отдельный source-contract workflow независимо перепроверяет official TerrariaServer evidence при изменениях loot-кода или тестов.

## Граница roadmap

D6 `loot rules` завершён как архитектурная/runtime-граница для текущего импортированного vanilla slice: rules являются typed immutable data, tables явно кодируют support и capacity, evaluation отделён от world-item mutation, а source/RNG order закреплён исполняемыми проверками. Расширение списка NPC и rule families остаётся parity-работой, а не поводом держать декомпозиционную задачу вечно открытой. Иначе чекбокс превращается в религию, а не инженерный критерий.
