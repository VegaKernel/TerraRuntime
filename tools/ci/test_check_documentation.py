#!/usr/bin/env python3
"""Focused regression tests for tools/ci/check_documentation.py."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

CHECKER_PATH = Path(__file__).with_name("check_documentation.py")
SPEC = importlib.util.spec_from_file_location("terra_documentation_checker", CHECKER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"cannot load documentation checker from {CHECKER_PATH}")

checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)


class DiagramStyleTests(unittest.TestCase):
    def test_directory_tree_is_literal_not_process_diagram(self) -> None:
        block = """docs/
├── en/
│   ├── README.md
│   └── architecture.md
└── ru/
    └── README.md
"""
        self.assertFalse(checker.looks_like_ascii_process_diagram(block))

    def test_vertical_ascii_flow_is_process_diagram(self) -> None:
        block = """TCP clients
    |
    v
frame decoder
    |
    v
game loop
"""
        self.assertTrue(checker.looks_like_ascii_process_diagram(block))

    def test_ascii_box_is_process_diagram(self) -> None:
        block = """┌──────────────┐
│ game runtime │
└──────────────┘
"""
        self.assertTrue(checker.looks_like_ascii_process_diagram(block))

    def test_literal_marker_allows_intentional_diagram_like_text(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "literal.md"
            source.write_text(
                "<!-- docs-style: literal-text -->\n"
                "```text\nA -> B\nB -> C\nC -> D\n```\n",
                encoding="utf-8",
            )

            with patch.object(checker, "ROOT", root):
                self.assertEqual([], checker.validate_diagram_style([source]))

    def test_unmarked_ascii_flow_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "diagram.md"
            source.write_text(
                "```text\nA -> B\nB -> C\nC -> D\n```\n",
                encoding="utf-8",
            )

            with patch.object(checker, "ROOT", root):
                errors = checker.validate_diagram_style([source])

            self.assertEqual(1, len(errors))
            self.assertIn("ASCII process diagram", errors[0])


class LinkValidationTests(unittest.TestCase):
    def test_missing_relative_link_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            docs = root / "docs"
            docs.mkdir()
            source = docs / "guide.md"
            source.write_text("[missing](missing.md)\n", encoding="utf-8")

            with patch.object(checker, "ROOT", root):
                errors = checker.validate_links([source])

            self.assertEqual(1, len(errors))
            self.assertIn("missing relative link target", errors[0])

    def test_existing_relative_link_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            docs = root / "docs"
            docs.mkdir()
            source = docs / "guide.md"
            target = docs / "target.md"
            target.write_text("target\n", encoding="utf-8")
            source.write_text("[target](target.md)\n", encoding="utf-8")

            with patch.object(checker, "ROOT", root):
                self.assertEqual([], checker.validate_links([source]))

    def test_external_links_are_ignored(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "guide.md"
            source.write_text(
                "[web](https://example.com/path)\n[mail](mailto:test@example.com)\n",
                encoding="utf-8",
            )

            with patch.object(checker, "ROOT", root):
                self.assertEqual([], checker.validate_links([source]))

    def test_relative_link_may_not_escape_repository(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "repo"
            docs = root / "docs"
            docs.mkdir(parents=True)
            source = docs / "guide.md"
            source.write_text("[escape](../../outside.md)\n", encoding="utf-8")

            with patch.object(checker, "ROOT", root):
                errors = checker.validate_links([source])

            self.assertEqual(1, len(errors))
            self.assertIn("link escapes repository root", errors[0])


class LanguagePairTests(unittest.TestCase):
    def test_english_only_change_is_rejected(self) -> None:
        changed = {"docs/en/security.md"}
        with patch.object(checker, "changed_paths_since", return_value=changed):
            errors = checker.validate_changed_language_pairs("1" * 40)

        self.assertEqual(1, len(errors))
        self.assertIn("expected docs/ru/security.md", errors[0])

    def test_russian_only_change_is_rejected(self) -> None:
        changed = {"docs/ru/security.md"}
        with patch.object(checker, "changed_paths_since", return_value=changed):
            errors = checker.validate_changed_language_pairs("2" * 40)

        self.assertEqual(1, len(errors))
        self.assertIn("expected docs/en/security.md", errors[0])

    def test_paired_language_change_passes(self) -> None:
        changed = {"docs/en/security.md", "docs/ru/security.md"}
        with patch.object(checker, "changed_paths_since", return_value=changed):
            self.assertEqual([], checker.validate_changed_language_pairs("3" * 40))


if __name__ == "__main__":
    unittest.main(verbosity=2)
