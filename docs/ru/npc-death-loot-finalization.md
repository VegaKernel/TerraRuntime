# Финализация смерти NPC, loot и world-item

TerraRuntime разделяет урон, определение смерти, вычисление loot, материализацию world-item и репликацию на отдельные authoritative-границы. Текущий срез Blue Slime уже может завершить server-owned смерть реальным world-item state без выдуманного клиентского `ConnectionHandle`.

## Production flow

```mermaid
flowchart TD
    Dead["мёртвый активный NPC\nточный NpcHandle"] --> Validate["generation, Life == 0,\nverified definition и loot rules"]
    Validate --> Support["preflight materializer support\nдля всех потенциальных rule items"]
    Support --> Capacity["worst-case world-item reservation\nдо loot RNG"]
    Capacity --> Rule["выполнить одно loot rule"]
    Rule -->|success| Materialize["сразу materialize item\nprefix + velocity на том же RNG"]
    Materialize --> Stage["staged validated unpublished drop"]
    Stage --> Next["следующее loot rule"]
    Rule -->|random miss| Next
    Next --> Rule
    Next --> Done["все rules завершены"]
    Done --> Despawn["despawn точного поколения NPC"]
    Despawn --> Commit["commit точных world-item reservations"]
```

Критичная деталь: loot теперь выполняется **потоково**, а не сначала целиком вычисляется и только потом materialize'ится.

## Почему нужен streaming

Pinned TerrariaServer 1.4.5.8 source доказывает, что `NPC.NPCLoot_DropItems` создаёт

`DropAttemptInfo { rng = Main.rand }`.

`CommonDrop.TryDroppingItem` выполняет luck check, потребляет `info.rng.Next(...)` для stack и немедленно вызывает `CommonCode.DropItemFromNPC`. Этот путь вызывает `Item.NewItem`, а natural prefix и default velocity там также потребляют `Main.rand` **до следующего loot rule**.

Поэтому обязательный порядок таков:

$$
R_i^{loot}\;\rightarrow\;R_i^{stack}\;\rightarrow\;R_i^{prefix}\;\rightarrow\;R_i^{velocity}\;\rightarrow\;R_{i+1}^{loot}.
$$

Если сначала собрать все `NpcLootDrop`, а затем создавать items, отдельные вероятности могли бы выглядеть правильно, но deterministic RNG stream уже отличался бы от vanilla. Транзакция теперь выполняет одно правило через `VanillaNpcLootEvaluator.TryEvaluateRule`, немедленно materialize'ит успешный результат и только затем переходит к следующему правилу.

## Generation safety и capacity

Идентичность NPC задаётся как

$$
H_{npc}=(slot,generation).
$$

Начальный lookup и финальный despawn используют один и тот же exact handle. Stale generation не может финализировать нового NPC в переиспользованном слоте, а повторный вызов после успешной транзакции завершается до RNG.

Reservations в `RuntimeWorldItemStore` unpublished и generation-safe. До потребления loot RNG транзакция резервирует максимальное число item slots, которое может потребовать импортированная последовательность правил. Для Blue Slime это два слота: Gel и Slime Staff. При нехватке capacity мёртвый NPC остаётся в store, luck/random не потребляются.

Это консервативное capacity-preflight намеренно отличается от opportunistic slot selection Terraria при почти полностью занятом item pool: TerraRuntime может отложить финализацию, если свободен только один слот, хотя одно из двух вероятностных правил могло бы не сработать. Такой компромисс исключает retry-driven RNG drift и частичный loot commit, пока общий loot transaction model ещё расширяется.

## Concrete Blue Slime materializer

`VanillaNpcLootWorldItemMaterializer` сейчас поддерживает два source-backed предмета Blue Slime:

| Item | Размер | Gravity | Natural prefix |
|---|---:|---|---|
| Gel (`23`) | `10×12` | обычная | отсутствует |
| Slime Staff (`1309`) | `26×28` | обычная | summon family |

Обычный NPC loot использует целочисленный центр NPC:

$$
x_c=\operatorname{trunc}(x_{npc})+\left\lfloor\frac{w_{npc}}2\right\rfloor,
\qquad
y_c=\operatorname{trunc}(y_{npc})+\left\lfloor\frac{h_{npc}}2\right\rfloor.
$$

Левый верхний угол физического предмета равен центру минус $8\,\mathrm{px}$ по каждой оси. Тело `WorldItem` имеет размер $16\times16\,\mathrm{px}$; размеры item defaults описывают данные предмета. Ни Gel, ни Slime Staff не входят в `ItemID.Sets.ItemNoGravity`, поэтому начальная скорость задаётся как

