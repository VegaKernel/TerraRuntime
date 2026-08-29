# Runtime world snapshot

TerraRuntime uses two adjacent world files with deliberately different jobs:

```text
world.wld            # Terraria-compatible checkpoint and recovery source
world.runtime-world  # TerraRuntime startup snapshot
```

A valid warm startup is driven by `.runtime-world`. The original `.wld` contents are not read or hashed on that path. TerraRuntime only reads filesystem metadata for the source `.wld` so an externally newer Terraria checkpoint invalidates the snapshot.

## Verified implementation checklist

> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or equivalent executable proof. Partial/foundation-only work remains `[ ]`.

- [x] Self-contained warm startup from `.runtime-world` without reading source `.wld` contents.
- [x] Integrity-checked embedded canonical checkpoint, tile shards and liquid runtime queues.
- [x] Missing/stale/corrupt runtime snapshot falls back safely to canonical `.wld`.
- [x] Snapshot rebuild writes a temporary file, flushes it and atomically replaces the old snapshot.
- [x] `--save-wld` atomically exports the embedded canonical checkpoint and refreshes the runtime snapshot source stamp.
- [x] Machine-readable startup profiling plus official cold/warm startup workflow coverage.
- [ ] Complete vanilla `WorldFileWriter` capable of serializing fresh live runtime state into a new `.wld`.
- [ ] `--save-wld` exports all runtime-only live mutations rather than only the canonical checkpoint represented by the snapshot.

## Current runtime snapshot format

The runtime snapshot is intentionally disposable. There is no migration contract: if the current reader rejects magic/header/layout or any integrity check, the file is invalid and rebuilt from canonical `.wld`.

The snapshot is self-contained for startup. It embeds the validated canonical checkpoint needed for non-tile persistence state, normalized runtime tiles as independently verified shards, and runtime liquid work queues so warm startup avoids full-world liquid rediscovery.

The binary layout remains literal data, not a process diagram:

```text
128-byte fixed header
embedded canonical .wld checkpoint
shard 0 normalized WorldTile records
shard 1 normalized WorldTile records
...
shard N normalized WorldTile records
N * 48-byte shard integrity entries
64-byte LIQSTATE trailer header
active liquid entries
buffered liquid tile indices
```

Dimensional sizes are:

$$
S_{\mathrm{header}}=128\,\mathrm{B},
\qquad
S_{\mathrm{tile}}=16\,\mathrm{B},
\qquad
S_{\mathrm{shardEntry}}=48\,\mathrm{B},
\qquad
S_{\mathrm{liqHeader}}=64\,\mathrm{B}.
$$

The fixed header stores magic `TRWCACHE`, fixed header/tile-record sizes, source `.wld` byte length and `LastWriteTimeUtc`, embedded checkpoint length/SHA-256, Terraria world format version, dimensions, tile count/payload length, shard count/entry size and tile/shard-table offsets.

The live source `.wld` SHA-256 is deliberately not recomputed during warm startup because hashing it would force a complete source read. SHA-256 remains an integrity mechanism for data inside `.runtime-world`: embedded canonical payload, every tile shard and liquid runtime payload are verified before publication.

Each normalized `WorldTile` has a frozen

$$
16\,\mathrm{B}
$$

sequential memory/disk layout, allowing a validated shard to read directly into the backing `WorldTile[]` without per-tile decode/copy. It stores tile/wall types, frame coordinates, flags, liquid amount, paint, shape and liquid kind; the final byte is reserved and remains zero.

Each tile shard targets

$$
16\,\mathrm{MiB}.
$$

Loading uses positional `RandomAccess` reads with bounded parallelism. The conservative current default permits at most `$4$` simultaneous tile-shard reads. Embedded canonical payload, tile shards and liquid runtime payload can be read concurrently.

## Liquid runtime state

Tile liquid contents and pending liquid simulation work are distinct and both persist.

`WorldTile.LiquidAmount` + `WorldTile.LiquidKind` preserve actual material state. `WorldLiquidUpdateQueue` preserves active FIFO work, per-entry `delay`/`kill`, buffered/deferred work and deduplicated membership.

Snapshot persistence uses compact linear tile indices. Entry sizes are:

$$
S_{\mathrm{activeLiquid}}=12\,\mathrm{B},
\qquad
S_{\mathrm{bufferedLiquid}}=4\,\mathrm{B}.
$$

The `LIQSTATE` trailer header is

$$
64\,\mathrm{B}
$$

and records counts, entry sizes, payload length and SHA-256 for combined liquid payload.

When queues are empty, warm startup restores an empty liquid scheduler without scanning the entire map. When work is pending, only queued cells restore. Invalid index, duplicate entry, bad trailer, length mismatch or hash failure invalidates the whole snapshot and triggers canonical fallback.

## Warm-start validity and fallback

The cheap source stamp is literal metadata:

```text
source .wld length
source .wld LastWriteTimeUtc
```

A snapshot is accepted when stored source length still matches, live `.wld` is not newer and all internal integrity/layout checks pass. TerraRuntime re-stats source metadata after loading to detect concurrent external replacement.

Original `.wld` contents are read only when the runtime snapshot is missing, stale or invalid. Fallback validates a stable canonical world and rebuilds `.runtime-world` only after successful canonical load.

Cache corruption, embedded/shard/liquid hash failure, invalid queue data, truncation, newer `.wld`, changed source length or incompatible header/layout are cache misses. No partially reconstructed world is published.

Snapshot writes use a temporary file, durable flush and atomic replacement. Snapshot rebuild never modifies canonical `.wld` as a side effect.

## Canonical checkpoint command

Literal CLI:

```text
TerraRuntime.Server --save-wld path/to/world.wld
```

The command validates the canonical checkpoint embedded in `world.runtime-world`, writes `world.wld.tmp`, flushes and atomically replaces `world.wld`, then refreshes the runtime snapshot source stamp. Liquid runtime queues are preserved through this refresh.

This remains checkpoint restore/export rather than a complete serializer of all future runtime-only live state. A complete vanilla `WorldFileWriter` is still required for that.

## Startup profiling and performance gate

`startup_profile` includes source metadata/stat time, canonical file-read time, runtime-snapshot load time, canonical-loader stages on fallback, snapshot rebuild/write time, join-bootstrap preparation, `WorldReady` / `NetworkReady` wall time and allocation delta.

On a genuine warm hit:

$$
T_{\mathrm{canonicalFileRead}}=0\,\mathrm{ms}.
$$

The official-world workflow compares cold/warm startup on the same TerrariaServer 1.4.5.8 world and includes a warm run where source `.wld` contents are unreadable while filesystem metadata remains accessible. This proves the warm path is self-contained in `.runtime-world` plus cheap source metadata.

Performance changes use same-world before/after measurements. More parallel reads, additional prebuilt indexes/bootstrap data or different shard sizing are accepted only after measurable improvement.
