#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path

METHOD_PATTERN = re.compile(
    r"^[ \t]*(?:public|private|internal|protected)(?:\s+(?:static|override|virtual|sealed|new|readonly|async))*\s+[^\r\n{;=]*\b([A-Za-z_]\w*)\([^\r\n)]*\)",
    re.MULTILINE,
)


def compact(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def extract_method_at(source: str, signature_start: int) -> str:
    brace = source.find("{", signature_start)
    if brace < 0:
        raise SystemExit("Method declaration has no body")
    depth = 0
    in_string = False
    in_char = False
    escaped = False
    for index in range(brace, len(source)):
        ch = source[index]
        if escaped:
            escaped = False
            continue
        if ch == "\\" and (in_string or in_char):
            escaped = True
            continue
        if ch == '"' and not in_char:
            in_string = not in_string
            continue
        if ch == "'" and not in_string:
            in_char = not in_char
            continue
        if in_string or in_char:
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return source[signature_start:index + 1]
    raise SystemExit("Method body did not terminate")


def containing_method(source: str, position: int) -> tuple[str, str]:
    matches = [m for m in METHOD_PATTERN.finditer(source, 0, position)]
    for match in reversed(matches):
        body = extract_method_at(source, match.start())
        end = match.start() + len(body)
        if match.start() <= position < end:
            return match.group(0).strip(), body
    raise SystemExit("Could not locate method containing Micro Biomes registration")


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 Micro Biomes registration.")
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--classes", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    source = Path(args.world_gen).read_text(encoding="utf-8")
    classes = Path(args.classes).read_text(encoding="utf-8").splitlines()
    needle = '"Micro Biomes"'
    positions = [m.start() for m in re.finditer(re.escape(needle), source)]
    if len(positions) != 1:
        raise SystemExit(f"Expected one Micro Biomes registration literal, found {len(positions)}")

    signature, method = containing_method(source, positions[0])
    compact_method = compact(method)
    marker = compact_method.find(needle)
    if marker < 0:
        raise SystemExit("Registration literal disappeared after compaction")

    before = max(0, marker - 12000)
    after = min(len(compact_method), marker + 24000)
    excerpt = compact_method[before:after]

    biome_classes = []
    for line in classes:
        text = line.strip()
        if not text:
            continue
        lowered = text.lower()
        if ("biome" in lowered or "micro" in lowered) and ("generation" in lowered or "world" in lowered):
            biome_classes.append(text)

    calls = sorted(set(re.findall(r"\b(?:new\s+)?([A-Za-z_]\w*(?:Biome|Generator))\b", excerpt)))
    literals = sorted(set(re.findall(r'"([^"\\]*(?:\\.[^"\\]*)*)"', excerpt)))
    source_sha = hashlib.sha256(source.encode("utf-8")).hexdigest()
    method_sha = hashlib.sha256(method.encode("utf-8")).hexdigest()

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "decompiler=ilspycmd 11.0.0.9375",
        f"WorldGen_source_sha256={source_sha}",
        f"registration_method_signature={compact(signature)}",
        f"registration_method_sha256={method_sha}",
        f"micro_biome_candidate_types={'|'.join(calls)}",
        f"micro_biome_excerpt_string_literals={'|'.join(literals)}",
        "BEGIN_MICRO_BIOMES_EXCERPT",
        excerpt,
        "END_MICRO_BIOMES_EXCERPT",
        "BEGIN_BIOME_CLASS_CATALOG",
        *biome_classes,
        "END_BIOME_CLASS_CATALOG",
    ]

    for line in lines:
        print(line)

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
