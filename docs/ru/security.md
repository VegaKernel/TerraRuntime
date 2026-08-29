# Security, trust boundaries и failure isolation

[English](../en/security.md) · [Документация](README.md) · [Networking](networking-protocol.md) · [Roadmap](../roadmap.md)

## 1. Security model

TerraRuntime считает каждый client-controlled byte, length, packet rate, identity claim и gameplay request недоверенным input.

Security — не один anti-cheat component. Это набор boundaries, которые не дают одному connection или malformed world/network input получить unbounded CPU, memory, queue growth или direct access к authoritative state.

Базовое правило:

```text
untrusted input
   -> bounded parse/accounting
   -> connection/session legality
   -> authoritative gameplay validation
   -> mutation только после acceptance
```

## 2. Security отдельно от anti-cheat

Protocol hardening и gameplay authority решают разные задачи.

Protocol/security rules могут reject:

- impossible frame lengths;
- invalid handshake;
- unsupported protocol version;
- configured rate abuse;
- connection-capacity exhaustion;
- illegal connection-state ordering;
- malformed variable-length payloads.

Gameplay authority может reject синтаксически valid action, если оно невозможно в текущем world/player state.

Нельзя угадывать gameplay rules только чтобы выглядеть «безопаснее». False-positive anti-cheat, ломающий legal vanilla behavior, является correctness bug.

## 3. Connection admission

`TerrariaConnectionAdmissionGate` ограничивает expensive connection admission до выделения полного connection/player workload.

Он применяет два независимых control:

- maximum concurrent active connections;
- admission-attempt rate.

Default admission-rate gate разрешает **512 attempts за окно 1 second**.

Каждая попытка потребляет rate budget **до** capacity admission. Это намеренно: когда server уже full, attacker не должен иметь возможность организовать unbounded accept/reject churn, обходящий rate accounting только потому, что capacity rejection происходит сразу.

Gate считает accepted, rejected, capacity-rejected и rate-rejected connections.

## 4. Admission leases

Successful admission возвращает lease. Release lease уменьшает active-connection state ровно один раз.

Lease idempotent для caller: повторный `Dispose` не делает double-release, потому что ownership atomically меняется на null.

Internal negative active-count state считается invariant violation, а не игнорируется.

## 5. Handshake и join deadlines

Default connection policy требует завершить protocol handshake за **10 seconds**.

Valid `Hello` завершает pre-handshake deadline, но не выдаёт unlimited player-slot lease. Production sink composition сообщает network watchdog узкий readiness signal. После `Hello` connection, который ещё не дошёл до runtime ready/`Playing` state, ограничен default **2 minute join-completion deadline**.

Две минуты — намеренно консервативный hard-abuse ceiling, а не Terraria gameplay cadence и не утверждение о каком-либо official vanilla timeout. Этот budget нужен, чтобы peer не мог отправить valid `Hello` и затем бесконечно удерживать admitted player slot, не завершив вход в мир.

После перехода connection в `Playing` join deadline больше не действует. Текущий default normal post-join idle timeout остаётся infinite. Deployment при необходимости может независимо настроить finite idle timeout.

Watchdog различает handshake, join и обычный idle expiration, поэтому telemetry отдельно показывает `HandshakeTimeout`, `JoinTimeout` и `IdleTimeout`.

## 6. Connection-wide rate accounting

Каждый connection имеет fixed-window rate accounting для configured frame/byte work.

Current default connection policy использует `HardAbuse` budget вместо unlimited/accounting-only mode.

Rate limits нужны как hard abuse ceiling, а не как кодирование обычных gameplay timings.

Legitimate Terraria traffic бывает bursty во время join/sections/chests. Поэтому tightening limits подтверждается реальными workloads.

## 7. Per-message rate accounting

`TerrariaMessageRateAccountant` хранит optional rate accountants по message ID.

Только explicitly configured message IDs получают message-specific budget. Остальные всё равно проходят connection-wide accounting.

Это позволяет expensive packet classes иметь более строгие controls, не делая вид, что каждый packet стоит одинаково.

Long-term security всё ещё требует broader subsystem-level budgets, когда один legal packet может породить существенную world/gameplay работу.

## 8. Framing и size bounds

