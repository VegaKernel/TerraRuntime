# Паритет normal-mode loot King Slime

TerraRuntime теперь владеет source-backed normal-mode NPC-specific loot King Slime через тот же generation-safe death/world-item transaction, который используется для уже импортированных NPC loot-срезов.

## Импортированный порядок правил

Для TerrariaServer 1.4.5.8 этот срез представляет следующую normal-mode последовательность:

1. King Slime Trophy, `1/10`;
2. Slimy Saddle, `1/4`;
3. King Slime Mask, `1/7`;
4. ровно один предмет из Ninja Hood/Shirt/Pants;
5. Slime Hook, `1/3`, а при неудачном roll цепочка переходит к Slime Gun;
6. Solidifier, гарантированно;
7. Slime Staff, `1/30`.

Hook rule намеренно не превращается в luck-scaled `CommonDrop`: основной шанс потребляет raw RNG stream, и только failed branch входит в Common-правило Slime Gun. Успешный world item materialize-ится до вычисления следующего rule, поэтому item-spawn RNG остаётся вплетён в loot RNG в исходном порядке.

## Безопасность transaction

`RuntimeNpcLootWorldItemTransaction` заранее проверяет поддержку всех потенциальных item identities, включая все три Ninja-варианта и обе ветки Hook/Gun, до резервирования capacity и до первого loot RNG. Transaction резервирует место максимум под семь stack и despawn-ит только точное dead NPC generation после staging всех drops.

Normal-mode King Slime больше нельзя провести мимо импортированного loot через `RuntimeNpcDeathLifecycleFinalizer`.

## Намеренная граница difficulty

Expert и Master delivery не изображаются обычными world drops. Treasure bag и Master per-player drops остаются отдельной неподдержанной работой до появления в runtime явных ownership/recipient semantics. Context-aware lifecycle fallback при этом может корректно завершать такие dead generations, не заявляя ложный loot parity.
