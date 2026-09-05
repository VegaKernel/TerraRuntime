using System.Collections.ObjectModel;
using System.Globalization;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using TerraRuntime.Application.Operations;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Application.TerminalUI;

/// <summary>Live, generation-safe operator view for one connected player session.</summary>
internal sealed class PlayerDetailsWindow : Window
{
    private readonly IPlayerAdministrativeOperations administration;
    private readonly RuntimeConnectionSessionDirectory sessions;
    private readonly IPlayerOperations players;
    private readonly RuntimePlayerSnapshot initialPlayer;
    private readonly PlayerHandle handle;
    private readonly Label identity;
    private readonly Label endpoint;
    private readonly Label sessionDuration;
    private readonly Label character;
    private readonly Label vitals;
    private readonly Label position;
    private const string GodModeDisabled = "Disabled";
    private const string GodModeEnabled = "Enabled";

    private readonly DropDownList godMode;
    private readonly Label feedback;
    private bool updatingGodModeSelection;
    private bool godModeDirty;

    public PlayerDetailsWindow(
        RuntimePlayerSnapshot player,
        IPlayerOperations players,
        IPlayerAdministrativeOperations administration,
        RuntimeConnectionSessionDirectory sessions)
    {
        initialPlayer = player;
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.administration = administration ?? throw new ArgumentNullException(nameof(administration));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        handle = new PlayerHandle(new PlayerSlotId(player.Slot), new PlayerSessionGeneration(player.Generation));

        Title = $"Player #{player.Slot} · {player.Name}";
        Width = 74;
        Height = 18;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        identity = LabelAt(1, 1);
        endpoint = LabelAt(1, 3);
        sessionDuration = LabelAt(1, 4);
        character = LabelAt(1, 6);
        vitals = LabelAt(1, 7);
        position = LabelAt(1, 8);
        feedback = new Label
        {
            X = 1,
            Y = 13,
            Width = Dim.Fill(1),
            Height = 2,
            SchemeName = "Base"
        };
        godMode = new DropDownList
        {
            X = 20,
            Y = 10,
            Width = 18,
            ReadOnly = true,
            Source = new ListWrapper<string>(new ObservableCollection<string>([GodModeDisabled, GodModeEnabled])),
            Text = GodModeDisabled,
            SchemeName = "Base"
        };
        godMode.ValueChanged += (_, _) =>
        {
            if (updatingGodModeSelection)
                return;

            godModeDirty = true;
            feedback.Text = $"God mode {godMode.Text}; click Apply to commit this player session.";
        };

        var apply = new Button { X = 20, Y = 12, Text = "Apply", SchemeName = "Base" };
        var close = new Button { X = 32, Y = 12, Text = "Close", SchemeName = "Base" };
        apply.Accepted += (_, _) => ApplyGodMode();
        close.Accepted += (_, _) => CloseRequested?.Invoke();

        Add(
            identity,
            endpoint,
            sessionDuration,
            character,
            vitals,
            position,
            new Label { X = 1, Y = 10, Text = "God mode", SchemeName = "Base" },
            godMode,
            apply,
            close,
            feedback);

        RefreshLiveState();
    }

    public event Action? CloseRequested;

    internal PlayerHandle Player => handle;
    internal bool GodModeForSmoke => IsGodModeSelected();
    internal string SessionTextForSmoke => sessionDuration.Text?.ToString() ?? string.Empty;
    internal string EndpointTextForSmoke => endpoint.Text?.ToString() ?? string.Empty;