Frame lengths и packet-specific declared lengths считаются hostile до validation.

Правила:

- impossible/truncated frame envelopes reject deterministically;
- message-specific size ceilings применяются до крупных allocations;
- client-declared count не выбирает unbounded memory;
- receive-buffer lifetime не переносится в gameplay state;
- parser failure закрывает/reject bounded scope вместо process crash.

Safe decoder обязан оставаться safe, даже если каждое length/count field выбрано adversarially.

## 9. Connection-state legality

Packet может быть well-formed, но illegal на текущей стадии connection.

Примеры: actions до handshake, до player identity/slot assignment или до world entry.

Runtime валидирует session state до gameplay mutation. Client-claimed player slot/identity игнорируется там, где connection ownership уже определяет authoritative identity.

## 10. Typed stop reasons

Network failure классифицируется, а не сливается в одну помойку.

Current stop reasons включают:

```text
HandshakeTimeout
JoinTimeout
IdleTimeout
InvalidHandshake
UnsupportedProtocol
ProtocolFailure
InboundIoFailure
OutboundFailure
SlowClient
RateLimited
```

Это полезно для security telemetry, потому что stalled join, rate-limited peer, malformed client и broken network adapter — разные incidents.

## 11. Bounded queues

Inbound authoritative work и outbound connection work обязаны оставаться bounded.

Клиент не может создавать:

- unlimited inbound command backlog;
- unlimited outbound frame backlog;
- unlimited section/compression work;
- unbounded logging/telemetry objects.

Если outbound client не успевает drain bounded queue, он может быть отключён как `SlowClient` вместо блокировки simulation.

## 12. Authoritative work budgets

Syntactically legal request всё равно может быть computationally expensive.

Authoritative loop поэтому использует global/per-source fairness и operation limits. Проект движется к explicit global subsystem budgets для tile work, liquids, sections и других expensive operations.

Security budgets являются **global**, если work делит один tick. Умножить полный expensive-work budget на число игроков означает превратить limit в более крупную DoS surface.

## 13. Single-writer containment

Network callbacks, timers, TUI и background workers не могут напрямую мутировать authoritative gameplay state.

Это security property так же, как architecture property: malformed input path имеет меньше мест, где может создать concurrent state corruption.

Validated commands пересекают одну controlled owner boundary до mutation.

## 14. Entity identity safety

Generation/revision-aware handles защищают runtime entities от stale slot reuse.

Stale command, указывающий старый NPC/projectile/player slot, не должен изменить другую новую entity, позже занявшую тот же numeric slot.

Content type IDs и runtime entity handles являются разными domains.

## 15. World coordinate safety

World/tile operations проверяют coordinates до index runtime arrays.

Future area operations должны также защищаться от:

- integer overflow;
- negative dimensions;
- attacker-controlled large rectangles;
- repeated expensive framing/liquid/placement work.

Bounds checking выполняется до доступа к world memory, а не после failed index operation.

## 16. Chest/object input

Chest/object traffic исторически exploit-prone, потому что смешивает identity, coordinates и inventory state.

Current chest handling содержит malformed-input containment и проверяет relevant identifiers/coordinates до authoritative operations.

Strict conservation/anti-dupe rejection должен строиться на реальном server-owned inventory model. Reject legal client behavior из guessed ownership transitions — плохая security.

## 17. Persistence safety

World corruption и data loss являются security/reliability failures.

Persistence rules:

- canonical `.wld` остаётся recovery source;
- runtime snapshot corruption fallback'ится на `.wld`;
- save publish через temporary file + flush + atomic replacement;
- unknown/newer world layouts не rewrite по guessed offsets;
- background writers используют detached snapshots, не mutable live stores;
- truncated sections не стирают unrelated valid state молча.

Attacker-caused malformed network exception не должен ломать normal final-save/shutdown path здорового world.

## 18. Runtime snapshot integrity

`.runtime-world` считается untrusted persisted input при startup, даже если когда-то был создан самим TerraRuntime.

Loader проверяет header/layout и integrity-protected embedded canonical data, tile shards и liquid payload.

Любая inconsistency является cache miss. Partially validated snapshot не публикуется.

