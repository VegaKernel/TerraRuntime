#!/usr/bin/env python3
"""Publish server-player physics docs and all bilingual references in one commit."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EN_PAYLOAD = ROOT / "tools/ci/server-player-physics.en.tmp"
RU_PAYLOAD = ROOT / "tools/ci/server-player-physics.ru.tmp"
EN_GUIDE = ROOT / "docs/en/server-player-physics.md"
RU_GUIDE = ROOT / "docs/ru/server-player-physics.md"
EN_INDEX = ROOT / "docs/en/README.md"
RU_INDEX = ROOT / "docs/ru/README.md"
EN_GAMEPLAY = ROOT / "docs/en/gameplay.md"
RU_GAMEPLAY = ROOT / "docs/ru/gameplay.md"
CHECKER = ROOT / "tools/ci/check_documentation.py"
ROADMAP = ROOT / "docs/roadmap/documentation.md"

EN_INDEX_MARKER = "- [Gameplay and vanilla parity](gameplay.md) — players, items, tiles, chests, NPCs, projectiles, combat and explicit parity gaps."
EN_INDEX_LINE = "- [Server-controlled player physics](server-player-physics.md) — connection-free player lifecycle, semantic horizontal/jump intents, source-backed baseline movement/collision/liquid physics and explicit unsupported branches."
RU_INDEX_MARKER = "- [Gameplay и vanilla parity](gameplay.md) — players, items, tiles, chests, NPCs, projectiles, combat и явные parity gaps."
RU_INDEX_LINE = "- [Физика server-controlled players](server-player-physics.md) — lifecycle connection-free players, semantic horizontal/jump intents, source-backed baseline movement/collision/liquid physics и явные unsupported branches."


def insert_after(text: str, marker: str, line: str, label: str) -> str:
    if line in text:
        return text
    count = text.count(marker)
    if count != 1:
        raise SystemExit(f"{label}: expected one marker, found {count}: {marker!r}")
    return text.replace(marker, marker + "\n" + line, 1)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one old block, found {count}: {old[:120]!r}")
    return text.replace(old, new, 1)


def main() -> None:
    if not EN_PAYLOAD.is_file() or not RU_PAYLOAD.is_file():
        raise SystemExit("server-player physics payload files are missing")

    EN_GUIDE.write_text(EN_PAYLOAD.read_text(encoding="utf-8"), encoding="utf-8")
    RU_GUIDE.write_text(RU_PAYLOAD.read_text(encoding="utf-8"), encoding="utf-8")

    en_index = insert_after(EN_INDEX.read_text(encoding="utf-8"), EN_INDEX_MARKER, EN_INDEX_LINE, "EN index")
    EN_INDEX.write_text(en_index, encoding="utf-8")
    ru_index = insert_after(RU_INDEX.read_text(encoding="utf-8"), RU_INDEX_MARKER, RU_INDEX_LINE, "RU index")
    RU_INDEX.write_text(ru_index, encoding="utf-8")

    checker = CHECKER.read_text(encoding="utf-8")
    checker = insert_after(checker, '    "gameplay.md",', '    "server-player-physics.md",', "checker mirror set")
    CHECKER.write_text(checker, encoding="utf-8")

    en_gameplay = EN_GAMEPLAY.read_text(encoding="utf-8")
    en_gameplay = replace_once(
        en_gameplay,
        "| Player spawn/state/movement | partial-to-substantial | authoritative ingress/state, normalization and replication foundations exist; complete anti-cheat movement model does not |",
        "| Player spawn/state/movement | partial-to-substantial | network ingress/state/normalization/replication foundations exist; connection-free server players have a verified ordinary runtime-owned physics slice; complete network-player anti-cheat/movement parity does not |",
        "EN gameplay parity row",
    )
    en_gameplay = replace_once(
        en_gameplay,
        """## 8. Server-controlled players

Trusted hosts may create connection-free runtime-owned players through `IServerPlayerOperations`.

These actors reserve normal Terraria player slots from the generation-safe pool and accept semantic intent such as horizontal movement. The host cannot directly set final velocity/position every tick and bypass runtime physics/ownership.

This boundary is intended for server-controlled actors and integration scenarios, not for exposing mutable player internals to plugins.
""",
        """## 8. Server-controlled players

