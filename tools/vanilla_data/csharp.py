"""Lexical views for offline C# data readers; offsets/newlines remain unchanged."""
from __future__ import annotations

import re

_SPECIAL = re.compile(r'''[/\'"$@]''')


def require_unconditional(code: str) -> None:
    """An offline layout reader has no compiler symbol set; never guess a branch."""
    if re.search(r'(?m)^[ \t]*#[ \t]*(?:if|elif|else|endif|define|undef)\b', code):
        raise ValueError('Conditional compilation is not admitted by this offline data reader')


def _comment_end(text: str, at: int) -> int | None:
    if text.startswith('//', at):
        end = text.find('\n', at + 2)
        return len(text) if end < 0 else end
    if text.startswith('/*', at):
        end = text.find('*/', at + 2)
        if end < 0:
            raise ValueError('Unterminated C# block comment')
        return end + 2
    return None


def _interpolation_end(text: str, at: int, closing_count: int) -> int:
    nested = 0
    while at < len(text):
        comment = _comment_end(text, at)
        if comment is not None:
            at = comment
            continue
        literal = _literal_end(text, at)
        if literal is not None:
            at = literal
            continue
        if text[at] == '{':
            nested += 1
        elif text[at] == '}':
            if nested == 0 and text.startswith('}' * closing_count, at):
                return at + closing_count
            nested -= 1
            if nested < 0:
                raise ValueError('Unsupported C# interpolation closing braces')
        at += 1
    raise ValueError('Unterminated C# interpolation')


def _literal_end(text: str, at: int) -> int | None:
    if text[at] == "'":
        cursor = at + 1
        while cursor < len(text):
            if text[cursor] == '\\':
                cursor += 2
            elif text[cursor] == "'":
                return cursor + 1
            elif text[cursor] in '\r\n':
                break
            else:
                cursor += 1
        raise ValueError('Unterminated C# character literal')
    cursor = at
    while cursor < len(text) and text[cursor] in '$@':
        cursor += 1
    if cursor == len(text) or text[cursor] != '"':
        return None
    prefix = text[at:cursor]
    if prefix.count('@') > 1:
        raise ValueError('Unsupported C# literal prefix')
    dollars = prefix.count('$')
    verbatim = '@' in prefix
    end = cursor
    while end < len(text) and text[end] == '"':
        end += 1
    quotes = end - cursor
    if quotes >= 3:
        if verbatim:
            raise ValueError('Unsupported verbatim raw C# literal')
        cursor = end
        while cursor < len(text):
            if text.startswith('"' * quotes, cursor):
                return cursor + quotes
            if dollars and text.startswith('{' * dollars, cursor):
                cursor = _interpolation_end(text, cursor + dollars, dollars)
            else:
                cursor += 1
        raise ValueError('Unterminated raw C# literal')
    if dollars > 1:
        raise ValueError('Multiple interpolation dollars require a raw literal')
    cursor += 1
    while cursor < len(text):
        if verbatim and text.startswith('""', cursor):
            cursor += 2
        elif text[cursor] == '"':
            return cursor + 1
        elif not verbatim and text[cursor] == '\\':
            cursor += 2
        elif dollars and text.startswith('{{', cursor):
            cursor += 2
        elif dollars and text[cursor] == '{':
            cursor = _interpolation_end(text, cursor + 1, 1)
        elif not verbatim and text[cursor] in '\r\n':
            raise ValueError('Newline in ordinary C# string literal')
        else:
            cursor += 1
    raise ValueError('Unterminated C# string literal')


def mask(text: str) -> tuple[str, str]:
    """Return (code without comments/literals, original literals without comments)."""
    code, comments_only = list(text), list(text)
    cursor = 0
    while cursor < len(text):
        special = _SPECIAL.search(text, cursor)
        if special is None:
            break
        cursor = special.start()
        end = _comment_end(text, cursor)
        comment = end is not None
        if end is None:
            end = _literal_end(text, cursor)
        if end is None:
            cursor += 1
            continue
        for index in range(cursor, end):
            if text[index] not in '\r\n':
                code[index] = ' '
                if comment:
                    comments_only[index] = ' '
        cursor = end
    return ''.join(code), ''.join(comments_only)
