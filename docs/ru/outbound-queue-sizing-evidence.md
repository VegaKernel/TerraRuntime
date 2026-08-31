# Evidence для sizing outbound queue

[English](../en/outbound-queue-sizing-evidence.md) · [Сеть и протокол](networking-protocol.md)

TerraRuntime сохраняет настроенный outbound queue envelope как структурный минимум корректности и теперь формирует process-lifetime evidence, по которому можно решать, нужен ли этому минимуму дополнительный запас.

## Входные данные

Queue telemetry сохраняет за время жизни процесса:

- настроенный максимум frames и bytes даже после disconnect соединения;
- peak queued frames и bytes;
- число rejected outbound frames;
- факты slow-client detection.

Настроенные лимиты по-прежнему берутся из structural model, зависящей от player count. Измерения не могут молча ослабить этот минимум.

## Измеренный envelope

Текущий target utilization равен 75%. Для наблюдаемого peak $P$ измеренный envelope с 25% запасом:

$$
M(P)=\left\lceil\frac{P}{0.75}\right\rceil.
$$

Frames и bytes рассчитываются независимо. Безопасная рекомендация:

$$
R_f=\max(S_f,M(P_f)),
$$

$$
R_b=\max(S_b,M(P_b)),
$$

где $S_f$ и $S_b$ являются structural floor для frames и bytes.

Sizing требует review, если любой measured envelope превышает structural floor либо если были queue rejection / slow-client события, даже при меньшем peak.

## Интерпретация

Низкий measured peak **не** является основанием уменьшать очередь ниже structural join/bootstrap bound. Он лишь показывает запас для реально наблюдавшейся нагрузки. Увеличение лимита тоже должно подтверждаться повторяемыми representative workloads, а не единственным случайным burst.

Runtime network snapshot теперь отдаёт structural envelope, utilization в basis points, measured envelope с headroom, safe recommendation и флаги `HasMeasurements` / `RequiresReview`. Evidence становится machine-readable, при этом capacity живой очереди не меняется посреди соединения.
