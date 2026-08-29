#!/usr/bin/env python3
"""Validate TerraRuntime's bilingual Markdown documentation without external dependencies."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs"
EN = DOCS / "en"
RU = DOCS / "ru"

REQUIRED_MIRRORS = {
    "README.md",
    "project-guide.md",
    "architecture.md",
    "host-interfaces.md",
    "networking-protocol.md",
    "world-persistence.md",
    "gameplay.md",
    "synchronization.md",
    "operations-tui.md",
    "world-generation.md",
    "security.md",
}

# Deliberately simple: repository docs use ordinary inline Markdown links.
LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")


def markdown_files(directory: Path) -> set[str]:
    return {
        path.relative_to(directory).as_posix()
        for path in directory.rglob("*.md")
        if path.is_file()
    }


def normalize_link_target(raw: str) -> str | None:
    target = raw.strip()
    if not target:
        return None

    if target.startswith("<") and target.endswith(">"):
        target = target[1:-1].strip()

    # Drop an optional Markdown title after a whitespace-separated target.
    if " " in target and not target.startswith("#"):
        target = target.split(maxsplit=1)[0]

    lowered = target.lower()
    if (
        target.startswith("#")
        or lowered.startswith("http://")
        or lowered.startswith("https://")
        or lowered.startswith("mailto:")
        or lowered.startswith("data:")
    ):
        return None

    target = target.split("#", 1)[0].split("?", 1)[0]
    if not target:
        return None

    return unquote(target)


def validate_links(paths: list[Path]) -> list[str]:
    errors: list[str] = []

    for source in paths:
        text = source.read_text(encoding="utf-8")
        for match in LINK_RE.finditer(text):
            target = normalize_link_target(match.group(1))
            if target is None:
                continue

            candidate = (source.parent / target).resolve()
            try:
                candidate.relative_to(ROOT)
            except ValueError:
                errors.append(
                    f"{source.relative_to(ROOT)}: link escapes repository root: {target}"
                )
                continue

            if not candidate.exists():
                line = text.count("\n", 0, match.start()) + 1
                errors.append(
                    f"{source.relative_to(ROOT)}:{line}: missing relative link target: {target}"
                )

    return errors


def main() -> int:
    errors: list[str] = []

    if not EN.is_dir() or not RU.is_dir():
        print("docs/en and docs/ru must both exist", file=sys.stderr)
        return 1

    en_files = markdown_files(EN)
    ru_files = markdown_files(RU)

    missing_en = sorted(REQUIRED_MIRRORS - en_files)
    missing_ru = sorted(REQUIRED_MIRRORS - ru_files)
    if missing_en:
        errors.append("missing required EN pages: " + ", ".join(missing_en))
    if missing_ru:
        errors.append("missing required RU pages: " + ", ".join(missing_ru))

    only_en = sorted(en_files - ru_files)
    only_ru = sorted(ru_files - en_files)
    if only_en:
        errors.append("pages present only in docs/en: " + ", ".join(only_en))
    if only_ru:
        errors.append("pages present only in docs/ru: " + ", ".join(only_ru))

    markdown = sorted(DOCS.rglob("*.md"))
    for extra in (ROOT / "README.md", ROOT / "AGENTS.md"):
        if extra.is_file():
            markdown.append(extra)

    errors.extend(validate_links(markdown))

    if errors:
        print("Documentation validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(
        "Documentation validation passed: "
        f"{len(en_files)} mirrored RU/EN pages, {len(markdown)} Markdown files checked."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