Derived cache disposable, поэтому runtime может reject incompatible versions без risky migrations.

## 19. Host/plugin trust boundary

CoreCLR extensible host различает trusted host modules и ordinary plugins.

Trusted host modules получают `TerraRuntime.HostContracts`, а не arbitrary internal implementation objects. Они регистрируют narrow extension providers или вызывают controlled operations.

Обычные Vega plugins остаются за Vega Plugin SDK и автоматически не получают TerraRuntime trusted-module privileges.

NativeAOT standalone profile не загружает arbitrary managed plugin DLLs.

## 20. TUI/operations boundary

UI не становится trusted mutation owner только потому, что local.

Read paths используют bounded snapshots. Administrative writes проходят controlled operations/command surfaces к authoritative owner.

Terminal/UI failure деградирует в plain console вместо corruption runtime state или shutdown server.

## 21. NativeAOT dependency discipline

Dependency admission является частью security/reliability model.

Runtime reflection scanning, dynamic code generation и serializers с неясным trimming behavior не допускаются в production paths без явного контракта.

Новые production dependencies проходят Linux/Windows NativeAOT gates и exercised smoke paths там, где применимо.

Это уменьшает hidden runtime behavior и deployment-only failures в trust-boundary code.

## 22. Failure isolation

Желаемый failure scope минимален:

```text
bad frame          -> reject/close connection
rate abuse         -> reject/close connection
stalled handshake  -> close that connection
stalled join       -> close that connection
slow reader        -> close that connection
bad runtime cache  -> fall back to .wld
TUI failure        -> fall back to plain console
save write fail    -> keep previous canonical checkpoint
invalid worldgen   -> discard candidate world
```

Process не должен fail-fast на обычном hostile network input.

Invariant throws уместны для impossible internal programming states, но production trust-boundary failures должны использовать explicit bounded error paths.

## 23. Telemetry

Security-relevant observability должна различать как минимум:

- active/accepted/rejected connections;
- capacity admission rejects;
- admission-rate rejects;
- connection/message rate limits;
- connection stop reason mapping, включая handshake/join/idle timeouts;
- malformed/protocol failures;
- slow-client disconnects;
- queue depth/backlog age;
- invalid gameplay requests;
- cache validation failure reason;
- save failure counters.

Telemetry сама bounded, чтобы attacker не превратил error reporting в следующую memory/CPU attack.

## 24. Fuzzing

Permanent deterministic malformed/fuzz coverage уже существует для framing layer и typed packet decoders, включая hostile declared lengths и segmented input. Это regression floor, а не заявление, что protocol fuzzing полностью завершён.

Broader roadmap всё ещё требует fuzz/malformed corpora для:

- section/tile data;
- `.wld` parsing;
- command/text parsing;
- future complex variable-length gameplay formats.

Хороший fuzz scenario доказывает не только rejection malformed input, но и способность server принять valid connection после атаки.

## 25. Security evidence

В зависимости от change evidence включает:

- admission-gate churn/capacity tests;
- handshake/join/idle deadline tests;
- connection policy/rate tests;
- malformed frame/packet tests;
- slow-reader real-socket tests;
- runtime queue/fairness tests;
- world parser/cache corruption tests;
- persistence interruption/atomic-replacement tests;
- live official-client compatibility probes.

Для bug fix regression test должен падать при удалении fix.

## 26. Текущие ограничения

Active security work:

- broader protocol/world fuzz corpora beyond current framing and typed-decoder regression floor;
- complete global/per-subsystem expensive-work budgets;
- complete rate limits всех expensive gameplay classes;
- richer malformed/rejected packet telemetry;
- full authoritative movement/inventory/combat validation;
- production-like 24-player и 255-connection stress coverage;
- long-running soak/adversarial scenarios.

Security posture надо описывать concrete implemented bounds, а не blanket label «secure».

## 27. Checklist security change

Trust-boundary/security change не завершён, пока по необходимости attacker-controlled sizes bounded до allocation, expensive work rate/budget controlled, failure scope contained, connection/gameplay rejection categories различимы, authoritative state не мутируется off-thread, persistence recovery safe, regression evidence падает на старом behavior, а эта страница обновлена вместе с `docs/en/security.md`.
