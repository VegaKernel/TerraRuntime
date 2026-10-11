using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

internal static class ProjectileFundedQuantity1458Checks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            Xunit.Assert.True(condition, message);
        }
        foreach (short type in new short[] { 97, 40, 166, 42 })
        {
            using var fixture = new Fixture(type, 3, 1);
            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var capture), "capture");

            var debit = new RuntimePlayerInventoryMutation(54, fixture.Item with { Stack = 2 });
            Check(fixture.Players.CanCommitProjectileUse(capture!, debit, 0), "preflight");

            Check(fixture.Players.TryCommitProjectileUseUnpublished(capture!, debit, 0), "debit");
            Check(fixture.FundedQuantity == 0 && fixture.Physical == 2, "exact partial debit");

            Check(!fixture.Players.TryCommitProjectileUseUnpublished(capture!, debit, 0), "once");

            fixture.Events.OnItem = () => Check(fixture.FundedQuantity == 0 && fixture.Physical == 2, "callback joint image");
            fixture.Players.PublishProjectileUse(capture!, debit, 0);
            fixture.Events.OnItem = null;

            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var next), "next imported");
            var second = new RuntimePlayerInventoryMutation(54, fixture.Item with { Stack = 1 });
            Check(fixture.Players.TryCommitProjectileUseUnpublished(next!, second, 0), "historical q0 physical use");
            Check(fixture.FundedQuantity == 0 && fixture.Physical == 1, "no budget mint");

            fixture.Players.TryApply(new PlayerEquipmentRuntimeCommand(fixture.Connection, new(fixture.Connection.Player.Slot, 54, 3, 0, type, 0)));
            Check(fixture.FundedQuantity == 0 && fixture.Physical == 3, "raw restoration no renewal");

            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var same), "conserve capture");
            var conserve = new RuntimePlayerInventoryMutation(54, fixture.Item);
            Check(fixture.Players.TryCommitProjectileUseUnpublished(same!, conserve, 0) && fixture.FundedQuantity == 0 && fixture.Physical == 3, "conserve q0");

            var done = new TaskCompletionSource<RuntimePlayerTransferState?>();
            fixture.Players.TryApply(new PlayerTransferDetachRuntimeCommand(fixture.Connection, done));
            var dto = done.Task.GetAwaiter().GetResult();
            Check(dto is not null && dto.FundedInventoryQuantities[54] == 0 && dto.Inventory[54].Stack == 3, "transfer exact spent budget");

            var target = new PlayerAuthority(new Events(), null);
            var attach = new TaskCompletionSource<bool>();
            target.TryApply(new PlayerTransferAttachRuntimeCommand(fixture.Connection, dto!, 100, 100, false, false, attach));
            Check(attach.Task.GetAwaiter().GetResult(), "actual destination attach");
            Check(Access.Inventory(target).CaptureFundedQuantities(fixture.Connection)[54] == 0 && target.TryGetInventoryItem(fixture.Connection, 54, out var transferred) && transferred.Stack == 3, "destination preserves spent budget");

        }
        foreach (short budget in new short[] { 0, 1, 3 })
        {
            using var fixture = new Fixture(97, 3, budget);
            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var capture), "full conserve capture");
            var same = new RuntimePlayerInventoryMutation(54, fixture.Item);
            Check(fixture.Players.TryCommitProjectileUseUnpublished(capture!, same, 0) && fixture.FundedQuantity == budget, "conserve all budget");

            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var fresh), "fresh");
            var before = fixture.Item;
            short fundedQuantity = fixture.FundedQuantity;

            foreach (var invalid in new[] { before with { Stack = 1 }, before with { Stack = 4 }, before with { ItemType = new(40) }, before with { ItemFlags = 1 } })
            {
                var mutation = new RuntimePlayerInventoryMutation(54, invalid);
                Check(!fixture.Players.CanCommitProjectileUse(fresh!, mutation, 0), "invalid source transition preflight");
                Check(!fixture.Players.TryCommitProjectileUseUnpublished(fresh!, mutation, 0), "invalid source transition adoption");
                Check(fixture.FundedQuantity == fundedQuantity && fixture.Item == before, "rejection unchanged");

            }
            fixture.Players.TryApply(new PlayerEquipmentRuntimeCommand(fixture.Connection, new(fixture.Connection.Player.Slot, 0, 1, 0, 24, 0)));
            var debit = new RuntimePlayerInventoryMutation(54, before with { Stack = 2 });
            Check(!fixture.Players.TryCommitProjectileUseUnpublished(fresh!, debit, 0) && fixture.FundedQuantity == fundedQuantity, "input/serial stale");

        }
        using (var fixture = new Fixture(97, 3, 3))
        {
            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var capture), "fully funded capture");
            var mutation = new RuntimePlayerInventoryMutation(54, fixture.Item with { Stack = 2 });
            Check(fixture.Players.TryCommitProjectileUseUnpublished(capture!, mutation, 0) && fixture.FundedQuantity == 2 && fixture.Physical == 2, "fully funded consumes exact one");
        }
        using (var fixture = new Fixture(97, 1, 1))
        {
            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var capture), "last capture");
            var last = new RuntimePlayerInventoryMutation(54, default);
            Check(fixture.Players.TryCommitProjectileUseUnpublished(capture!, last, 0) && fixture.FundedQuantity == 0 && fixture.Item.IsEmpty, "last Air");
        }
        using (var fixture = new Fixture(97, 3, 1))
        {
            Check(fixture.Players.TryCaptureProjectileUse(fixture.Connection, out var capture), "throw capture");
            var mutation = new RuntimePlayerInventoryMutation(54, fixture.Item with { Stack = 2 });
            Check(fixture.Players.TryCommitProjectileUseUnpublished(capture!, mutation, 0), "throw debit");
            var original = new InvalidOperationException("observer");
            fixture.Events.OnItem = () =>
            {
                Check(fixture.FundedQuantity == 0 && fixture.Physical == 2, "throw observes joint");
                throw original;
            };

            try
            {
                fixture.Players.PublishProjectileUse(capture!, mutation, 0);
                throw new Exception("no observer throw");
            }
            catch (InvalidOperationException e)
            {
                Check(ReferenceEquals(e, original), "exception identity");
            }
            Check(fixture.FundedQuantity == 0 && fixture.Physical == 2, "throw retained");

            fixture.Events.OnItem = () =>
            {
                fixture.Events.OnItem = null;
                fixture.Players.TryApply(new PlayerEquipmentRuntimeCommand(fixture.Connection, new(fixture.Connection.Player.Slot, 54, 3, 0, 97, 0)));
                Check(fixture.FundedQuantity == 0 && fixture.Physical == 3, "reentrant raw cannot mint");
                Check(!fixture.Players.TryCommitProjectileUseUnpublished(capture!, mutation, 0), "reentrant old capture refuses");
            };
            fixture.Players.PublishProjectileUse(capture!, mutation, 0);

        }
        return checks;

    }
    sealed class Fixture : IDisposable
    {
        readonly PlayerSlotPool pool = new(1);
        readonly PlayerJoinSession session;
        internal readonly Events Events = new();
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        internal RuntimePlayerInventoryStore Store => Access.Inventory(Players);

        internal RuntimePlayerInventoryItem Item
        {
            get
            {
                if (!Store.TryGet(Connection, 54, out var item)) throw new Exception("item");
                return item;
            }
        }
        internal short FundedQuantity => Store.CaptureFundedQuantities(Connection)[54];
        internal short Physical => Item.Stack;

        internal Fixture(short type, short stack, short fundedQuantity)
        {
            Players = new(Events, null);
            if (!pool.TryAcquireConnection(out var lease)) throw new Exception("lease");
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99), session.Handle);
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, 54, stack, 0, type, 0)));
            var funds = new short[59];
            funds[54] = fundedQuantity;
            Store.AdoptFundedQuantities(Connection, funds);
        }
        public void Dispose() => session.Dispose();

    }
    sealed class Events : IRuntimePlayerEventSink
    {
        internal Action? OnItem;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r)
        { }
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) => OnItem?.Invoke();
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r)
        { }
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r)
        { }
        public void PlayerDisconnected(ConnectionHandle c)
        { }
    }
    static class Access
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")] internal static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority p);
    }
}
