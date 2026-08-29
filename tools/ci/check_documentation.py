#!/usr/bin/env python3
"""Validate TerraRuntime's bilingual Markdown documentation without external dependencies."""

from __future__ import annotations

import argparse
import re
import subprocess
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
    "performance-runtime.md",
    "operations-tui.md",
    "observability-logging.md",
    "world-generation.md",
    "security.md",
    "testing-evidence.md",
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


def is_zero_sha(value: str) -> bool:
    return bool(value) and set(value) == {"0"}


def changed_paths_since(base_sha: str) -> set[str]:
    if not base_sha or is_zero_sha(base_sha):
        return set()

    result = subprocess.run(
        ["git", "diff", "--name-only", "--diff-filter=ACMRTUXB", f"{base_sha}...HEAD"],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"git diff from {base_sha} failed with exit code {result.returncode}: "
            f"{result.stderr.strip()}"
        )

    return {line.strip() for line in result.stdout.splitlines() if line.strip()}


def validate_changed_language_pairs(base_sha: str | None) -> list[str]:
    if not base_sha or is_zero_sha(base_sha):
        return []

    changed = changed_paths_since(base_sha)
    errors: list[str] = []

    for path in sorted(changed):
        if path.startswith("docs/en/") and path.endswith(".md"):
            relative = path.removeprefix("docs/en/")
            mirror = f"docs/ru/{relative}"
            if mirror not in changed:
                errors.append(
                    f"English documentation changed without its Russian mirror in the same change set: "
                    f"{path} -> expected {mirror}"
                )
        elif path.startswith("docs/ru/") and path.endswith(".md"):
            relative = path.removeprefix("docs/ru/")
            mirror = f"docs/en/{relative}"
            if mirror not in changed:
                errors.append(
                    f"Russian documentation changed without its English mirror in the same change set: "
                    f"{path} -> expected {mirror}"
                )

    return errors


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--changed-base",
        default=None,
        help=(
            "Optional git base SHA. When supplied, every changed docs/en/*.md page must have the "
            "matching docs/ru/*.md page in the same diff and vice versa."
        ),
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
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

    try:
        errors.extend(validate_changed_language_pairs(args.changed_base))
    except RuntimeError as error:
        errors.append(str(error))

    if errors:
        print("Documentation validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    pair_note = ""
    if args.changed_base and not is_zero_sha(args.changed_base):
        pair_note = f", paired-change diff checked from {args.changed_base[:12]}"

    print(
        "Documentation validation passed: "
        f"{len(en_files)} mirrored RU/EN pages, {len(markdown)} Markdown files checked"
        f"{pair_note}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
