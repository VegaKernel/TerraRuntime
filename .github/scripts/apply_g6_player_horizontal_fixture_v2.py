from pathlib import Path

path = Path("tests/TerraRuntime.Tests/TrustedHostModuleLoaderTests.cs")
text = path.read_text(encoding="utf-8")
old = '''        public ValueTask<bool> DespawnAsync(
            ServerPlayerId id,
            CancellationToken cancellationToken = default)
'''
new = '''        public ValueTask<bool> SetHorizontalIntentAsync(
            ServerPlayerId id,
            ServerPlayerHorizontalIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask<bool> DespawnAsync(
            ServerPlayerId id,
            CancellationToken cancellationToken = default)
'''
count = text.count(old)
if count != 1:
    raise SystemExit(f"{path}: expected one TestServerPlayerOperations insertion target, found {count}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("G6 horizontal host fixture wiring applied")
