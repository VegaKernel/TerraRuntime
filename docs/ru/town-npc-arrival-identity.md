# Identity и уведомление о прибытии Town NPC

TerraRuntime теперь materialize-ит persistent identity, которую TerrariaServer 1.4.5.8 назначает в `NPC.NewNPC` / `GiveTownUniqueDataToNPCsThatNeedIt`, вместо создания нового жителя с пустым именем и unset variation.

Для обычных legacy/transformable профилей vanilla сначала назначает `getNewNPCName(type)`, а затем profile при выборе variant ещё раз вызывает `getNewNPCName(type)`. TerraRuntime сохраняет оба расхода `WorldGen.genRand`, а итоговым именем делает второе. У Cat, Dog и Bunny другой порядок: сначала расходуется default-pet name roll, затем `Main.rand` выбирает одну из шести variations, после чего `WorldGen.genRand` выбирает имя уже из variant-specific категории. Persisted shimmer-флаг переопределяет `townNpcVariationIndex` значением `1` только после этих roll'ов и не генерирует имя заново.

Given name генерируется по официальным значениям категорий Terraria 1.4.5.8 en-US `Town.json` и в исходном порядке. Это осознанно ограничено server-side locale, которым сейчас владеет TerraRuntime: отдельной runtime-настройки языка сервера пока нет. Само arrival-уведомление английский текст в packet не зашивает.

После успешного move-in `GivenName` и `townNpcVariationIndex` записываются в `RuntimeTownNpcStateStore`, входят в последующие `.wld` snapshots и публикуются packet 56 до home-state packet. Затем уведомление из `WorldGen.SpawnTownNPC` отправляется как packet 82 / `NetTextModule` с server author `255`, цветом `ChatColors.NPCTravel` `(50,125,255)` и вложенным localization tree `Announcement.HasArrived(Game.NPCTitle(literal given name, NPCName.*))`. Для жителей без given name, например Santa, используется напрямую локализованное имя типа NPC, как в `NPC.GetFullNetName()`.

Pinned source contract проверяет категории `getNewNPCName`, порядок RNG в профилях, порядок pet variations, shimmer override, `GetFullNetName`, финальное arrival-уведомление в `SpawnTownNPC` и server chat author semantics.
