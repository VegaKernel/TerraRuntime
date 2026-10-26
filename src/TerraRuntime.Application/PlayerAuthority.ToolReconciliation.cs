using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal enum ToolReportObservationResult1458 : byte
{
    NotApplicable,
    Retained,
    Rejected
}

internal sealed partial class PlayerAuthority
{
    internal ToolReportObservationResult1458 ObserveToolReport(ConnectionHandle connection,
        in TerrariaTileManipulationState report, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (report.Action is not (0 or 2 or 7 or 14 or 20 or 23))
            return ToolReportObservationResult1458.NotApplicable;
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserve(in report)
            ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    internal ToolReportObservationResult1458 ObserveToolShoot(ConnectionHandle connection,
        in TerrariaProjectileUpdateState report, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        // Route by this producer family, not by merely holding any tool. An
        // ongoing ordinary projectile family's reports retain their old owner.
        if (report.ProjectileType <= 0) return ToolReportObservationResult1458.Rejected;
        if (!VanillaHeldToolProjectile1458.IsSupported(new ProjectileTypeId(report.ProjectileType)) &&
            report.ProjectileType != VanillaThrownAxeProjectile1458.ProjectileType)
            return ToolReportObservationResult1458.NotApplicable;
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null)
            return ToolReportObservationResult1458.Rejected;
        return input.TryObserve(in report)
            ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    internal ToolReportObservationResult1458 ObserveToolChestUpdate(ConnectionHandle connection,
        in TerrariaChestUpdatesState1458 report, out RuntimePlayerToolInput1458? input)
    {
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserve(in report)
            ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    internal ToolReportObservationResult1458 ObserveToolSquare(ConnectionHandle connection,
        in TerrariaToolTileSquareState1458 report, out RuntimePlayerToolInput1458? input)
    {
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserve(in report) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    internal ToolReportObservationResult1458 ObserveToolLucy(ConnectionHandle connection,
        in TerrariaLucyAxeMessageState1458 report, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        // Idle/storage/throw/pickup/unknown source bytes remain ordinary cosmetic
        // relays. No selected-item or cooldown inference turns them into authority.
        if (report.Source is < 4 or > 6) return ToolReportObservationResult1458.NotApplicable;
        if (!membership.TryGet(connection, out var member) ||
            !inventory.TryGet(connection, member.SelectedItem, out var selected))
            return ToolReportObservationResult1458.Rejected;
        if (selected.ItemType.Value != 5095) return ToolReportObservationResult1458.NotApplicable;
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserve(in report)
            ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    internal ToolReportObservationResult1458 ObserveToolObject(ConnectionHandle connection,
        in TerrariaPlaceObjectState report, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (!membership.TryGet(connection, out var member) ||
            !inventory.TryGet(connection, member.SelectedItem, out var selected))
            return ToolReportObservationResult1458.Rejected;
        if (selected.ItemType.Value != 5295) return ToolReportObservationResult1458.NotApplicable;
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserve(in report)
            ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    private ToolReportObservationResult1458 TryCaptureToolInput(ConnectionHandle connection,
        out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (!membership.TryGet(connection, out var member) ||
            !inventory.TryGet(connection, member.SelectedItem, out var selected))
            return ToolReportObservationResult1458.Rejected;
        if (!VanillaToolCatalog1458.TryGet(selected.ItemType, out _))
            return ToolReportObservationResult1458.NotApplicable;
        if (playerUpdateWorld is not { } world ||
            !VanillaToolCatalog1458.TryResolve(selected.ItemType, selected.Prefix,
                world.RemixWorld, world.WindowsItemPrefixArithmetic, out var tool) ||
            member.ItemPhase is not { ToolUse: { } tools } phase)
            return ToolReportObservationResult1458.Rejected;
        if (tools.Input is { IsCurrent: true } existing)
        {
            input = existing;
            return ToolReportObservationResult1458.Retained;
        }
        // Selection/input/provider changes retire the old observation, never replay
        // it through the raw mutation path. This report starts a separately captured
        // current cohort; it does not inherit old hit credit or old RNG.
        if (tools.Input is not null)
        {
            tools = tools with { Input = null, DeferredHealthPublication =
                tools.Input.TakeHealthPublication() ?? tools.DeferredHealthPublication };
            phase = phase with { ToolUse = tools };
            member.ItemPhase = phase;
        }

        var candidate = new RuntimePlayerToolInput1458(tool);
        var attached = phase with { ToolUse = tools with { Input = candidate } };
        // This only attaches a bounded command observation. No clocks, inventory,
        // RNG or public player state have changed and no provider is invoked.
        bool retainedProjection = member.RemotePhaseSnapshot == member.CaptureSnapshot() &&
            member.RemotePhaseItem == phase &&
            member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
        member.ItemPhase = attached;
        if (retainedProjection) member.MarkRemotePhaseSnapshot();
        if (!TryCaptureToolActor(connection, out var capture) || capture is null)
        {
            if (member.ItemPhase == attached)
            {
                member.ItemPhase = phase;
                if (retainedProjection) member.MarkRemotePhaseSnapshot();
            }
            return ToolReportObservationResult1458.Rejected;
        }
        candidate.BindActor(capture);
        input = candidate;
        return ToolReportObservationResult1458.Retained;
    }

    // A rejected bounded cohort must lose its exact actor binding before a
    // correction sink can reenter. Preserve clocks and pending health; this is
    // not a successful action, selector reset or a second receipt history.
    internal bool TryRetireObservedToolInput(RuntimePlayerToolInput1458 input,
        out ConnectionHandle connection)
    {
        connection = default;
        if (input.Actor is not { } actor ||
            !membership.TryGet(actor.Connection, out var member) ||
            !ReferenceEquals(member.ItemPhase?.ToolUse?.Input, input)) return false;
        RetireToolInput(member, input);
        connection = member.Connection;
        return !ReferenceEquals(member.ItemPhase?.ToolUse?.Input, input);
    }

    private static void RetireStaleToolInput(RuntimePlayerMember member)
    {
        if (member.ItemPhase?.ToolUse?.Input is not { } input || input.IsCurrent) return;
        RetireToolInput(member, input);
    }

    private static void RetireToolInput(RuntimePlayerMember member, RuntimePlayerToolInput1458 input)
    {
        if (member.ItemPhase is not { ToolUse: { } tools } phase || !ReferenceEquals(tools.Input, input)) return;
        bool retainedProjection = member.RemotePhaseSnapshot == member.CaptureSnapshot() &&
            member.RemotePhaseItem == phase &&
            member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
        member.ItemPhase = phase with { ToolUse = tools with { Input = null,
            DeferredHealthPublication = input.TakeHealthPublication() ?? tools.DeferredHealthPublication } };
        if (retainedProjection) member.MarkRemotePhaseSnapshot();
    }

    internal bool ObserveToolPicking(ConnectionHandle connection, in TerrariaTilePickingState1458 report) =>
        membership.TryGet(connection, out var member) &&
        member.ItemPhase?.ToolUse?.Input is { } input && input.TryObserve(in report);

    internal bool IsCurrentToolInput(RuntimePlayerToolInput1458 input, ulong revision) =>
        input.IsCurrent && input.Revision == revision && input.Actor is { } captured &&
        membership.TryGet(captured.Connection, out var member) &&
        ReferenceEquals(member.ItemPhase?.ToolUse?.Input, input);
}
