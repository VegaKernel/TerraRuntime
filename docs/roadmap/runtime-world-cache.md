# Runtime world snapshot

TerraRuntime uses two adjacent world files with deliberately different jobs:

```text
world.wld            # Terraria-compatible checkpoint and recovery source
world.runtime-world  # TerraRuntime startup snapshot
```

A valid warm startup is driven by `.runtime-world`. The original `.wld` contents are not read or hashed on that path. TerraRuntime only reads filesystem metadata for the source `.wld` so an externally replaced or newer Terraria checkpoint invalidates the snapshot.

## Schema v3

Schema v3 is self-contained for startup. It embeds the validated canonical `.wld` checkpoint needed for non-tile persistence state and stores the normalized runtime tile array separately as independently verified shards. This lets the expensive tile reconstruction use bounded parallel positional I/O while the embedded canonical payload is read and verified independently.

Layout:

```text
128-byte fixed header
embedded canonical .wld checkpoint
shard 0 normalized WorldTile records
shard 1 normalized WorldTile records
...
shard N normalized WorldTile records
N * 48-byte shard integrity entries
```

The fixed header stores:

- magic `TRWCACHE`;
- runtime snapshot schema version;
- source `.wld` byte length;
- source `.wld` `LastWriteTimeUtc` ticks captured when the snapshot was built;
- embedded canonical checkpoint length and SHA-256;
- Terraria world format version;
- world width and height;
- normalized tile-record size and tile count;
- tile payload length;
- shard count and shard-entry size;
- tile-payload and shard-table offsets.

The live source `.wld` SHA-256 is deliberately not recomputed at warm startup. Hashing the whole source would force a full source-file read and defeat the purpose of a self-contained runtime snapshot. SHA-256 is retained for data inside `.runtime-world`: the embedded canonical payload and every tile shard are verified before the world is published.

Each normalized tile record is 16 bytes and explicitly stores tile type, wall type, frame coordinates, flags, liquid amount, tile/wall paint, shape and liquid kind. Liquid state therefore already participates in parallel tile loading.

Each tile shard targets 16 MiB. Loading uses positional `RandomAccess` reads with bounded parallelism. The current default is at most four simultaneous tile-shard reads, suitable as a conservative SSD/NVMe baseline. The embedded canonical payload is read concurrently with tile reconstruction.

## Warm-start validity and fallback

At startup TerraRuntime captures a cheap source stamp consisting of:

```text
source .wld length
source .wld LastWriteTimeUtc
```

A snapshot is used when its stored source length still matches and the live `.wld` is not newer. TerraRuntime re-stats the source after loading the snapshot to detect a concurrent external replacement while the cache was being read.

The original `.wld` contents are read only when the runtime snapshot is missing, stale or invalid. The fallback path reads a stable `.wld`, fully validates it through the canonical loader and atomically rebuilds `.runtime-world` only after the canonical load succeeds.

Cache corruption, unsupported schema, a bad embedded canonical hash, a bad shard hash, invalid tile data, truncation, a newer `.wld` or a changed source length are all cache misses. No partially reconstructed world is published.

Snapshot writes use a temporary file, flush it to disk and atomically replace the previous snapshot. The source `.wld` is never modified as a side effect of cache rebuild.

## Canonical checkpoint command

The host exposes an offline compatibility-checkpoint command:

```text
TerraRuntime.Server --save-wld path/to/world.wld
```

It validates the canonical checkpoint embedded in `world.runtime-world`, writes `world.wld.tmp`, flushes it and atomically replaces `world.wld`. It then refreshes the runtime snapshot source stamp so the just-written checkpoint is not immediately treated as newer than the snapshot.

This command is currently a checkpoint restore/export operation. TerraRuntime does not yet have a complete vanilla `WorldFileWriter`, so future live mutations that exist only in runtime-owned state cannot yet be serialized into a fresh vanilla `.wld`. Adding that writer is the required step before `--save-wld` can become a complete live-state export rather than an export of the canonical checkpoint represented by the snapshot.

## Startup profiling and performance gate

The server emits a machine-readable `startup_profile` line with:

- source metadata/stat time;
- original `.wld` file-read time;
- runtime snapshot load time;
- canonical loader total and stage timings on fallback;
- runtime snapshot rebuild/write time;
- join-bootstrap preparation time;
- `WorldReady` and `NetworkReady` wall time;
- startup allocation delta.

On a genuine warm snapshot hit `file_read_ms` must remain `0.000` because the original `.wld` contents were not read.

The official-world workflow runs cold and warm startup against the same generated Terraria 1.4.5.8 world, reports the two profiles and publishes a timing artifact. Its warm run removes read permission from the original `.wld`, proving that startup succeeds from `.runtime-world` plus source filesystem metadata rather than silently touching the source contents.

Performance decisions after schema v3 must use these before/after measurements. More parallel reads, additional prebuilt bootstrap/index sections or a different shard size should be adopted only when the same-world profile demonstrates a real improvement rather than because more threads look impressive in a diagram.
