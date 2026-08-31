# Условия заселения Town NPC

TerraRuntime теперь владеет source-backed проекцией TerrariaServer 1.4.5.8 для проверки возможности заселения городских NPC.

Evaluator воспроизводит vanilla-флаги кандидатов из `Main.UpdateTime_SpawnTownNPCs` и границу cadence `7200 / WorldGen.GetWorldUpdateRate()`. Он получает authoritative факты игроков, а не читает транспортные пакеты напрямую: суммарную стоимость монет, максимальное здоровье и source-pinned проверки инвентаря для Arms Dealer, Demolitionist и Dye Trader.

Состояние спасённых NPC и persistent unlock-флаги больше не выбрасываются при чтении `.wld`. Состояние Goblin Tinkerer, Wizard, Mechanic, Angler, Stylist, Tax Collector, Golfer и Tavernkeep, а также unlock-состояние Merchant, Demolitionist, Party Girl, Dye Trader, Arms Dealer, Nurse и Princess сохраняются и при разборе мира, и в одноразовом prepared-world cache.

Этот срез намеренно заканчивается на eligibility кандидатов. Физический поиск подходящего дома, room-aware priority, фактическое размещение NPC, сообщения о заселении и дневные/ночные расписания остаются отдельной authoritative runtime-задачей и здесь не заявляются как готовые.
