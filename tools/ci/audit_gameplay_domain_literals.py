#!/usr/bin/env python3
"""High-signal guard against raw Terraria domain literals leaking into gameplay code.

The audit deliberately scans gameplay-owned runtime/core C# rather than protocol/persistence codecs,
where raw wire/file representation is legitimate. Catalog files remain the version-pinned source of
numeric identity. A rare intentional gameplay literal may be suppressed on the same source line with:

    // gameplay-domain-literal-audit: allow <rule> - <specific reason>

The suppression is intentionally noisy and reviewable; do not use it to hide ordinary content IDs.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GAMEPLAY_ROOTS = (
    REPO_ROOT / "src" / "TerraRuntime.Core",
    REPO_ROOT / "src" / "TerraRuntime",
    REPO_ROOT / "src" / "TerraRuntime.World",
)

BOUNDARY_NAME_MARKERS = (
    "Packet",
    "Protocol",
    "Projection",
    "FrameEncoder",
    "FrameDecoder",
    "Encoder",
    "Decoder",
    "Codec",
    "Wire",
    "WorldFile",
    "Snapshot",
    "Prepared",
    "Generation",
    "Format",
    "Envelope",
)

DOMAIN_ID_TYPES = (
    "ItemTypeId",
    "NpcTypeId",
    "ProjectileTypeId",
    "TileTypeId",
    "WallTypeId",
    "BuffTypeId",
    "PrefixId",
    "TileEntityTypeId",
    "NpcAiStyleId",
    "ProjectileAiStyleId",
)

NUMBER = r"(?:0[xX][0-9A-Fa-f_]+|\d[\d_]*)"
DOMAIN_TYPE = "|".join(DOMAIN_ID_TYPES)


@dataclass(frozen=True)
class Rule:
    name: str
    pattern: re.Pattern[str]
    explanation: str


RULES = (
    Rule(
        "explicit-domain-id-literal",
        re.compile(rf"\b(?:new\s+)?(?:{DOMAIN_TYPE})\s*\(\s*-?{NUMBER}\b"),
        "construct a domain ID from a named catalog or validated boundary value, not a numeric literal",
    ),
    Rule(
        "target-typed-domain-id-literal",
        re.compile(rf"\b(?:{DOMAIN_TYPE})\s+\w+\s*=\s*new\s*\(\s*-?{NUMBER}\b"),
        "target-typed domain IDs must use named/version-pinned identities outside catalogs",
    ),
    Rule(
        "raw-entity-type-decision",
        re.compile(
            rf"\b(?:npc|projectile|item|tile|wall|buff)\s*\.\s*(?:Type|Wall|AiStyle)"
            rf"(?:\s*\.\s*Value)?\s*(?:==|!=|<=|>=|<|>|is)\s*-?{NUMBER}\b",
            re.IGNORECASE,
        ),
        "gameplay decisions must compare typed/named identities or metadata, not raw type/AI-style numbers",
    ),
    Rule(
        "raw-entity-type-decision-reversed",
        re.compile(
            rf"-?{NUMBER}\b\s*(?:==|!=|<=|>=|<|>)\s*"
            rf"\b(?:npc|projectile|item|tile|wall|buff)\s*\.\s*(?:Type|AiStyle)"
            rf"(?:\s*\.\s*Value)?\b",
            re.IGNORECASE,
        ),
        "gameplay decisions must compare typed/named identities or metadata, not raw type/AI-style numbers",
    ),
    Rule(
        "raw-domain-mask",
        re.compile(
            rf"\b(?:\w*(?:flags|bits|mask)\d*|hidevisibleaccessory|hidemisc)\b"
            rf"\s*(?:&|\||\^)\s*-?{NUMBER}\b",
            re.IGNORECASE,
        ),
        "gameplay bit decisions must use named flag/mask values; raw masks belong at wire/file boundaries",
    ),
    Rule(
        "raw-frame-arithmetic",
        re.compile(rf"\b\w+\s*\.\s*Frame[XY]\s*(?:/|%)\s*-?{NUMBER}\b"),
        "frame arithmetic must come from a tile-object definition or named frame fact",
    ),
    Rule(
        "raw-player-inventory-slot-literal",
        re.compile(
            r"\b(?:"
            r"\w*(?:Inventory(?:Slot|Count|Start|End)|CoinSlot|AmmoSlot|MouseItem)\w*\s*=\s*"
            r"|(?:inventorySlot|selectedItem|slot)\s*(?:==|!=|<=|>=|<|>)\s*"
            r")(?:49|50|53|54|57|58|59|98|99|699|700|989|990)\b",
            re.IGNORECASE,
        ),
        "player inventory slot ranges must come from VanillaPlayerItemSlotCatalog",
    ),
    Rule(
        "raw-item-net-id-literal",
        re.compile(
            r"(?:\bItemNetId\s*:\s*|\b\w+\.ItemNetId\s*(?:==|!=|<=|>=|<|>)\s*)[1-9][\d_]*\b",
            re.IGNORECASE,
        ),
        "non-empty item net IDs must cross from a named item catalog or validated boundary value",
    ),
    Rule(
        "raw-prefix-none-literal",
        re.compile(
            r"(?:\b\w+\s*\.\s*Prefix(?:\s*\.\s*Value)?\s*(?:==|!=)\s*0\b|\bPrefix\s*:\s*0\b)",
            re.IGNORECASE,
        ),
        "prefix absence must use VanillaPrefixIds.None/NoneValue rather than a raw zero",
    ),
    Rule(
        "raw-moon-phase-decision",
        re.compile(
            rf"\bmoonPhase\s*(?:==|!=|<=|>=|<|>)\s*{NUMBER}\b",
            re.IGNORECASE,
        ),
        "moon phase decisions must use VanillaMoonPhase/VanillaMoonPhases rather than a raw range literal",
    ),
)

SUPPRESSION = "gameplay-domain-literal-audit: allow"


@dataclass(frozen=True)
class Finding:
    path: Path
    line: int
    rule: Rule
    source: str


def is_boundary_file(path: Path) -> bool:
    name = path.name
    return any(marker in name for marker in BOUNDARY_NAME_MARKERS)


def strip_comments_and_literals(text: str) -> str:
    out = list(text)
    length = len(text)
    i = 0

    def blank(start: int, end: int) -> None:
        for index in range(start, end):
            if out[index] not in "\r\n":
                out[index] = " "

    while i < length:
        if text.startswith("//", i):
            end = text.find("\n", i + 2)
            if end < 0:
                end = length
            blank(i, end)
            i = end
            continue
        if text.startswith("/*", i):
            end = text.find("*/", i + 2)
            end = length if end < 0 else end + 2
            blank(i, end)
            i = end
            continue

        raw_start = i
        while raw_start < length and text[raw_start] == "$":
            raw_start += 1
        quote_count = 0
        while raw_start + quote_count < length and text[raw_start + quote_count] == '"':
            quote_count += 1
        if quote_count >= 3:
            delimiter = '"' * quote_count
            end = text.find(delimiter, raw_start + quote_count)
            end = length if end < 0 else end + quote_count
            blank(i, end)
            i = end
            continue

        verbatim_prefix = None
        for prefix in ("$@\"", "@$\"", "@\""):
            if text.startswith(prefix, i):
                verbatim_prefix = prefix
                break
        if verbatim_prefix is not None:
            j = i + len(verbatim_prefix)
            while j < length:
                if text[j] == '"':
                    if j + 1 < length and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            blank(i, j)
            i = j
            continue

        string_prefix = None
        for prefix in ("$\"", "\""):
            if text.startswith(prefix, i):
                string_prefix = prefix
                break
        if string_prefix is not None:
            j = i + len(string_prefix)
            while j < length:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            blank(i, min(j, length))
            i = min(j, length)
            continue

        if text[i] == "'":
            j = i + 1
            while j < length:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == "'":
                    j += 1
                    break
                j += 1
            blank(i, min(j, length))
            i = min(j, length)
            continue
        i += 1

    return "".join(out)


def suppression_is_valid(source_line: str, rule_name: str) -> bool:
    marker = source_line.lower().find(SUPPRESSION)
    if marker < 0:
        return False
    suffix = source_line[marker + len(SUPPRESSION):].strip()
    if not suffix.startswith(rule_name):
        return False
    reason_separator = suffix.find(" - ")
    return reason_separator >= 0 and len(suffix[reason_separator + 3:].strip()) >= 8


def scan_file(path: Path) -> list[Finding]:
    text = path.read_text(encoding="utf-8")
    code = strip_comments_and_literals(text)
    original_lines = text.splitlines()
    findings: list[Finding] = []
    for rule in RULES:
        if path.name == "VanillaPlayerItemSlotCatalog.cs" and rule.name == "raw-player-inventory-slot-literal":
            continue
        for match in rule.pattern.finditer(code):
            line = code.count("\n", 0, match.start()) + 1
            source = original_lines[line - 1].strip() if line <= len(original_lines) else ""
            if suppression_is_valid(source, rule.name):
                continue
            findings.append(Finding(path, line, rule, source))
    return findings


def source_files() -> list[Path]:
    files: list[Path] = []
    for root in GAMEPLAY_ROOTS:
        if not root.is_dir():
            raise SystemExit(f"gameplay audit root is missing: {root.relative_to(REPO_ROOT)}")
        for path in root.rglob("*.cs"):
            if not is_boundary_file(path):
                files.append(path)
    return sorted(files)


def run_self_test() -> None:
    fixtures = {
        "if (npc.Type == 3) return;": "raw-entity-type-decision",
        "if (tile.Type is 10 or 388) return;": "raw-entity-type-decision",
        "if (tile.Wall != 350) return;": "raw-entity-type-decision",
        "var column = tile.FrameX / 18;": "raw-frame-arithmetic",
        "if (3 == projectile.AiStyle) return;": "raw-entity-type-decision-reversed",
        "NpcTypeId type = new(3);": "target-typed-domain-id-literal",
        "var style = new NpcAiStyleId(2);": "explicit-domain-id-literal",
        "if ((flags & 0x04) != 0) return;": "raw-domain-mask",
        "var hidden = request.HideVisibleAccessory & 0x03ff;": "raw-domain-mask",
        "var visible = request.SomeStateFlags & 7;": "raw-domain-mask",
        "var optional = request.MiscFlags1 & 0x40;": "raw-domain-mask",
        "private const int AmmoSlotStart = 54;": "raw-player-inventory-slot-literal",
        "if (inventorySlot >= 59) return;": "raw-player-inventory-slot-literal",
        "var state = new Drop(ItemNetId: 71);": "raw-item-net-id-literal",
        "if (item.Prefix.Value != 0) return;": "raw-prefix-none-literal",
        "var state = new Drop(Prefix: 0);": "raw-prefix-none-literal",
        "if (moonPhase >= 8) return;": "raw-moon-phase-decision",
    }
    for source, expected in fixtures.items():
        hits = [rule.name for rule in RULES if rule.pattern.search(strip_comments_and_literals(source))]
        if expected not in hits:
            raise SystemExit(f"audit self-test failed: {expected} did not match {source!r}; hits={hits}")

    safe = (
        "if (npc.TypeIdentity == VanillaNpcIds.Zombie) return;\n"
        "if (definition.AiStyle != VanillaNpcAiStyles.Fighter) return;\n"
        "var projectile = VanillaProjectileIds.Shuriken;\n"
        "var hidden = request.HideMisc & VanillaPlayerAppearanceNormalizer.HideMiscMask;\n"
        "var velocity = request.MovementFlags & VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag;\n"
        "if (inventorySlot >= VanillaPlayerItemSlotCatalog.InventoryEndExclusive) return;\n"
        "var item = new Drop(ItemNetId: checked((short)VanillaItemIds.DirtBlock.Value));\n"
        "if (item.Prefix != VanillaPrefixIds.None) return;\n"
        "var phase = VanillaMoonPhases.Next(VanillaMoonPhase.Full);\n"
        "// if (npc.Type == 3) this example must be ignored\n"
        "var text = \"projectile.Type == 2\";\n"
    )
    unexpected = [rule.name for rule in RULES if rule.pattern.search(strip_comments_and_literals(safe))]
    if unexpected:
        raise SystemExit(f"audit self-test failed: safe fixture matched {unexpected}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true", help="run lexical/rule fixtures only")
    args = parser.parse_args()
    run_self_test()
    if args.self_test:
        print("gameplay domain literal audit self-test: ok")
        return 0

    findings: list[Finding] = []
    files = source_files()
    for path in files:
        findings.extend(scan_file(path))

    if not findings:
        print(f"gameplay domain literal audit: ok ({len(files)} C# files scanned)")
        return 0

    print("gameplay domain literal audit failed:", file=sys.stderr)
    for finding in findings:
        relative = finding.path.relative_to(REPO_ROOT)
        print(
            f"  {relative}:{finding.line}: {finding.rule.name}: {finding.source}\n"
            f"    {finding.rule.explanation}",
            file=sys.stderr,
        )
    print(
        f"\n{len(findings)} violation(s). Move numeric Terraria identity/masks into named catalogs/flags or "
        f"use a reviewed same-line '{SUPPRESSION} <rule> - <reason>' suppression.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
