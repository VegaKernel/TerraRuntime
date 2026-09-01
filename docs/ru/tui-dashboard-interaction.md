# Взаимодействие с TUI dashboard

[English](../en/tui-dashboard-interaction.md) · [Operations и TUI](operations-tui.md)

## Назначение

Built-in System Dashboard TerraRuntime использует interaction model, проверенную в Vega, но data/mutation boundaries остаются полностью runtime-owned.

## Layout

```mermaid
flowchart LR
    Console["Console\nвыделяемый tail логов\naccented command line"]
    subgraph Right["Правая колонка"]
        Server["Server"]
        subgraph GraphRow["Строка графиков"]
            TPS["TPS graph"]
            Network["IN / OUT graph"]
        end
        Chat["Chat\nвсё оставшееся место по высоте"]
    end
    Console --- Server
    Server --> GraphRow
    GraphRow --> Chat
```

Console занимает левую часть. Справа сверху остаётся компактная плитка Server, под ней TPS и Network располагаются рядом в одной строке, а Chat получает всё оставшееся вертикальное пространство. CPU и Memory/GC намеренно не являются overview-плитками: System Dashboard оставляет постоянное место только для операторских сигналов, которые нужны на экране всё время.

Layout намеренно асимметричный: Console получает примерно половину workspace, а строка графиков состоит из двух одинаковых компактных плиток. Фиксированная небольшая высота Server и graph row позволяет Chat автоматически расти при увеличении высоты terminal.

## Maximize и focus

Double-click по title плитки обрабатывается через title/border view, а не через content view. Выбранная плитка растягивается на весь dashboard workspace и скрывает остальные. Повторный double-click по title восстанавливает tiled layout.

Keyboard или mouse focus включает Accent scheme и добавляет к активному title префикс `▶`, поэтому focus остаётся заметным даже в terminal, который урезает настроенную палитру.

## Графики

TPS использует настоящий Terminal.Gui `GraphView`:

- bounded history текущего TPS;
- reference line целевого TPS;
- scale, привязанный к настроенному target TPS.

CPU history на overview-графике не отображается.

Network имеет отдельный `GraphView` с раздельными histories входящих и исходящих packet rate. Компактный legend показывает текущие packet rates и throughput. Rate вычисляется по разнице subsystem-owned process-lifetime counters входящих/исходящих Terraria messages между двумя последовательными detached network snapshots. Интервал берётся из snapshot capture timestamps, поэтому график показывает traffic за фактический UI sampling interval, а не ошибочно делит lifetime totals на длину telemetry rolling window. При откате counters или некорректном/слишком длинном sampling interval локальный rate sample сбрасывается вместо искусственного spike.

Histories и предыдущий counter sample являются bounded presentation state самого UI. Они не становятся authoritative telemetry и не добавляют новые counters в packet hot paths.

## Строка команд Console

Внизу плитки Console находится отдельная bordered command area с Accent scheme. Поле ввода визуально отделено от log output и больше не выглядит как ещё одна строка лога. `Ctrl+P` переводит focus на него.

Текущие runtime-owned команды:

```text
help
save
interest on
interest off
system
players
npcs
projectiles
items
network
world
logs
```

`save` и `interest on|off` делегируются в существующий bounded operations ingress. Navigation commands только переключают текущий workspace screen. Неизвестный input показывается локально и никогда не трактуется как arbitrary runtime mutation.

## Tail логов и чата

Console и Chat показывают bounded tail detached snapshots. Overview форматирует не более 64 последних записей на поверхность, а underlying runtime log/chat stores ограничены независимо. Обе поверхности автоматически следуют за новыми сообщениями, пока оператор уже находится внизу. Ручная прокрутка истории вверх отключает принудительный follow-tail до возвращения пользователя к концу.

## Выделение текста

Console, Server и Chat используют read-only selectable text surfaces. Поддерживаются selection мышью/клавиатурой и `Ctrl+C`. Snapshot refresh не заменяет отображаемый текст при активном непустом selection, поэтому обычное обновление telemetry не уничтожает выделение в момент копирования.

Все встроенные Details screens (Players, NPCs, Projectiles, Items, Network, World и Logs) используют ту же read-only selectable проекцию. Существующий bounded `rows[]` render model остаётся внутренним форматом для formatting и smoke assertions, а оператору эти строки показывает один scrollable `TextView`. Automatic refresh сохраняет активное selection; явный переход на другой Details screen сначала сбрасывает selection предыдущего экрана и только потом показывает новые данные.

## Отзывчивость

Authoritative operations capture остаётся вне Terminal.Gui thread. UI читает последний atomically published cache snapshot, поэтому input processing, selection, menu navigation и interaction с окнами не ждут world/network snapshot acquisition.

Runtime snapshots имеют целевой период примерно

$$
T_{\mathrm{snapshot}}\approx500\,\mathrm{ms},
$$

а lightweight UI publication check выполняется примерно каждые

$$
T_{\mathrm{ui\ pump}}\approx25\,\mathrm{ms}.
$$

Эти периоды определяют свежесть данных, а не latency обработки keyboard/mouse input.
