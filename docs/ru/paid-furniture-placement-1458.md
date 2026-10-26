# Оплаченное размещение мебели (1.4.5.8)

[English](../en/paid-furniture-placement-1458.md) · [Использование предметов](player-item-use-1458.md) · [Переход размещения](local-placement-tool-continuity-1458.md) · [Инструменты и мебель](tool-furniture-exceptions-1458.md)

## Разрешённые предметы и владельцы

Принятый компонент размещает Display Doll (498), Womannequin (1989, исходный style **2**), Weapons Rack (2699), Hat Rack (3977), базовые Wooden Table (32), Wooden Chair (34), Iron Anvil (35) и Work Bench (36). Другие material styles и семейства tile entities не разрешены одним наличием метаданных. Компонент объединён с [размещением 251 solid/292 wall и ограниченной заменой](local-placement-tool-continuity-1458.md).

Действием владеют существующие placement input, локальные часы, подготовка инвентаря и количественный бюджет. Начальный импорт физического stock и отдельные пакеты `20`/`87`/`121` не создают оплаченные единицы или авторитетную entity. Полное подтверждённое действие списывает ровно одну уже оплаченную единицу. Тот же канонический владелец метаданных мира совместно принимает изначально пустую entity, footprint и оплату до observers; вторая таблица stock, RNG-поток или pending ledger не вводятся.

Оригинальный локальный producer отправляет **20→87** для тайлов 470/475 и **20→87→79** для 471; четыре обычных предмета мебели используют `79`. Сервер применяет доказанный образ и framing до вставки пустой entity/`86`. Следующий rack-пакет `79` не выделяет вторую entity. Локальная и серверная роли RNG отделены. Captures исполняют прямые оригинальные placement producers и packet handlers, а не полный Main update или отсутствующее в Terraria частное поле оплаты.

Уже подтверждённая исходная стадия может пережить точную release-only цепочку `13` до задержанного `5` в одном owned update. Используется полный private/public beforeimage и RNG-доказательство существующего input; общий stage predicate остаётся строгим. Завершённая неоплаченная группа удаляется до correction на следующем явном release. Произвольный задержанный результат между updates не выводится.

## Канонические метаданные, порядок сохранения и restart

Live ID монотонно растут при создании/удалении. Канонический образ сохраняет исходный порядок entry slots словаря и повторно использует удалённые slots в LIFO-порядке. Сохранение не сортирует по ID и не сводится к append-only последовательности. Производственная загрузка мира перенумеровывает сохранившиеся допустимые строки с уникальными позициями в файловом порядке, как `WorldFile.LoadTileEntities`; raw decoded persisted ID сохраняют исходное представление. Изменённые live ID делают старые сжатые section metadata недействительными.

Отделённые checkpoint-снимки глубоко владеют содержимым entity и используют существующий атомарный world-file workflow. Реальные WorldRuntime load, `.wld` save/reload, section encoding и дальнейшие pose/mining используют того же владельца и новое live-пространство ID. Явный configured/ad-hoc импорт сохраняет заданные ID. Отсутствующие канонические метаданные остаются неизвестными; посторонние допустимые entity не перезаписываются молча.

Существующий decoder нормализует повторные позиции без сохранения сериализованных порядковых номеров. Точные исходные ID для duplicate-position и иных malformed imports не заявлены. Ограничения load/read/preparation — ограниченная политика authority, а не vanilla-константы или доказательство производительности.

## Доказательства и принятый checkpoint

Официальные Linux/Windows first/repeat fixtures сохраняют представленные клетки, часы, полный RNG и literal `20`/`87`/`79`/`86`. Normal grouped Fact охватывает реальные State/socket q0/q1/q3 оплату, задержанные отчёты, дальнейшие pose/mining, callback/currentness и alias guards, фактические load/section/checkpoint/reload. Два чувствительных omission-контроля дают assertion RED: обход перенумерации при загрузке и сохранение завершённой неоплаченной группы после release. Точное восстановление проходит при неизменных 54 выполненных component DLL. Отдельная source-proven ошибка dictionary entry-order/LIFO даёт old assertion RED→fixed GREEN на том же заранее скомпилированном TestDLL с заменой только World.dll. Две попытки funding cuts завершились защитными invariant exceptions; это сохранённые diagnostics, **не assertion controls**.

Общий ordinary-placement/furniture checkpoint проходит свежие Release WAE-сборки, **191 focused case**, fast **25 285/25 286** и один full **1 566 698/1 566 699**, без failures/errors и с известным liquid-skip. Фактический full XML содержит все **5 204** обнаруженных метода. Все **3 969** source/config/fixture inputs и **158** выполненных DLL неизменны. Свежая Windows NativeAOT, PE CLR directory `0/0`, пять smokes и реальные Release/Native TCP foundation checks проходят; оба host остановлены. Fingerprint: `e59b2d5086f402a8c380643690fd33849d2158c656a26b562ba8cb9076233687`. См. локальную приёмку (`.cache/merge-player-use/connected-candidate-04/acceptance.json`) и [дорожную карту](../roadmap.md); shipment отдельно фиксирует Git history.

Эти hosting/TCP gates не превращают component captures в placement-трафик официального GUI или полное vanilla-исполнение. Linux Native, широкое поведение furniture/styles, начальное владение stock персонажа и неограниченные скрытые истории не подтверждены. Private ammo и дополнительный pick-witness не входят в этот checkpoint.
