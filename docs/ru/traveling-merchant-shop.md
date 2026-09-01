# Магазин Traveling Merchant (1.4.5.8)

Этот срез закрепляет алгоритм инвентаря Traveling Merchant и wire-образ packet 72 по TerrariaServer 1.4.5.8, не изображая полную runtime-parity там, где TerraRuntime ещё не владеет необходимыми входами.

## Source-backed генератор инвентаря

`VanillaTravelingMerchantShop1458` повторяет `Chest.SetupTravelShop` и его helper-методы. Сохраняется исходный порядок RNG, включая независимые `if`, которые могут перезаписывать ранее выбранный предмет, hardmode-поиск с минимальной rarity, ослабление rarity после серии неудачных попыток вплоть до лимита 5000, обычный цикл выбора без искусственного лимита, отдельный painting pass, запрет дублей и связанные vanity/decorative наборы. Результат представляет точный 40-слотовый образ `Main.travelShop` с нулевым хвостом.

Флаги мира и прогресса вынесены в `VanillaTravelingMerchantWorldFacts1458`. Luck игрока тоже является явным входом: caller должен передать текущий luck активного игрока, выбранного ванильным `Player.GetPlayerWithHighestLuck`. Сам `Luck.RollLuck` реализован внутри примитива поверх внедрённого `Main.rand`-подобного источника, поэтому положительный и отрицательный luck потребляют RNG в исходном порядке, а не заменяются приблизительным коэффициентом вероятности.

## Packet 72

`TerrariaTravelShopCodec` реализует protocol-326 packet `72`. Payload состоит ровно из 40 little-endian signed `Int16` идентификаторов предметов: 80 байт payload и 83 байта полного Terraria frame. Отрицательные item id отклоняются на серверной границе кодирования; ноль остаётся ванильным признаком пустого слота.

Pinned source-contract проверяет `Chest.SetupTravelShop`, `Player.GetPlayerWithHighestLuck`, `Player.RollLuck`, `Luck.RollLuck`, обновление магазина при spawn Traveling Merchant, broadcast packet 72, клиентский receive packet 72 и join-time синхронизацию магазина по TerrariaServer 1.4.5.8.

## Граница runtime ownership

Текущий town-commerce resolver по-прежнему помечает живой инвентарь Traveling Merchant как явный missing fact. Это намеренно. Vanilla пересобирает магазин в момент spawn Traveling Merchant и выполняет `RollLuck` от luck самого удачливого активного игрока в этот момент. TerraRuntime пока не владеет authoritative player-luck state, поэтому подставить выдуманный `0f` и назвать это parity было бы обычным ссанием в уши.

Следующий runtime-срез теперь узкий и понятный: спроецировать authoritative player luck, вызвать этот генератор из lifecycle Traveling Merchant, хранить полученный 40-слотовый образ, публиковать packet 72 активным клиентам и при join, затем передавать сохранённый образ в `VanillaSpecialTownShopCatalog1458`. До появления этого входа core/wire слой завершён, а runtime lifecycle честно остаётся fail-closed.
