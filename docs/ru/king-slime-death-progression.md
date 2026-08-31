# Прогрессия после смерти King Slime

TerraRuntime завершает смерть King Slime внутри авторитетного NPC state pipeline, а не пытается угадать смерть босса по клиентскому пакету или позднему сканированию переиспользуемых слотов.

## Зафиксированный source order

TerrariaServer 1.4.5.8 выполняет death-case King Slime в наблюдаемом порядке:

1. если активен Slime Rain, `StopSlimeRain` вызывает `Next(3024, 6048)` и сохраняет отрицательный cooldown `-roll * 100`;
2. при первом убийстве устанавливается `unlockedSlimeBlueSpawn`;
3. Nerdy Slime (`NPC 670`) создаётся в `(int)KingSlime.Center.X - 10, (int)KingSlime.Center.Y`;
4. только после `NewNPC` вызывается `NextFloatDirection()`, X-скорость становится `value * 3`, Y-скорость — `-10`;
5. последним отмечается `downedSlimeKing`.

`RuntimeNpcAiStateExecutor` предоставляет узкую post-commit границу `INpcAiStatePostCommitEffect`. Эти эффекты выполняются только после успешной generation-safe фиксации `TimeLeft = 0` для точного поколения мёртвого NPC. Поэтому stale/reused slot не расходует death RNG и не может породить лишний Nerdy Slime. Изменение скорости созданного Nerdy также выполняется через generation-safe mutation sink.

## Привязка к миру и persistence

`RuntimeWorldProgressionRegistry` использует конкретный `WorldTileStore` как слабый ключ. Journal хранит milestone King Slime и новый blue-town-slime unlock, отдельно учитывая persisted baseline загруженного мира. Если unlock уже был в `.wld`, повторный Nerdy не создаётся и это состояние не выдаётся за новую save mutation.

`WorldFileProgressionHeaderPatcher` сохраняет оба флага: `downedSlimeKing` и `UnlockedSlimeBlueSpawn`. До blue-slime flag он проходит настоящий layout Terraria 1.4.5.8, включая variable-length список Angler, массивы BannerSystem, party NPC entries и TreeTops. Патчер меняет только принадлежащие runtime boolean-поля и fail-closed отклоняет неподдерживаемые milestone bits.

## Оставшаяся граница

Этим закрыт source-backed срез **terminal transition, остановка Slime Rain, first-kill Nerdy unlock/spawn и persistence** King Slime. `FullVanillaAiParity` остаётся false. Для Expert/Master ещё требуется подключить уже реализованный per-player loot finalizer к live combat/death ingress и тикать instanced item-slot leases в авторитетной item phase. Presentation-only death effects и общие boss announcements остаются отдельными задачами.
