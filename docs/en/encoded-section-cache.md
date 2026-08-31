# Encoded section cache contract

TerraRuntime caches the already encoded Terraria packet-10 representation of world sections. The cache is a derived network optimization only; `WorldTileStore` remains the authoritative tile state.

## Revision-based invalidation

Every network section has an even committed revision token. `WorldTileStore.Set` advances that token around the tile write and then dirties both independent consumers: networking and persistence. Cached frames carry the revision they were encoded from and are never returned when the live revision differs.

A tile mutation does not acquire the cache lock. Revision mismatch is the immediate invalidation boundary; stale frame memory is reclaimed lazily on the next cache lookup or publication. This keeps tile mutation deterministic and cheap while preventing stale bytes from being delivered.

Pinned bootstrap sections obey the same revision contract as dynamic sections. They are pinned only against LRU eviction, not against invalidation.

## Construction and delivery

Base spawn sections are encoded once when `PlayerBootstrapPacketSet` is constructed. Other sections are rebuilt from immutable section snapshots on the bounded section-cache worker pipeline. A worker result is published only if its captured revision still equals the committed live revision.

The per-player streaming tracker is updated only after a packet-10 frame has successfully entered the outbound queue. Encoding failure, stale worker output, rate limiting, or outbound backpressure therefore cannot create a false "already delivered" section.

## Initial load and generation

Initial world construction uses `SetInitialPopulationTile`, which bypasses section revisions and both dirty trackers while the world is unpublished. Initial load/generation therefore does not manufacture a full-world rebuild/save backlog. Once the world becomes authoritative, mutations must use the normal tracked mutation API.

## Memory bound and observability

Base bootstrap entries are pinned because every joining player needs them. Non-bootstrap frames use a deterministic LRU with a default 64 MiB byte budget. The total observable ceiling is the dynamic budget plus `base section count * ushort.MaxValue`, matching the maximum possible Terraria frame size per pinned packet.

`SectionPacketCacheSnapshot` exposes entries, current bytes, maximum bytes, dynamic bytes/budget, hits, misses, stale reads, waits, wait completions/failures/timeouts, evictions, and physical invalidations. Counters are aggregate values rather than per-section labels, so observability itself does not become an unbounded memory problem.

## Verification

The `EncodedSectionCacheContractTests` gate proves revision invalidation for dynamic and pinned entries, dual dirty tracking for live mutations, clean initial population, delivery marking only after explicit success, and bounded-memory telemetry. `SectionCacheMemoryBudgetTests`, `PlayerSectionStreamingStateTests`, and `SectionCachePriorityTelemetryTests` remain complementary regression coverage.