$$
v_x=0.1R_x,\quad R_x\in[-30,30],
$$

$$
v_y=0.1R_y,\quad R_y\in[-40,-16].
$$

## Natural prefixes Slime Staff

`VanillaItemPrefixCatalog` фиксирует точную summon family из 22 элементов официального сервера. `Prefix(-1)` воспроизводится в source-порядке:

1. `Next(4) == 0` даёт prefix `0`;
2. иначе равномерно выбирается один summon prefix;
3. prefix из `ReducedNaturalChance` сохраняется только при `Next(3) == 0`, иначе результат становится prefix `0`;
4. выполняется item-specific prefix validity; невалидный выбранный prefix запускает natural-prefix loop заново.

Для Slime Staff source-backed stat-rounding guards отвергают prefix ID `55`, `89` и `91`. Их damage multipliers после округления возвращают базовые `8` damage staff, поэтому vanilla считает modifier неэффективным и выполняет reroll.

Materializer выполняет prefix selection до velocity RNG, как и `Item.NewItem`.

## Source contract

`NPC Loot Source Contract` скачивает официальный Windows TerrariaServer 1.4.5.8 и фиксирует SHA-256:

`d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e`

Executable probe проверяет регистрацию rules, `Player.RollLuck`, stack ranges, центр NPC, размеры items, gravity membership, общий `Main.rand`, немедленный `CommonDrop → Item.NewItem`, summon-prefix family, `ReducedNaturalChance` и item-specific prefix validity Slime Staff.

## Вертикальный death-срез Deerclops

Deerclops теперь имеет явный импортированный boss-death path и не проваливается в обычную финализацию неизвестного NPC. Evaluator сохраняет порядок rules TerrariaServer 1.4.5.8 для реализованных difficulty branches:

- Expert: instanced Boss Bag `5111` с существующим `54000`-tick slot lease;
- Master: relic `5110` плюс независимые `1/4` pet rolls для каждого interacting player, item `5090`;
- Classic: mask `5109`, Chester `5098`, Eyebrella `5101`, shader `5113`, Dizzy Hat `5385` и один гарантированный weapon из `5117/5118/5119/5095`;
- все сложности: Deerclops trophy `5108` по source boss-trophy rule `1/10`.

Классическая обёртка гарантированного оружия расходует три `Next(1)`: внешний шанс, выбор единственного дочернего правила и шанс правила вариантов. Четвёртый вызов выбирает оружие; фиксированный стек не требует дополнительного броска. Трофей регистрируется раньше правил босса в `ItemDropDatabase.Populate`. Взаимодействующие игроки передаются по порядку слотов.

После успешной authoritative death выставляется `VanillaWorldProgressionId.Deerclops`. `.wld` progression header patcher теперь обновляет source-backed byte `downedDeerclops`, расположенный непосредственно после `downedQueenSlime`. Regression coverage выполняет round-trip мира текущего формата и доказывает, что patch меняет ровно один byte header, не затрагивая соседние Empress, Queen Slime и town-slime/truffle unlock flags.

## Текущие ограничения

Это всё ещё NPC-specific срез Blue Slime, а не весь Terraria loot engine. Global/chained rules, world/event conditions, money/heal drops и остальные NPC остаются следующими этапами. Killer/closest-player resolution и production-реализация `Player.RollLuck` также остаются отдельными обязанностями и не должны угадываться по переиспользуемому byte player slot.

## Объявления о боссах

Существующие пути призыва через тайлы, пакет `61`, Slime Rain и Mechdusa отправляют исходное локализованное сообщение о боссе только после успешного авторитетного выделения NPC. Retinazer отправляет `LegacyMisc.48`; Spazmatism не отправляет сообщение о появлении; Mechdusa отправляет `LegacyMisc.107` один раз для основного Prime. События рассылаются играющим участникам и не повторяются как исходное состояние при подключении.

Смерть поддерживаемых боссов объявляется после добычи и прогресса, перед удалением. Первый Twin не объявляет победу, пока другой активен; последний отправляет множественное объявление `Enemies.TheTwins`. Только последний сегмент Eater отмечает победу, сохраняя ключ локализации именно этого сегмента. Moon Lord использует `Enemies.MoonLord` на авторитетном терминальном тике смерти $600\,\text{тиков}$; смерти частей, уход и устаревшие терминальные вызовы не объявляют победу. Текстовый модуль протокола `326` сохраняет вложенные ключи локализации, автора `255` и исходный цвет босса/события `(175,75,255)`. Пять независимых эталонных пакетов получены из официального сериализатора `1.4.5.8`. Остальные источники появления, объявления событий и полная совместимость представления боссов остаются открытыми.

