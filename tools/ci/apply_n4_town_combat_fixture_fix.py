#!/usr/bin/env python3
from pathlib import Path

p = Path('tests/TerraRuntime.Tests/RuntimeTownNpcCombat1458Tests.cs')
text = p.read_text(encoding='utf-8-sig')
old = '                480f,\n                160f,'
new = '                400f,\n                160f,'
if text.count(old) != 1:
    raise SystemExit('Town combat hostile-position fixture anchor missing or ambiguous')
p.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Town combat hostile fixture moved inside the strict source danger range')
