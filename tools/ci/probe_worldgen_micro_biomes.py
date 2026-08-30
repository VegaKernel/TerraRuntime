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


def method_name(signature: str) -> str:
    return signature.split("(", 1)[0].rsplit(" ", 1)[-1]


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate method signature: {signature}")
    brace = source.find("{", start + len(signature))
    if brace < 0:
        raise SystemExit(f"Method declaration has no body: {signature}")
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
                return source[start:index + 1]
    raise SystemExit(f"Method body did not terminate: {signature}")


def extract_invocations(method: str, callee: str) -> list[str]:
    result = []
    pattern = re.compile(rf"\b{re.escape(callee)}\s*\(")
    for match in pattern.finditer(method):
        open_paren = method.find("(", match.start())
        depth = 0
        brace_depth = 0
        bracket_depth = 0
        in_string = False
        in_char = False
        escaped = False
        for index in range(open_paren, len(method)):
            ch = method[index]
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
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0 and brace_depth == 0 and bracket_depth == 0:
                    result.append(method[open_paren + 1:index])
                    break
            elif ch == "{":
                brace_depth += 1
            elif ch == "}":
                brace_depth -= 1
            elif ch == "[":
                bracket_depth += 1
            elif ch == "]":
                bracket_depth -= 1
        else:
            raise SystemExit(f"Unterminated {callee}(...) invocation")
    return result


def split_top_level(arguments: str) -> list[str]:
    result = []
    start = 0
    paren = bracket = brace = 0
    in_string = False
    in_char = False
    escaped = False
    for index, ch in enumerate(arguments):
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
        if ch == "(": paren += 1
        elif ch == ")": paren -= 1
        elif ch == "[": bracket += 1
        elif ch == "]": bracket -= 1
        elif ch == "{": brace += 1
        elif ch == "}": brace -= 1
        elif ch == "," and paren == 0 and bracket == 0 and brace == 0:
            result.append(arguments[start:index].strip())
            start = index + 1
    result.append(arguments[start:].strip())
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 Micro Biomes pass implementation.")
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--classes", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    source = Path(args.world_gen).read_text(encoding="utf-8")
    signatures = [m.group(0).strip() for m in METHOD_PATTERN.finditer(source) if m.group(1) == "AddPasses"]
    if len(signatures) != 1:
        raise SystemExit(f"Expected exactly one WorldGen.AddPasses, found {len(signatures)}")
    add_passes = extract_method(source, signatures[0])
    invocations = extract_invocations(add_passes, "AddGenerationPass")
    matches = []
    for index, invocation in enumerate(invocations):
        arguments = split_top_level(invocation)
        first = arguments[0] if arguments else ""
        if "micro" in first.lower():
            matches.append((index, first, invocation))
    if len(matches) != 1:
        catalog = " | ".join(f"{i}:{split_top_level(v)[0]}" for i, v in enumerate(invocations))
        raise SystemExit(f"Expected one Micro Biomes AddGenerationPass registration, found {len(matches)}. Catalog: {catalog}")

    index, first, invocation = matches[0]
    compact_invocation = compact(invocation)
    candidate_types = sorted(set(re.findall(r"\bnew\s+([A-Za-z_][A-Za-z0-9_.<>]*(?:Biome|Generator))\b", compact_invocation)))
    member_calls = sorted(set(re.findall(r"\b([A-Za-z_]\w*)\.(?:Place|Generate|GenerateInto|PlaceAt)\s*\(", compact_invocation)))
    numeric_literals = sorted(set(re.findall(r"(?<![A-Za-z_])[-+]?\d+(?:\.\d+)?(?:[fFdDmM])?(?![A-Za-z_])", compact_invocation)))

    class_lines = Path(args.classes).read_text(encoding="utf-8").splitlines()
    biome_classes = []
    for line in class_lines:
        text = line.strip()
        lowered = text.lower()
        if text and ("biome" in lowered or "trackgenerator" in lowered) and ("gamecontent" in lowered or "generation" in lowered):
            biome_classes.append(text)

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "decompiler=ilspycmd 11.0.0.9375",
        f"WorldGen_AddPasses_sha256={hashlib.sha256(add_passes.encode('utf-8')).hexdigest()}",
        f"micro_biomes_registration_index={index}",
        f"micro_biomes_name_expression={compact(first)}",
        f"micro_biomes_registration_sha256={hashlib.sha256(invocation.encode('utf-8')).hexdigest()}",
        f"micro_biomes_candidate_types={'|'.join(candidate_types)}",
        f"micro_biomes_member_calls={'|'.join(member_calls)}",
        f"micro_biomes_numeric_literals={'|'.join(numeric_literals)}",
        "BEGIN_MICRO_BIOMES_REGISTRATION",
        compact_invocation,
        "END_MICRO_BIOMES_REGISTRATION",
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
