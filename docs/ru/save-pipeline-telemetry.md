# Телеметрия времени save pipeline

[Сохранение мира](world-persistence.md) · [Operations/TUI](operations-tui.md) · [Дорожная карта](../roadmap.md)

TerraRuntime публикует время работы persistence через неизменяемые snapshots operations API. Измерения выполняются на границах владения, а не через чтение изменяемых внутренних объектов сохранения из TUI или другого фонового наблюдателя.

## Измеряемые границы

Save coordinator хранит последнее и накопленное время для трёх границ:

- **snapshot capture**: время синхронного выполнения authoritative `captureSnapshot()` до передачи отделённого save image фоновому scheduler;
- **serialization**: время выполнения callback сериализатора на фоновом save worker;
- **write**: полное время транзакции `AtomicSaveFileWriter.WriteAsync`.

`write` намеренно является внешним измерением всей atomic-транзакции. В него входят создание/очистка temporary-файла, callback сериализатора, durable flush, необязательная валидация candidate, публикация и валидация backup предыдущего поколения, атомарная публикация canonical checkpoint и поддерживаемый barrier метаданных каталога.

Сейчас serializer пишет непосредственно в temporary `Stream`, поэтому `serialization` включает не только кодирование формата, но и stream writes, выполненные самим serializer. Это не чистое CPU-время сериализации.

Следовательно,

\[
T_{write} \ge T_{serialization}
\]

и эти две величины **нельзя складывать** при расчёте общей задержки сохранения.

## Владение и стоимость

Измерение snapshot выполняется на authoritative owner, потому что там же выполняется сам capture. Измерения serialization и write выполняются существующим фоновым save worker. Для времени используется монотонный `Stopwatch`; наружу через immutable operations snapshot публикуются только ограниченные скалярные значения.

В tile hot path не добавляются отдельные таймеры, тяжёлое трассирование или новые изменяемые объекты persistence.

## Интерпретация

Большое время snapshot указывает на стоимость authoritative handoff/capture. Большое время serialization указывает на canonical rewrite/encoding и stream I/O, выполняемый serializer. Большая разница между write и serialization указывает на стоимость flush, validation, backup и atomic publication.

Эти метрики являются диагностическими границами, а не независимыми суммируемыми фазами. Если в будущем кодирование будет отделено в независимый buffer от физической записи файла, можно добавить действительно отдельную фазу file I/O, но такую границу нужно реализовать, а не вычислять из вложенных таймеров.
