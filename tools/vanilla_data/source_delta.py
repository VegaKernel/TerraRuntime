"""Bounded, source-only migration triage for two pinned Windows references."""
from __future__ import annotations

from difflib import SequenceMatcher
from hashlib import sha256
from pathlib import Path, PurePosixPath
import re

from .csharp import mask, require_unconditional


ASSEMBLIES = {
    '1.4.5.8': 'd87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e',
    '1.4.6.1': 'e6d7db36e8ababc465490743e03f46f3c76d2fcdb757239e60a2b139a92f03a1',
}


def _read(root: Path, relative: str, limit: int) -> bytes:
    path = (root / relative).resolve(strict=True)
    if not path.is_relative_to(root) or not path.is_file():
        raise ValueError(f'Not a regular file inside the reference tree: {relative}')
    with path.open('rb') as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise ValueError(f'Reference input exceeds {limit} bytes: {relative}')
    return data


def _load(version: str, assembly: Path, root: Path, relative: str) -> tuple[dict, str]:
    root = root.resolve(strict=True)
    assembly = assembly.resolve(strict=True)
    assembly_hash = sha256(_read(assembly.parent, assembly.name, 64 * 1024 * 1024)).hexdigest()
    if assembly_hash != ASSEMBLIES[version]:
        raise ValueError(f'Assembly SHA-256 does not match the pinned Windows Terraria {version} server')
    info = _read(root, 'Properties/AssemblyInfo.cs', 64 * 1024).decode('utf-8-sig')
    _, info = mask(info)
    versions = re.findall(r'(?m)^\s*\[assembly:\s*AssemblyVersion\("([\d.]+)"\)\s*\]', info)
    if versions != [version]:
        raise ValueError(f'Decompiled AssemblyInfo does not identify Terraria {version}')
    source = _read(root, relative, 4 * 1024 * 1024)
    return {'version': version, 'assemblySha256': assembly_hash,
            'sourceSha256': sha256(source).hexdigest()}, source.decode('utf-8-sig').replace('\r\n', '\n')


def _locate(code: str, text: str, name: str) -> tuple[int, list[str]]:
    pattern = (r'(?m)^[ \t]*(?:public|private|internal|protected)\b[^\n;={}]*?\b'
               + re.escape(name) + r'[ \t]*\([^\n()]*\)')
    matches = list(re.finditer(pattern, code))
    if len(matches) != 1:
        raise ValueError(f'Expected one supported declaration for {name}; found {len(matches)}')
    match = matches[0]
    brace = match.end() + re.match(r'\s*', code[match.end():]).end()
    if code[brace:brace + 1] != '{':
        raise ValueError(f'Expected a block body immediately after the signature of {name}')
    depth = 0
    for offset in range(brace, len(code)):
        if code[offset] == '{':
            depth += 1
        elif code[offset] == '}':
            depth -= 1
            if depth == 0:
                lines = text[match.start():offset + 1].split('\n')
                if len(lines) > 4096:
                    raise ValueError(f'Method exceeds 4096 lines: {name}')
                return code.count('\n', 0, match.start()) + 1, lines
    raise ValueError(f'Unterminated method body: {name}')


def _span(start: int, begin: int, end: int) -> dict:
    return {'startLine': start + begin, 'lineCount': end - begin}


def compare(before_assembly: Path, before_root: Path, after_assembly: Path, after_root: Path,
            relative: str, methods: list[str]) -> dict:
    relative_path = PurePosixPath(relative)
    if (relative_path.is_absolute() or '..' in relative_path.parts or relative_path.suffix != '.cs'
            or relative_path.as_posix() != relative or any(char in relative for char in '\\:')):
        raise ValueError('Expected a canonical relative .cs path inside both source trees')
    if not 1 <= len(methods) <= 32 or len(set(methods)) != len(methods) or any(
            not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', name) for name in methods):
        raise ValueError('Expected 1..32 unique simple method names')
    before, before_text = _load('1.4.5.8', before_assembly, before_root, relative)
    after, after_text = _load('1.4.6.1', after_assembly, after_root, relative)
    before_code, _ = mask(before_text)
    after_code, _ = mask(after_text)
    require_unconditional(before_code)
    require_unconditional(after_code)
    rows = []
    for name in methods:
        old_start, old_lines = _locate(before_code, before_text, name)
        new_start, new_lines = _locate(after_code, after_text, name)
        rows.append({'method': name, 'status': 'unchanged-text' if old_lines == new_lines else 'changed-text',
                     'before': _span(old_start, 0, len(old_lines)), 'after': _span(new_start, 0, len(new_lines)),
                     'changes': [{'kind': kind, 'before': _span(old_start, old_begin, old_end),
                                  'after': _span(new_start, new_begin, new_end)}
                                 for kind, old_begin, old_end, new_begin, new_end in
                                 SequenceMatcher(None, old_lines, new_lines, autojunk=False).get_opcodes()
                                 if kind != 'equal']})
    return {'accepted': True, 'operation': 'source-delta', 'analysisOnly': True, 'file': relative,
            'before': before, 'after': after, 'methods': rows,
            'scope': 'Selected decompiled text only; no executable or behavioral equivalence claim'}
