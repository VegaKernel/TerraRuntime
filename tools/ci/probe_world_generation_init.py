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


def named_signatures(source: str, names: set[str]) -> list[str]:
    name_pattern = "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True))
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)(?: static)? [^\r\n{{;]*\b(?:{name_pattern})\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def method_name(signature: str) -> str:
    return signature.split("(", 1)[0].rsplit(" ", 1)[-1]


def emit(label: str, source: str, names: set[str]) -> set[str]:
    found = named_signatures(source, names)
    emitted: set[str] = set()
    for signature in found:
        name = method_name(signature)
        print(f"{label}_signature={compact(signature)}")
        print(f"BEGIN_{label}_{name}")
        print(compact(extract_method(source, signature)))
        print(f"END_{label}_{name}")
        emitted.add(name)
    return emitted


def split_statements(method: str) -> list[str]:
    statements: list[str] = []
    start = 0
    in_string = False
    in_char = False
    escaped = False
    paren_depth = 0
    bracket_depth = 0

    for index, ch in enumerate(method):
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
            paren_depth += 1
        elif ch == ")":
            paren_depth = max(0, paren_depth - 1)
        elif ch == "[":
            bracket_depth += 1
        elif ch == "]":
            bracket_depth = max(0, bracket_depth - 1)
        elif ch == ";" and paren_depth == 0 and bracket_depth == 0:
            statement = compact(method[start:index + 1])
            if statement:
                statements.append(statement)
            start = index + 1

    tail = compact(method[start:])
    if tail:
        statements.append(tail)
    return statements


def decode_csharp_string(value: str) -> str:
    return bytes(value, "utf-8").decode("unicode_escape") if "\\" in value else value


def inspect_add_passes(source: str) -> dict[str, object]:
    signatures = named_signatures(source, {"AddPasses"})
    if len(signatures) != 1:
        raise SystemExit(
            "Pinned Terraria.WorldGen must expose exactly one AddPasses method; "
            f"found {len(signatures)}."
        )

    signature = signatures[0]
    method = extract_method(source, signature)
    fingerprint = hashlib.sha256(method.encode("utf-8")).hexdigest()
    statements = split_statements(method)
    generator_statements = [statement for statement in statements if "_generator" in statement]
    constructor_types = list(dict.fromkeys(re.findall(r"\bnew\s+([A-Za-z_][\w.<>]*)\s*\(", method)))
    string_literals = [
        decode_csharp_string(value)
        for value in re.findall(r'"((?:\\.|[^"\\])*)"', method)
    ]

    print(f"WorldGen_AddPasses_signature={compact(signature)}")
    print(f"WorldGen_AddPasses_sha256={fingerprint}")
    print(f"WorldGen_AddPasses_generator_statement_count={len(generator_statements)}")
    for index, statement in enumerate(generator_statements):
        print(f"WorldGen_AddPasses_generator_statement_{index:03d}={statement}")
    print(f"WorldGen_AddPasses_constructor_types={constructor_types}")
    print(f"WorldGen_AddPasses_string_literal_count={len(string_literals)}")
    for index, value in enumerate(string_literals):
        print(f"WorldGen_AddPasses_string_{index:03d}={value}")

    if not generator_statements and not constructor_types:
        raise SystemExit(
            "Pinned Terraria.WorldGen.AddPasses exposed neither _generator statements nor constructor calls; "
            "the source registration shape changed beyond this probe's discovery contract."
        )

    return {
        "signature": compact(signature),
        "fingerprint": fingerprint,
        "generator_statements": generator_statements,
        "constructor_types": constructor_types,
        "string_literals": string_literals,
    }


def write_pass_catalog(path: Path, evidence: dict[str, object]) -> None:
    signature = str(evidence["signature"])
    fingerprint = str(evidence["fingerprint"])
    generator_statements = list(evidence["generator_statements"])
    constructor_types = list(evidence["constructor_types"])
    string_literals = list(evidence["string_literals"])

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "decompiler=ilspycmd 11.0.0.9375",
        "catalog_state=registration-api-discovery",
        f"WorldGen_AddPasses_signature={signature}",
        f"WorldGen_AddPasses_sha256={fingerprint}",
        f"WorldGen_AddPasses_generator_statement_count={len(generator_statements)}",
    ]
    lines.extend(
        f"WorldGen_AddPasses_generator_statement_{index:03d}={statement}"
        for index, statement in enumerate(generator_statements)
    )
    lines.append(f"WorldGen_AddPasses_constructor_types={constructor_types}")
    lines.append(f"WorldGen_AddPasses_string_literal_count={len(string_literals)}")
    lines.extend(
        f"WorldGen_AddPasses_string_{index:03d}={value}"
        for index, value in enumerate(string_literals)
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 fresh-world initialization and finalization."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--main", required=True)
    parser.add_argument("--pass-catalog-output")
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    main_source = Path(args.main).read_text(encoding="utf-8")

    world_gen_names = {
        "GenerateWorld",
        "clearWorld",
        "ClearWorld",
        "Reset",
        "Finish",
        "SetWorldSize",
        "setWorldSize",
        "RandomizeTreeStyle",
        "RandomizeCaveBackgrounds",
    }
    main_names = {
        "SetWorldSize",
        "setWorldSize",
        "Initialize",
        "ResetGameCounter",
    }

    emitted_world_gen = emit("WorldGen", world_gen, world_gen_names)
    emitted_main = emit("Main", main_source, main_names)
    add_passes_evidence = inspect_add_passes(world_gen)

    required_world_gen = {"GenerateWorld", "Reset", "Finish"}
    missing_world_gen = sorted(required_world_gen - emitted_world_gen)
    if missing_world_gen:
        raise SystemExit(f"Pinned Terraria.WorldGen is missing required generation methods: {missing_world_gen}")
    if not ({"clearWorld", "ClearWorld"} & emitted_world_gen):
        raise SystemExit("Pinned Terraria.WorldGen did not expose clearWorld/ClearWorld.")

    if args.pass_catalog_output:
        write_pass_catalog(Path(args.pass_catalog_output), add_passes_evidence)

    print(f"worldgen_methods_emitted={sorted(emitted_world_gen)}")
    print(f"main_methods_emitted={sorted(emitted_main)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
