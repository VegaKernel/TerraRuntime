from pathlib import Path

p = Path('src/TerraRuntime.Core/Npcs/VanillaNpcCatchCatalog1458.cs')
text = p.read_text()
text = text.replace('if (!itemType.IsAssigned)', 'if (itemType.IsNone)')
p.write_text(text)

p = Path('src/TerraRuntime/RuntimeTownNpcRescueService1458.cs')
text = p.read_text()
text = text.replace(
    'Terraria.Protocol.Multiplicity.TerrariaNpcTalkCodec.MaximumNpcSlots',
    'TerraRuntime.Protocol.Multiplicity.TerrariaNpcTalkCodec.MaximumNpcSlots')
p.write_text(text)

p = Path('src/TerraRuntime/RuntimeNpcCatchCommands.cs')
text = p.read_text()
text = text.replace(
    'using TerraRuntime.Core;\nusing TerraRuntime.Protocol.Multiplicity;',
    'using TerraRuntime.Contracts.Runtime;\nusing TerraRuntime.Protocol.Multiplicity;')
p.write_text(text)

p = Path('src/TerraRuntime/RuntimeNpcCatchNetworkIngress.cs')
text = p.read_text()
if 'using TerraRuntime.Contracts.Runtime;' not in text:
    text = text.replace(
        'using TerraRuntime.Core;\n',
        'using TerraRuntime.Contracts.Runtime;\nusing TerraRuntime.Core;\n')
p.write_text(text)

p = Path('tests/TerraRuntime.Tests/TerrariaNpcCatchCodecTests.cs')
text = p.read_text()
old = '''        var frame = new TerrariaFrame((byte)TerrariaMessageId.CatchNpc, new ReadOnlySequence<byte>(new byte[] { 1 }));'''
new = '''        var payload = new ReadOnlySequence<byte>(new byte[] { 1 });\n        var frame = new TerrariaFrame(4, (byte)TerrariaMessageId.CatchNpc, payload, payload);'''
if old not in text:
    raise SystemExit('packet 70 negative-frame fixture anchor drifted')
text = text.replace(old, new, 1)
p.write_text(text)
print('N4 compatibility fixes applied; workflow trigger v4')