Обычная добыча King Slime в Classic теперь имеет исходные размеры и признаки отсутствия префиксов для всех девяти ранее отсутствовавших наград, включая обязательную одежду Ninja, Solidifier и ветвь Slime Hook/Slime Gun. Принятый смертельный удар поэтому завершает обычную добычу и смерть; ранее материализатор отклонял эти обязательные предметы. Порядок правил добычи сохранён; открытие мешков и полная совместимость общих правил добычи остаются открытыми.

Общий материализатор теперь размещает физическое тело $16\times16\,\mathrm{px}$. Общая транзакция и все сохранённые пути добычи Application используют усечённые координаты NPC плюс половину текущего целочисленного хитбокса, включая размеры, заданные AI. Размеры item defaults остаются отдельными. Независимые записи оригинала проверяют три размера тела при дробной позиции NPC.

`VanillaBossRecovery1458` заменяет восстановление только для Стены плоти. Порядок оригинала: импортированная добыча, специальные эффекты `DoDeathEvents`, восстановление, затем объявление победы. Выпадает стек зелья $5\ldots15$, затем $5\ldots9$ сердец; тип зелья определяется ветвями оригинала. Первый Близнец, нетерминальный сегмент Пожирателя и оболочки Лунного лорда восстановления не дают; ядро восстанавливает предметы на принятом терминальном тике $600$ с проверкой поколения. `VanillaBossRecoveryDailyState1458` принадлежит часам мира: связывает убийства Глаза и Стены, выдаёт одну шляпу Badger и очищает оба флага. Сумерки и новый владелец мира сбрасывают временное состояние; рассвет и сохранение его не переносят.

`VanillaSeasonalItemDropFacts1458` заменяет сердца/звёзды до RNG префикса и скорости. Юбилейный выбор имеет приоритет; одновременные Halloween/Christmas случайно выбирают одну из замен. Композиция передаёт сохранённые принудительные сезонные факты мира; обычная календарная активация остаётся открытой. `VanillaBossRecoveryItemCatalog1458` содержит проверенные данные зелий и подбираемых предметов. `VanillaBossRewardItemPrefixFacts1458` задаёт точные семейства и ограничения округления шестнадцати наград оружия/аксессуаров, не расширяя применение оружия. Семь оценщиков теперь проверяют трофей до специальных правил босса; фиксированные стеки правил вариантов не вводят дополнительных бросков.

Выделение при полном пуле в оригинале использует возраст предметов, замену подбираемых предметов и аварийное объединение стеков; этот распределитель остаётся открытым. Сохранённая граница урона проверяет и освобождает точные резервации до RNG урона и изменения NPC. Консервативная граница включает шестнадцать обычных слотов, одиннадцать слотов восстановления, аренду Expert и возможную Master-награду для каждого текущего подходящего игрока. Зарезервированные/арендованные слоты учитываются; порядок выделения сохраняется. Полный или почти полный пул отклоняет удар; терминальная смерть Лунного лорда ожидает ёмкости. Синхронная доставка опирается на существующего единственного владельца; произвольные рекурсивные callbacks выделения предметов не поддержаны. Принятое восстановление не отбрасывается молча.

Независимые записи оригинала включают $1152$ прямых случаев восстановления, $2592$ поддержанных суффикса при разных сезонах/сложностях/живых размерах, $108$ полных классических цепочек импортированной добычи и восстановления без общих сезонных правил, а также шестнадцать полных таблиц семейств префиксов и округления. Проверяются порядок, стеки, префиксы, позиции, скорости и следующий результат общего RNG там, где они представлены. Суффиксы начинаются с записанного курсора после импортированной добычи и не утверждают совместимость общих правил. Pipeline проверяет терминальные границы, повторные/устаревшие смерти, пару Badger, сброс в сумерках и нехватку ёмкости. Связанные проверки явно внедряют один `VanillaUnifiedRandom1458`; общая производственная синхронизация `Main.rand` между созданием NPC, AI, добычей и снарядами остаётся открытой. Общие монеты, остальные фазы pickup/banner/bestiary, календарный владелец, полное выделение при переполнении и открытие мешков также остаются открытыми.

Дополнительно 64 независимых записи Item.NewItem проверяют обе ветви сезонных замен: сердца и звёзды, их позиции, скорости и следующий результат RNG.
