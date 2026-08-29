#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate exact signature: {signature}")
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
        elif ch == "\\" and (in_string or in_char):
            escaped = True
        elif ch == '"' and not in_char:
            in_string = not in_string
        elif ch == "'" and not in_string:
            in_char = not in_char
        elif not in_string and not in_char:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return source[start:index + 1]
    raise SystemExit(f"Method body did not terminate: {signature}")


def method_signatures(source: str, names: set[str]) -> list[str]:
    names_pattern = "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True))
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)(?:\s+(?:static|override|virtual|sealed|new))*\s+[^\r\n{{;]*\b(?:{names_pattern})\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def constructor_signatures(source: str, type_name: str) -> list[str]:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)\s+{re.escape(type_name)}\([^\r\n)]*\)(?:\s*:\s*(?:base|this)\([^\r\n)]*\))?",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def emit_bodies(label: str, source: str, signatures: list[str], lines: list[str]) -> None:
    lines.append(f"{label}_count={len(signatures)}")
    print(f"{label}_count={len(signatures)}")
    for index, signature in enumerate(signatures):
        body = extract_method(source, signature)
        fingerprint = hashlib.sha256(body.encode("utf-8")).hexdigest()
        prefix = f"{label}_{index:02d}"
        lines.append(f"{prefix}_signature={compact(signature)}")
        lines.append(f"{prefix}_sha256={fingerprint}")
        print(f"{prefix}_signature={compact(signature)}")
        print(f"{prefix}_sha256={fingerprint}")
        print(f"BEGIN_{prefix}")
        print(compact(body))
        print(f"END_{prefix}")


def source_fingerprint(label: str, source: str, lines: list[str]) -> None:
    value = hashlib.sha256(source.encode("utf-8")).hexdigest()
    lines.append(f"{label}_source_sha256={value}")
    print(f"{label}_source_sha256={value}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 worldgen RNG plumbing.")
    parser.add_argument("--unified-random", required=True)
    parser.add_argument("--gen-base", required=True)
    parser.add_argument("--world-generator", required=True)
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    unified = Path(args.unified_random).read_text(encoding="utf-8")
    gen_base = Path(args.gen_base).read_text(encoding="utf-8")
    generator = Path(args.world_generator).read_text(encoding="utf-8")
    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    lines = ["source=TerrariaServer 1.4.5.8"]

    source_fingerprint("UnifiedRandom", unified, lines)
    source_fingerprint("GenBase", gen_base, lines)
    source_fingerprint("WorldGenerator", generator, lines)

    unified_constructors = constructor_signatures(unified, "UnifiedRandom")
    if not unified_constructors:
        raise SystemExit("Pinned UnifiedRandom exposes no constructors.")
    emit_bodies("UnifiedRandom_ctor", unified, unified_constructors, lines)

    unified_methods = method_signatures(
        unified,
        {"SetSeed", "InternalSample", "GetSampleForLargeRange", "Sample", "Next", "NextDouble", "NextBytes"},
    )
    expected_helpers = {"SetSeed", "InternalSample", "GetSampleForLargeRange", "Sample", "Next", "NextDouble"}
    found_helpers = {
        signature.split("(", 1)[0].rsplit(" ", 1)[-1]
        for signature in unified_methods
    }
    missing_helpers = sorted(expected_helpers - found_helpers)
    if missing_helpers:
        raise SystemExit(f"Pinned UnifiedRandom is missing required RNG helpers: {missing_helpers}")
    emit_bodies("UnifiedRandom_method", unified, unified_methods, lines)

    random_fields = [
        compact(match.group(0))
        for match in re.finditer(r"^[^\r\n;]*\b_random\b[^\r\n;]*;", gen_base, re.MULTILINE)
    ]
    if random_fields != ["protected static UnifiedRandom _random => WorldGen.genRand;"]:
        raise SystemExit(f"Pinned GenBase._random ownership changed: {random_fields}")
    lines.append("GenBase_random=protected static UnifiedRandom _random => WorldGen.genRand;")
    print("GenBase_random=protected static UnifiedRandom _random => WorldGen.genRand;")

    generator_constructors = constructor_signatures(generator, "WorldGenerator")
    if not generator_constructors:
        raise SystemExit("Pinned WorldGenerator exposes no constructors.")
    emit_bodies("WorldGenerator_ctor", generator, generator_constructors, lines)

    generator_methods = method_signatures(generator, {"GenerateWorld", "RunPass"})
    if not any("GenerateWorld(" in signature for signature in generator_methods):
        raise SystemExit("Pinned WorldGenerator exposes no GenerateWorld method.")
    emit_bodies("WorldGenerator_method", generator, generator_methods, lines)

    reset_signatures = method_signatures(world_gen, {"Reset"})
    if len(reset_signatures) != 1:
        raise SystemExit(f"Pinned WorldGen must expose exactly one Reset method; found {len(reset_signatures)}.")
    reset_body = extract_method(world_gen, reset_signatures[0])
    seed_assignments = [
        compact(match.group(0))
        for match in re.finditer(r"Main\.rand\s*=\s*new\s+UnifiedRandom\s*\([^;]+;", reset_body)
    ]
    if seed_assignments != ["Main.rand = new UnifiedRandom(seed);"]:
        raise SystemExit(f"Pinned WorldGen.Reset RNG seed ownership changed: {seed_assignments}")
    lines.append("WorldGen_Reset_rng_seed=Main.rand = new UnifiedRandom(seed);")
    print("WorldGen_Reset_rng_seed=Main.rand = new UnifiedRandom(seed);")

    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
