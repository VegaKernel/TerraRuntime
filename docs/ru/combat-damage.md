# Основа combat damage

[English](../en/combat-damage.md) · [Gameplay](gameplay.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

## 1. Назначение

В TerraRuntime появилась protocol-independent и generation-safe основа авторитетного урона по NPC. Этот срез намеренно разделяет **происхождение урона**, **детерминированный расчёт защиты NPC** и **авторитетный commit HP** от packet handlers, replication, death effects и loot.

Это фундамент для дальнейшего паритета с TerrariaServer 1.4.5.8, а не заявление, что полный vanilla-путь `StrikeNPC` уже воспроизведён.

## 2. Реализованные контракты

`DamageSource` хранит семантическое происхождение урона без packet ID и ссылок на изменяемые runtime-объекты. Текущие категории источников:

- окружение;
- предмет игрока;
- projectile игрока;
- контакт с NPC;
- projectile NPC;
- внутренний/server-owned урон.

Происхождение от игрока, NPC и projectile использует generation-safe runtime handles. Источник проверяет, что заполнены только те handles, которые имеют смысл для выбранной категории. Например, `PlayerItem` требует player handle и отвергает посторонний projectile handle, а `PlayerProjectile` требует и владельца-игрока, и точную generation projectile.

`NpcDamageRequest` содержит generation-safe цель, семантический источник, положительный base damage, неотрицательный flat armor penetration и обычный флаг critical hit. `NpcDamageResult` является immutable-записью уже committed перехода: source damage, defense, effective defense, resolved damage и Life до/после.

## 3. Авторитетный поток

```mermaid
flowchart LR
    Source["Item / projectile / NPC / environment"] --> Request["NpcDamageRequest"]
    Request --> Lookup["Generation-safe NPC lookup"]
    Lookup --> Definition["Verified VanillaNpcDefinition"]
    Definition --> Resolve["Defense + armor penetration + crit"]
    Resolve --> Store["RuntimeNpcStore HP commit"]
    Store --> Result["NpcDamageResult"]
    Result --> Future["Future hit replication / death / loot"]
```

Цель должна оставаться тем же самым живым `NpcHandle`. Повторное использование того же числового NPC slot с новой generation не делает старый damage request снова действительным.

## 4. Детерминированная математика урона

Реализованный срез начинается **после** source-specific масштабирования weapon/projectile и возможной случайной вариации урона. Пусть

- \(B\) — уже рассчитанный base/source damage;
- \(D\) — проверенная защита NPC из `VanillaNpcDefinitionCatalog`;
- \(P\) — flat armor penetration.

Эффективная защита:

\[
D_{\mathrm{eff}}=\max(D-P,0).
\]

Обычная эффективность защиты NPC в этом срезе:

\[
k_D=0.5,
\]

поэтому урон до critical hit:

\[
H=\max(B-k_DD_{\mathrm{eff}},1).
\]

Для обычного critical hit реализован множитель

\[
k_{\mathrm{crit}}=2,
\]

то есть

\[
H_{\mathrm{crit}}=2H.
\]

Итоговый целочисленный результат не может быть меньше единицы и насыщается на `Int32.MaxValue`, а не переполняется на экстремальном входе.

Это намеренно уже полного vanilla hit pipeline. Damage variation, banners, buffs/debuffs, scaling armor penetration, target damage multipliers, immunity, специальные resistances и прочие source/target modifiers здесь не угадываются и не подменяются приблизительными правилами.

## 5. Commit HP и lethal hit

`RuntimeNpcDamageExecutor` читает точную текущую generation NPC, рассчитывает урон по его проверенной definition и изменяет `Life` через `RuntimeNpcStore.TryUpdate`. Поэтому существующие revision/generation invariants остаются единственным владельцем mutation NPC state.

Для lethal hit этот срез коммитит

\[
\mathrm{Life}_{after}=0
\]

и возвращает `NpcDamageResult.Lethal = true`.

При этом NPC намеренно **не** despawn'ится немедленно, loot не запускается, kill effects не выполняются и порядок смерти не выдумывается. Это наблюдаемое gameplay-поведение, которому нужен отдельный проверенный death pipeline. Сохранение zero-life NPC до будущей точки commit не позволяет этой основе случайно закрепить неправильный порядок эффектов.

## 6. Свойства безопасности

- zero/unassigned target handles отвергаются;
- stale NPC generations отвергаются до mutation;
- некорректное или смешанное damage provenance отвергается;
- неположительный base damage и отрицательный armor penetration отвергаются;
- уже мёртвый (`Life <= 0`) NPC повторно этим executor не повреждается;
- NPC без проверенной definition/combat state отвергается вместо подстановки выдуманной защиты;
- экстремальный critical damage насыщается, а не вызывает integer overflow.

## 7. Текущие ограничения

Отдельной последующей работой остаются:

- player PvE/PvP damage и правила player defense/difficulty;
- damage variation и luck;
- knockback и применение knockback resistance;
- immunity frames/cooldowns и projectile penetration;
- buffs, debuffs, banners и специальные target modifiers;
- преобразование contact/projectile collision в hit;
- hit packet/combat-text replication;
- порядок NPC death, kill effects, loot и progression hooks;
- boss-specific damage rules и специальные immunities.

Семантическая damage model сделана так, чтобы эти системы наращивались вокруг одного авторитетного перехода, а не возвращали packet-driven mutation HP.

## 8. Проверка

Фокусные тесты закрепляют валидацию формы источника, расчёт защиты Blue Slime, применение armor penetration до critical multiplier, минимум в один damage, lethal commit в zero Life, rejection stale generation и защиту от integer overflow. Значения defense для выбранных NPC берутся из уже существующего version-pinned `VanillaNpcDefinitionCatalog`; для более широкого combat parity по-прежнему требуется differential evidence по официальному TerrariaServer 1.4.5.8 до того, как дополнительные правила можно будет считать закрытыми.
