#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path


METHOD_SIGNATURE_PATTERN = re.compile(
    r"^[ \t]*(?:public|private|internal|protected)(?: static)? [^\r\n{;]*\b([A-Za-z_]\w*)\([^\r\n)]*\)",
    re.MULTILINE,
)


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


def all_signatures(source: str) -> list[str]:
    return [match.group(0).strip() for match in METHOD_SIGNATURE_PATTERN.finditer(source)]


def named_signatures(source: str, names: set[str]) -> list[str]:
    return [signature for signature in all_signatures(source) if method_name(signature) in names]


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


def extract_invocations(method: str, callee: str) -> list[str]:
    invocations: list[str] = []
    pattern = re.compile(rf"\b{re.escape(callee)}\s*\(")
    for match in pattern.finditer(method):
        open_paren = method.find("(", match.start())
        depth = 0
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
                if depth == 0:
                    invocations.append(method[open_paren + 1:index])
                    break
        else:
            raise SystemExit(f"Unterminated {callee}(...) invocation in pinned Terraria.WorldGen.{method_name(method)}.")
    return invocations


def first_argument(arguments: str) -> str:
    paren_depth = 0
    bracket_depth = 0
    brace_depth = 0
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
        if ch == "(":
            paren_depth += 1
        elif ch == ")":
            paren_depth -= 1
        elif ch == "[":
            bracket_depth += 1
        elif ch == "]":
            bracket_depth -= 1
        elif ch == "{":
            brace_depth += 1
        elif ch == "}":
            brace_depth -= 1
        elif ch == "," and paren_depth == 0 and bracket_depth == 0 and brace_depth == 0:
            return compact(arguments[:index])

    return compact(arguments)


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
    invocations = extract_invocations(method, "AddGenerationPass")
    registrations = [first_argument(arguments) for arguments in invocations]
    if not registrations:
        raise SystemExit("Pinned Terraria.WorldGen.AddPasses contains no AddGenerationPass registrations.")

    sequence_fingerprint = hashlib.sha256("\n".join(registrations).encode("utf-8")).hexdigest()
    typed_passes = [value for value in registrations if value.startswith("new ")]
    named_passes = [value for value in registrations if value.startswith("GenPassNameID.")]
    unresolved = [value for value in registrations if value not in typed_passes and value not in named_passes]

    print(f"WorldGen_AddPasses_signature={compact(signature)}")
    print(f"WorldGen_AddPasses_sha256={fingerprint}")
    print(f"WorldGen_AddPasses_registration_count={len(registrations)}")
    print(f"WorldGen_AddPasses_registration_sequence_sha256={sequence_fingerprint}")
    for index, value in enumerate(registrations):
        print(f"WorldGen_AddPasses_registration_{index:03d}={value}")
    print(f"WorldGen_AddPasses_typed_registration_count={len(typed_passes)}")
    print(f"WorldGen_AddPasses_named_registration_count={len(named_passes)}")
    print(f"WorldGen_AddPasses_unresolved_registration_count={len(unresolved)}")
    for index, value in enumerate(unresolved):
        print(f"WorldGen_AddPasses_unresolved_{index:03d}={value}")

    return {
        "signature": compact(signature),
        "fingerprint": fingerprint,
        "sequence_fingerprint": sequence_fingerprint,
        "registrations": registrations,
        "typed_passes": typed_passes,
        "named_passes": named_passes,
        "unresolved": unresolved,
    }


def inspect_special_seed_filter(source: str) -> dict[str, str]:
    signatures = named_signatures(source, {"DisablePassesForSpecialSeeds"})
    if len(signatures) != 1:
        raise SystemExit(
            "Pinned Terraria.WorldGen must expose exactly one DisablePassesForSpecialSeeds method; "
            f"found {len(signatures)}."
        )
    signature = signatures[0]
    method = extract_method(source, signature)
    fingerprint = hashlib.sha256(method.encode("utf-8")).hexdigest()
    print(f"WorldGen_DisablePassesForSpecialSeeds_signature={compact(signature)}")
    print(f"WorldGen_DisablePassesForSpecialSeeds_sha256={fingerprint}")
    return {"signature": compact(signature), "fingerprint": fingerprint}


def write_pass_catalog(
    path: Path,
    registrations: dict[str, object],
    special_seed_filter: dict[str, str],
) -> None:
    values = list(registrations["registrations"])
    typed_passes = list(registrations["typed_passes"])
    named_passes = list(registrations["named_passes"])
    unresolved = list(registrations["unresolved"])
    lines = [
        "source=TerrariaServer 1.4.5.8",
        "decompiler=ilspycmd 11.0.0.9375",
        "catalog_state=source-registration-order",
        "catalog_semantics=lexical AddGenerationPass order before special-seed filtering",
        f"WorldGen_AddPasses_signature={registrations['signature']}",
        f"WorldGen_AddPasses_sha256={registrations['fingerprint']}",
        f"WorldGen_AddPasses_registration_count={len(values)}",
        f"WorldGen_AddPasses_registration_sequence_sha256={registrations['sequence_fingerprint']}",
    ]
    lines.extend(
        f"WorldGen_AddPasses_registration_{index:03d}={value}"
        for index, value in enumerate(values)
    )
    lines.extend([
        f"WorldGen_AddPasses_typed_registration_count={len(typed_passes)}",
        f"WorldGen_AddPasses_named_registration_count={len(named_passes)}",
        f"WorldGen_AddPasses_unresolved_registration_count={len(unresolved)}",
        f"WorldGen_DisablePassesForSpecialSeeds_signature={special_seed_filter['signature']}",
        f"WorldGen_DisablePassesForSpecialSeeds_sha256={special_seed_filter['fingerprint']}",
    ])
    lines.extend(
        f"WorldGen_AddPasses_unresolved_{index:03d}={value}"
        for index, value in enumerate(unresolved)
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 fresh-world initialization and pass registration."
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
        "DisablePassesForSpecialSeeds",
    }
    main_names = {
        "SetWorldSize",
        "setWorldSize",
        "Initialize",
        "ResetGameCounter",
    }

    emitted_world_gen = emit("WorldGen", world_gen, world_gen_names)
    emitted_main = emit("Main", main_source, main_names)
    registrations = inspect_add_passes(world_gen)
    special_seed_filter = inspect_special_seed_filter(world_gen)

    required_world_gen = {"GenerateWorld", "Reset", "Finish", "DisablePassesForSpecialSeeds"}
    missing_world_gen = sorted(required_world_gen - emitted_world_gen)
    if missing_world_gen:
        raise SystemExit(f"Pinned Terraria.WorldGen is missing required generation methods: {missing_world_gen}")
    if not ({"clearWorld", "ClearWorld"} & emitted_world_gen):
        raise SystemExit("Pinned Terraria.WorldGen did not expose clearWorld/ClearWorld.")

    if args.pass_catalog_output:
        write_pass_catalog(Path(args.pass_catalog_output), registrations, special_seed_filter)

    print(f"worldgen_methods_emitted={sorted(emitted_world_gen)}")
    print(f"main_methods_emitted={sorted(emitted_main)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