Trusted hosts may create connection-free runtime-owned players through `IServerPlayerOperations`.

These actors reserve normal Terraria player slots from the generation-safe pool and accept semantic horizontal and jump intent. The host cannot directly set final velocity/position every tick and bypass runtime physics/ownership.

The runtime now advances this actor class through a verified ordinary TerrariaServer 1.4.5.8 physics slice: baseline horizontal acceleration/slowdown, release-gated jump state, gravity/fall-speed clamp, walk-down-slope, ordinary step-up/step-down, tile/slope collision and liquid displacement scaling. This is **not** yet the movement authority for ordinary network-connected players and does not include mounts, dash/grapple/extra-jump families or the remaining liquid-specific movement branches.

See [Server-controlled player physics](server-player-physics.md) for the exact constants, tick ordering, host API, evidence and explicit out-of-scope behavior.

This boundary is intended for server-controlled actors and integration scenarios, not for exposing mutable player internals to plugins.
""",
        "EN server-player section",
    )
    EN_GAMEPLAY.write_text(en_gameplay, encoding="utf-8")

    ru_gameplay = RU_GAMEPLAY.read_text(encoding="utf-8")
    ru_gameplay = replace_once(
        ru_gameplay,
        "| Player spawn/state/movement | partial-to-substantial | есть authoritative ingress/state, normalization и replication foundations; complete anti-cheat movement model отсутствует |",
        "| Player spawn/state/movement | partial-to-substantial | есть network ingress/state/normalization/replication foundations; connection-free server players имеют verified ordinary runtime-owned physics slice; complete network-player anti-cheat/movement parity отсутствует |",
        "RU gameplay parity row",
    )
    ru_gameplay = replace_once(
        ru_gameplay,
        """## 8. Server-controlled players

Trusted hosts могут создавать connection-free runtime-owned players через `IServerPlayerOperations`.

Такие actors резервируют normal Terraria player slots из generation-safe pool и принимают semantic intent, например horizontal movement. Host не может напрямую выставлять final velocity/position каждый tick, обходя runtime physics/ownership.

Эта boundary предназначена для server-controlled actors/integration, а не для выдачи mutable player internals plugins.
""",
        """## 8. Server-controlled players

Trusted hosts могут создавать connection-free runtime-owned players через `IServerPlayerOperations`.

Такие actors резервируют normal Terraria player slots из generation-safe pool и принимают semantic horizontal/jump intent. Host не может напрямую выставлять final velocity/position каждый tick, обходя runtime physics/ownership.

Runtime теперь двигает этот класс actors через verified ordinary TerrariaServer 1.4.5.8 physics slice: baseline horizontal acceleration/slowdown, release-gated jump state, gravity/fall-speed clamp, walk-down-slope, ordinary step-up/step-down, tile/slope collision и liquid displacement scaling. Это **ещё не** movement authority для обычных network-connected players и не включает mounts, dash/grapple/extra-jump families или оставшиеся liquid-specific movement branches.

Точные constants, tick ordering, host API, evidence и явные out-of-scope behavior описаны в [Физике server-controlled players](server-player-physics.md).

Эта boundary предназначена для server-controlled actors/integration, а не для выдачи mutable player internals plugins.
""",
        "RU server-player section",
    )
    RU_GAMEPLAY.write_text(ru_gameplay, encoding="utf-8")

    roadmap = ROADMAP.read_text(encoding="utf-8")
    tree_marker = "│   ├── gameplay.md\n"
    tree_line = "│   ├── server-player-physics.md\n"
    if tree_line not in roadmap:
        count = roadmap.count(tree_marker)
        if count != 2:
            raise SystemExit(f"roadmap canonical tree: expected two gameplay markers, found {count}")
        roadmap = roadmap.replace(tree_marker, tree_marker + tree_line)

    roadmap = insert_after(
        roadmap,
        "- [x] Dedicated gameplay guide: players, inventory/items, NPCs, projectiles, combat and authoritative validation, with explicit parity status.",
        "- [x] Dedicated server-player physics guide: connection-free actor ownership, semantic host intents, source-backed baseline constants/order, collision/liquid behavior and explicit unsupported branches.",
        "roadmap coverage",
    )
    ROADMAP.write_text(roadmap, encoding="utf-8")

    EN_PAYLOAD.unlink()
    RU_PAYLOAD.unlink()
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
