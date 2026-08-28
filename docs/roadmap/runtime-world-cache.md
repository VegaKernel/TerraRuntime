# Runtime world snapshot

TerraRuntime uses two adjacent world files with deliberately different jobs:

```text
world.wld            # Terraria-compatible checkpoint and recovery source
world.runtime-world  # TerraRuntime startup snapshot
```

A valid warm startup is driven by `.runtime-world`. The original `.wld` contents are not read or hashed on that path. TerraRuntime only reads filesystem metadata for the source `.wld` so an externally newer Terraria checkpoint invalidates the snapshot.

## Current runtime snapshot format

The runtime snapshot is intentionally disposable. There is no schema-version or migration system: TerraRuntime has no deployed `.runtime-world` state that needs compatibility preservation. If the current reader does not accept the magic/header/layout or any integrity check fails, the file is treated as invalid and rebuilt from the canonical `.wld` checkpoint.

The snapshot is self-contained for startup. It embeds the validated canonical `.wld` checkpoint needed for non-tile persistence state and stores the normalized runtime tile array separately as independently verified shards.

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
- fixed header size and normalized tile-record size;
- source `.wld` byte length;
- source `.wld` `LastWriteTimeUtc` ticks captured when the snapshot was built;
- embedded canonical checkpoint length and SHA-256;
- Terraria world format version;
- world width and height;
- tile count and tile payload length;
- shard count and shard-entry size;
- tile-payload and shard-table offsets.

The live source `.wld` SHA-256 is deliberately not recomputed at warm startup. Hashing the whole source would force a full source-file read and defeat the purpose of a self-contained runtime snapshot. SHA-256 is retained for data inside `.runtime-world`: the embedded canonical payload and every tile shard are verified before the world is published.

Each normalized `WorldTile` has a frozen 16-byte sequential memory layout. The on-disk tile record is identical to that layout, so a validated shard can be read directly into the backing `WorldTile[]` without per-tile decode/copy work. It stores tile type, wall type, frame coordinates, flags, liquid amount, tile/wall paint, shape and liquid kind. The final byte is reserved and must remain zero.

Each tile shard targets 16 MiB. Loading uses positional `RandomAccess` reads with bounded parallelism. The current default is at most four simultaneous tile-shard reads, suitable as a conservative SSD/NVMe baseline. The embedded canonical payload is read concurrently with tile shards.

## Warm-start validity and fallback

At startup TerraRuntime captures a cheap source stamp consisting of:

```text
source .wld length
source .wld LastWriteTimeUtc
```

A snapshot is used when its stored source length still matches and the live `.wld` is not newer. TerraRuntime re-stats the source after loading the snapshot to detect a concurrent external replacement while the snapshot was being read.

The original `.wld` contents are read only when the runtime snapshot is missing, stale or invalid. The fallback path reads a stable `.wld`, fully validates it through the canonical loader and atomically rebuilds `.runtime-world` only after the canonical load succeeds.

Cache corruption, a bad embedded canonical hash, a bad shard hash, invalid tile data, truncation, a newer `.wld`, a changed source length or an incompatible header/layout are all cache misses. No partially reconstructed world is published.

Snapshot writes use a temporary file, flush it to disk and atomically replace the previous snapshot. The source `.wld` is never modified as a side effect of snapshot rebuild.

## Canonical checkpoint command

The host exposes an offline compatibility-checkpoint command:

```text
TerraRuntime.Server --save-wld path/to/world.wld
```

It validates the canonical checkpoint embedded in `world.runtime-world`, writes `world.wld.tmp`, flushes it and atomically replaces `world.wld`. It then refreshes the runtime snapshot source stamp so the just-written checkpoint is not immediately treated as newer than the snapshot.

This command is currently a checkpoint restore/export operation. TerraRuntime does not yet have a complete vanilla `WorldFileWriter`, so future live mutations that exist only in runtime-owned state cannot yet be serialized into a fresh vanilla `.wld`. Adding that writer is required before `--save-wld` becomes a complete live-state export rather than an export of the canonical checkpoint represented by the snapshot.

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

Performance changes must use same-world before/after measurements. More parallel reads, additional prebuilt bootstrap/index sections or a different shard size are adopted only when measurements show a real improvement.