    internal void RefreshLiveState()
    {
        bool connected = TryCaptureCurrentPlayer(out RuntimePlayerSnapshot player);
        identity.Text = $"Identity: #{initialPlayer.Slot} / generation {initialPlayer.Generation} / connection {initialPlayer.ConnectionId}";
        Title = $"Player #{initialPlayer.Slot} · {player.Name}";
        if (sessions.TryGet(initialPlayer.ConnectionId, out RuntimeConnectionSessionSnapshot session))
        {
            string remote = session.RemotePort > 0
                ? FormatEndpoint(session.RemoteAddress, session.RemotePort)
                : session.RemoteAddress;
            endpoint.Text = $"Remote: {remote}";
            TimeSpan elapsed = DateTimeOffset.UtcNow - session.ConnectedAtUtc;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
            sessionDuration.Text = $"Current session: {FormatDuration(elapsed)} (since {session.ConnectedAtUtc:yyyy-MM-dd HH:mm:ss} UTC)";
        }
        else
        {
            endpoint.Text = "Remote: disconnected / session metadata unavailable";
            sessionDuration.Text = "Current session: ended";
        }

        character.Text = $"Character difficulty: {FormatDifficulty(player.DifficultyFlags)}";
        vitals.Text = $"HP: {(player.HasHealth ? $"{player.Life}/{player.MaxLife}" : "--")}   Mana: {(player.HasMana ? $"{player.Mana}/{player.MaxMana}" : "--")}   Team: {player.Team}";
        position.Text = string.Create(CultureInfo.InvariantCulture,
            $"Position: {player.PositionX:F1}, {player.PositionY:F1}   Velocity: {player.VelocityX:F2}, {player.VelocityY:F2}   Item: {player.SelectedItem}");

        try
        {
            bool? enabled = administration.GetGodModeAsync(handle).AsTask().GetAwaiter().GetResult();
            if (!connected || enabled is null)
            {
                godMode.Enabled = false;
                godModeDirty = false;
                feedback.Text = "Player generation is no longer connected.";
            }
            else
            {
                godMode.Enabled = true;
                // Dashboard refreshes are periodic. Do not overwrite an operator selection that is
                // waiting for Apply, otherwise the control appears impossible to change.
                if (!godModeDirty)
                    SetGodModeSelection(enabled.Value);
                if (feedback.Text?.ToString() is not { Length: > 0 })
                    feedback.Text = "God mode is runtime-only and is not persisted.";
            }
        }
        catch (Exception exception)
        {
            godMode.Enabled = false;
            feedback.Text = $"God mode status unavailable: {exception.Message}";
        }
    }

    private bool TryCaptureCurrentPlayer(out RuntimePlayerSnapshot player)
    {
        ReadOnlySpan<RuntimePlayerSnapshot> current = players.CaptureSnapshot().Players.Span;
        for (int i = 0; i < current.Length; i++)
        {
            RuntimePlayerSnapshot candidate = current[i];
            if (candidate.ConnectionId == initialPlayer.ConnectionId &&
                candidate.Slot == initialPlayer.Slot &&
                candidate.Generation == initialPlayer.Generation)
            {
                player = candidate;
                return true;
            }
        }

        player = initialPlayer;
        return false;
    }

    private void ApplyGodMode()
    {
        bool enabled = IsGodModeSelected();
        try
        {
            bool applied = administration.SetGodModeAsync(handle, enabled).AsTask().GetAwaiter().GetResult();
            feedback.Text = applied
                ? $"God mode {(enabled ? "enabled" : "disabled")} for this exact player session."
                : "Player generation is no longer connected; no change was applied.";
            if (applied)
            {
                godModeDirty = false;
                SetGodModeSelection(enabled);
            }
            else
            {
                godModeDirty = false;
                godMode.Enabled = false;
            }
        }
        catch (Exception exception)
        {
            feedback.Text = $"God mode change failed: {exception.Message}";
        }
    }

    private bool IsGodModeSelected() =>
        string.Equals(godMode.Text, GodModeEnabled, StringComparison.Ordinal);

    private void SetGodModeSelection(bool enabled)
    {
        updatingGodModeSelection = true;
        try
        {
            godMode.Text = enabled ? GodModeEnabled : GodModeDisabled;
        }
        finally
        {
            updatingGodModeSelection = false;
        }
    }

    internal void SetGodModeForSmoke(bool enabled)
    {
        SetGodModeSelection(enabled);
        godModeDirty = true;
    }

    internal void ApplyGodModeForSmoke() => ApplyGodMode();

    internal static string FormatDifficulty(byte flags)
    {
        // TerrariaServer 1.4.5.8 MessageBuffer packet 4: bit0=mediumcore, bit1=hardcore, bit3=journey.
        if ((flags & 0b0000_1000) != 0)
            return "Journey";
        if ((flags & 0b0000_0010) != 0)
            return "Hardcore";
        if ((flags & 0b0000_0001) != 0)
            return "Mediumcore";
        return "Softcore";
    }

    private static string FormatDuration(TimeSpan value)
    {
        int hours = checked((int)Math.Floor(value.TotalHours));
        return string.Create(CultureInfo.InvariantCulture, $"{hours:D2}:{value.Minutes:D2}:{value.Seconds:D2}");
    }

    private static string FormatEndpoint(string address, int port) =>
        address.Contains(':', StringComparison.Ordinal) ? $"[{address}]:{port}" : $"{address}:{port}";

    private static Label LabelAt(int x, int y) => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(1),
        SchemeName = "Base"
    };
}
