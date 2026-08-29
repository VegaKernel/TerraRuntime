using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerSlotRegistryConcurrencyTests
{
    [Fact]
    public async Task Player_handle_lookup_does_not_wait_for_lifecycle_monitor()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var slots = new PlayerSlotPool(1);
        var registry = new RuntimeServerPlayerSlotRegistry(slots);
        Assert.Equal(
            ServerPlayerSlotAcquireResult.Acquired,
            registry.TryAcquire(new ServerPlayerId("test:hot-path"), out var lease));
        Assert.NotNull(lease);

        FieldInfo gateField = typeof(RuntimeServerPlayerSlotRegistry).GetField(
            "gate",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("Lifecycle gate field was not found.");
        object gate = gateField.GetValue(registry)
            ?? throw new Xunit.Sdk.XunitException("Lifecycle gate was null.");

        using var gateHeld = new ManualResetEventSlim(false);
        using var releaseGate = new ManualResetEventSlim(false);
        var holder = new Thread(() =>
        {
            lock (gate)
            {
                gateHeld.Set();
                releaseGate.Wait();
            }
        })
        {
            IsBackground = true,
            Name = "server-player-slot-gate-holder"
        };

        holder.Start();
        gateHeld.Wait(cancellationToken);

        try
        {
            Task<(bool Found, ServerPlayerSlotBinding Binding)> lookup = Task.Run(() =>
            {
                bool found = registry.TryGet(lease!.Player, out ServerPlayerSlotBinding binding);
                return (found, binding);
            }, cancellationToken);
            Task completed = await Task.WhenAny(
                lookup,
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));

            Assert.Same(lookup, completed);
            (bool found, ServerPlayerSlotBinding binding) = await lookup;
            Assert.True(found);
            Assert.Equal(lease!.Player, binding.Player);
        }
        finally
        {
            releaseGate.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(2)));
            lease!.Dispose();
        }
    }
}
