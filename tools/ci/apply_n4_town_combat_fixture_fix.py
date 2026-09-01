#!/usr/bin/env python3
from pathlib import Path

p = Path('tests/TerraRuntime.Tests/RuntimeTownNpcCombat1458Tests.cs')
text = p.read_text(encoding='utf-8-sig')
replacements = [
    (
        '                480f,\n                160f,',
        '                400f,\n                160f,',
        'Town combat hostile-position fixture anchor'),
    (
        '        for (int i = 0; i < 9; i++)\n            f.Combat.Tick();',
        '        for (int i = 0; i < 10; i++)\n            f.Combat.Tick();',
        'Merchant attack-tick fixture anchor'),
]
for old, new, label in replacements:
    if text.count(old) != 1:
        raise SystemExit(f'{label} missing or ambiguous')
    text = text.replace(old, new, 1)
p.write_text(text, encoding='utf-8')
print('Town combat fixtures aligned with strict source range and state-10 tick cadence')
