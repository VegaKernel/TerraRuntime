#!/usr/bin/env python3
from pathlib import Path

ROADMAP = Path("docs/roadmap.md")

REPLACEMENTS: tuple[tuple[str, str], ...] = (
    (
        """```text
TCP clients
    |
    v
Connection read loops
    |
    v
Frame decoder / protocol validation
    |
    v
Typed inbound commands
    |
    v
Authoritative game loop (single writer)
    |
    +--> World / Players / NPCs / Projectiles / Items
    |       |
    |       +--> deterministic gameplay systems
    |
    +--> outbound state/events
            |
            v
      per-client sync planner
            |
            v
      encoded packet queues
            |
            v
        socket writers
```""",
        """```mermaid
flowchart TD
    TCP[\"TCP clients\"] --> Read[\"Connection read loops\"]
    Read --> Frame[\"Frame decoder / protocol validation\"]
    Frame --> Commands[\"Typed inbound commands\"]
    Commands --> Loop[\"Authoritative game loop<br/>single writer\"]
    Loop --> State[\"World / Players / NPCs / Projectiles / Items\"]
    State --> Gameplay[\"Deterministic gameplay systems\"]
    Loop --> Events[\"Outbound state / events\"]
    Events --> Sync[\"Per-client sync planner\"]
    Sync --> Queues[\"Encoded packet queues\"]
    Queues --> Writers[\"Socket writers\"]
```""",
    ),
    (
        """```text
world.runtime.tmp
      -> complete write
      -> flush/checksums
      -> atomic replace
      -> world.runtime
```""",
        """```mermaid
flowchart LR
    Temp[\"world.runtime.tmp\"] --> Write[\"Complete write\"]
    Write --> Flush[\"Flush + integrity checks\"]
    Flush --> Replace[\"Atomic replace\"]
    Replace --> Cache[\"world.runtime-world\"]
```""",
    ),
    (
        """```text
Game runtime
    |
immutable operations snapshots + safe commands
    |-------------------|-------------------|
Terminal.Gui          plain console       future web/API
```""",
        """```mermaid
flowchart TD
    Runtime[\"Game runtime\"] --> Ops[\"Immutable operations snapshots + safe commands\"]
    Ops --> TUI[\"Terminal.Gui\"]
    Ops --> Console[\"Plain console\"]
    Ops --> Future[\"Future web / API\"]
```""",
    ),
    (
        """```text
Official Terraria client
        |
        v
TerraRuntime (.NET 11 NativeAOT)
        |
        +-- Multiplicity-backed typed handshake
        +-- player slot assignment
        +-- world metadata
        +-- section request/response
        +-- spawn
        +-- movement relay
        +-- clean disconnect
```""",
        """```mermaid
flowchart TD
    Client[\"Official Terraria client\"] --> Runtime[\"TerraRuntime<br/>.NET 11 NativeAOT\"]
    Runtime --> Handshake[\"Multiplicity-backed typed handshake\"]
    Runtime --> Slot[\"Player slot assignment\"]
    Runtime --> World[\"World metadata\"]
    Runtime --> Sections[\"Section request / response\"]
    Runtime --> Spawn[\"Spawn\"]
    Runtime --> Movement[\"Movement relay\"]
    Runtime --> Disconnect[\"Clean disconnect\"]
```""",
    ),
    (
        """- [ ] Expand dedicated bilingual subsystem guides for protocol/networking, world persistence/cache, gameplay, synchronization, operations/TUI, worldgen and security as those areas mature.
- [ ] Add documentation-link validation to CI once the bilingual tree stabilizes.
- [ ] Add a lightweight mirror/parity check for required RU/EN pages without requiring line-by-line translation equivalence.""",
        """- [x] Expand dedicated bilingual subsystem guides for protocol/networking, world persistence/cache, gameplay, synchronization, operations/TUI, worldgen and security.
- [x] Validate repository-local documentation links in CI.
- [x] Validate required RU/EN mirrored page sets in CI without requiring line-by-line translation equivalence.""",
    ),
)


def main() -> int:
    text = ROADMAP.read_text(encoding="utf-8")
    changed = False

    for old, new in REPLACEMENTS:
        count = text.count(old)
        if count == 1:
            text = text.replace(old, new)
            changed = True
        elif count == 0 and new in text:
            continue
        else:
            raise SystemExit(f"unexpected roadmap block count {count}: {old[:80]!r}")

    if changed:
        ROADMAP.write_text(text, encoding="utf-8")
        print("migrated docs/roadmap.md")
    else:
        print("docs/roadmap.md already migrated")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
