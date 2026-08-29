#!/usr/bin/env python3
"""Apply the bilingual deployment/configuration documentation as one atomic main commit."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

EN_PAYLOAD = ROOT / "tools/ci/deployment-configuration.en.tmp"
RU_PAYLOAD = ROOT / "tools/ci/deployment-configuration.ru.tmp"
EN_GUIDE = ROOT / "docs/en/deployment-configuration.md"
RU_GUIDE = ROOT / "docs/ru/deployment-configuration.md"
EN_INDEX = ROOT / "docs/en/README.md"
RU_INDEX = ROOT / "docs/ru/README.md"
CHECKER = ROOT / "tools/ci/check_documentation.py"
DOC_ROADMAP = ROOT / "docs/roadmap/documentation.md"
WORKFLOW = ROOT / ".github/workflows/documentation.yml"

EN_INDEX_LINE = (
    "- [Deployment and configuration](deployment-configuration.md) — NativeAOT/CoreCLR packaging, "
    "runtime directories, CLI configuration, trusted host-module loading and current deployment limitations."
)
RU_INDEX_LINE = (
    "- [Развёртывание и конфигурация](deployment-configuration.md) — NativeAOT/CoreCLR packaging, "
    "runtime directories, CLI configuration, trusted host-module loading и текущие deployment limitations."
)

FINAL_WORKFLOW = """name: Documentation

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

concurrency:
  group: terra-runtime-docs-${{ github.ref }}
  cancel-in-progress: true

jobs:
  validate-documentation:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v5
        with:
          fetch-depth: 0

      - name: Test documentation checker
        run: python3 tools/ci/test_check_documentation.py

      - name: Resolve documentation diff base
        id: docs-base
        shell: bash
        env:
          EVENT_NAME: ${{ github.event_name }}
          PUSH_BEFORE: ${{ github.event.before }}
          PR_BASE_SHA: ${{ github.event.pull_request.base.sha }}
        run: |
          if [ "$EVENT_NAME" = "pull_request" ]; then
            echo "sha=$PR_BASE_SHA" >> "$GITHUB_OUTPUT"
          else
            echo "sha=$PUSH_BEFORE" >> "$GITHUB_OUTPUT"
          fi

      - name: Validate bilingual documentation
        run: python3 tools/ci/check_documentation.py --changed-base "${{ steps.docs-base.outputs.sha }}"
"""


def insert_after(text: str, marker: str, line: str, label: str) -> str:
    if line in text:
        return text
    count = text.count(marker)
    if count != 1:
        raise SystemExit(f"{label}: expected one insertion marker, found {count}: {marker!r}")
    return text.replace(marker, marker + "\n" + line, 1)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one replacement marker, found {count}: {old!r}")
    return text.replace(old, new, 1)


def main() -> None:
    if not EN_PAYLOAD.is_file() or not RU_PAYLOAD.is_file():
        raise SystemExit("deployment documentation payload files are missing")

    EN_GUIDE.write_text(EN_PAYLOAD.read_text(encoding="utf-8"), encoding="utf-8")
    RU_GUIDE.write_text(RU_PAYLOAD.read_text(encoding="utf-8"), encoding="utf-8")

    en_index = EN_INDEX.read_text(encoding="utf-8")
    en_index = insert_after(
        en_index,
        "- [Operations and Terminal UI](operations-tui.md) — no-argument startup, CLI defaults, TUI lifecycle, fallback console, telemetry and dashboard extension rules.",
        EN_INDEX_LINE,
        "EN index",
    )
    # Normalize the dimensional tick-rate value while touching this index.
    en_index = en_index.replace("— 60 Hz authoritative loop,", "— $60\\,\\mathrm{Hz}$ authoritative loop,")
    EN_INDEX.write_text(en_index, encoding="utf-8")

    ru_index = RU_INDEX.read_text(encoding="utf-8")
    ru_index = insert_after(
        ru_index,
        "- [Operations и Terminal UI](operations-tui.md) — startup без аргументов, CLI defaults, lifecycle TUI, fallback console, telemetry и правила dashboard extensions.",
        RU_INDEX_LINE,
        "RU index",
    )
    ru_index = ru_index.replace("— 60 Hz authoritative loop,", "— $60\\,\\mathrm{Hz}$ authoritative loop,")
    RU_INDEX.write_text(ru_index, encoding="utf-8")

    checker = CHECKER.read_text(encoding="utf-8")
    checker = insert_after(
        checker,
        '    "operations-tui.md",',
        '    "deployment-configuration.md",',
        "documentation checker",
    )
    CHECKER.write_text(checker, encoding="utf-8")

    roadmap = DOC_ROADMAP.read_text(encoding="utf-8")
    tree_marker = "│   ├── operations-tui.md\n"
    if "│   ├── deployment-configuration.md\n" not in roadmap:
        count = roadmap.count(tree_marker)
        if count != 2:
            raise SystemExit(f"documentation roadmap: expected two canonical-tree markers, found {count}")
        roadmap = roadmap.replace(
            tree_marker,
            tree_marker + "│   ├── deployment-configuration.md\n",
        )

    roadmap = replace_once(
        roadmap,
        "The current baseline covers protocol/networking, world persistence, gameplay/parity, synchronization/interest management, performance/tick scheduling, operations/TUI, observability/logging, world generation, security and the project's testing/evidence discipline.",
        "The current baseline covers protocol/networking, world persistence, gameplay/parity, synchronization/interest management, performance/tick scheduling, operations/TUI, deployment/configuration, observability/logging, world generation, security and the project's testing/evidence discipline.",
        "documentation roadmap baseline",
    )

    deployment_coverage = (
        "- [x] Dedicated deployment/configuration guide: NativeAOT/CoreCLR packaging, runtime directories, CLI configuration, trusted host-module deployment and explicit unsupported/reserved surfaces."
    )
    roadmap = insert_after(
        roadmap,
        "- [x] Dedicated operations/TUI guide: startup modes, dashboard model, telemetry and safe administrative operations.",
        deployment_coverage,
        "documentation roadmap coverage",
    )
    DOC_ROADMAP.write_text(roadmap, encoding="utf-8")

    # The migration cleans up its own write-capable workflow and staging payloads in the same commit.
    WORKFLOW.write_text(FINAL_WORKFLOW, encoding="utf-8")
    EN_PAYLOAD.unlink()
    RU_PAYLOAD.unlink()
    Path(__file__).unlink()


if __name__ == "__main__":
    main()
