using System.Globalization;
using TerraRuntime.Operations;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

/// <summary>Small operator surface for live host settings that are safe to change without mutating game state.</summary>
internal sealed class RuntimeSettingsWindow : Window
{
    private readonly IRuntimeDashboardOperations operations;
    private readonly TextField bindAddressField;
    private readonly TextField portField;
    private readonly CheckBox interestManagement;
    private readonly Label endpointStatus;
    private readonly Label listenerStatus;
    private readonly Label connectionStatus;
    private readonly Label feedback;
    private RuntimeDashboardSnapshot openedSnapshot;

    public RuntimeSettingsWindow(IRuntimeDashboardOperations operations)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        Title = "Runtime settings";
        Width = 74;
        Height = 18;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        openedSnapshot = operations.CaptureSnapshot();
        bindAddressField = new TextField
        {
            X = 22,
            Y = 2,
            Width = 45,
            Text = openedSnapshot.BindAddress,
            SchemeName = "Base"
        };
        portField = new TextField
        {
            X = 22,
            Y = 4,
            Width = 12,
            Text = openedSnapshot.Port.ToString(CultureInfo.InvariantCulture),
            SchemeName = "Base"
        };
        interestManagement = new CheckBox
        {
            X = 22,
            Y = 9,
            Text = "Enabled",
            Value = openedSnapshot.InterestManagementEnabled ? CheckState.Checked : CheckState.UnChecked,
            SchemeName = "Base"
        };
        endpointStatus = new Label { X = 1, Y = 1, Width = Dim.Fill(1), SchemeName = "Accent" };
        listenerStatus = new Label { X = 1, Y = 6, Width = Dim.Fill(1), SchemeName = "Base" };
        connectionStatus = new Label { X = 1, Y = 7, Width = Dim.Fill(1), SchemeName = "Base" };
        feedback = new Label { X = 1, Y = 13, Width = Dim.Fill(1), Height = 2, SchemeName = "Base" };

        var apply = new Button { X = 22, Y = 11, Text = "Apply", SchemeName = "Base" };
        var close = new Button { X = 34, Y = 11, Text = "Close", SchemeName = "Base" };
        apply.Accepted += (_, _) => Apply();
        close.Accepted += (_, _) => CloseRequested?.Invoke();

        Add(
            endpointStatus,
            LabelAt(1, 2, "Bind address / IP"), bindAddressField,
            LabelAt(1, 4, "Port"), portField,
            listenerStatus,
            connectionStatus,
            LabelAt(1, 9, "Interest management"), interestManagement,
            apply, close, feedback);

        RefreshStatus(openedSnapshot);
    }

    public event Action? CloseRequested;

    internal string BindAddressForSmoke => bindAddressField.Text.Trim();
    internal string PortForSmoke => portField.Text.Trim();
    internal string ListenerStatusForSmoke => listenerStatus.Text?.ToString() ?? string.Empty;

    internal void SetEndpointForSmoke(string bindAddress, string port)
    {
        bindAddressField.Text = bindAddress;
        portField.Text = port;
    }

    internal void ApplyForSmoke() => Apply();

    internal string FeedbackForSmoke => feedback.Text?.ToString() ?? string.Empty;

    private void Apply()
    {
        string bindAddress = bindAddressField.Text.Trim();
        if (!int.TryParse(portField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < 1 or > ushort.MaxValue)
        {
            feedback.Text = $"Port must be an integer from 1 to {ushort.MaxValue}.";
            return;
        }

        ListenerChangeResult listenerChange = operations.TryChangeListenerEndpoint(bindAddress, port);
        if (!listenerChange.Success)
        {
            feedback.Text = listenerChange.Message;
            return;
        }

        bool requestedInterest = interestManagement.Value == CheckState.Checked;
        string interestResult = string.Empty;
        if (requestedInterest != openedSnapshot.InterestManagementEnabled)
        {
            interestResult = operations.TrySetInterestManagementEnabled(requestedInterest)
                ? $" Interest management {(requestedInterest ? "change queued: ON." : "change queued: OFF.")}"
                : " Interest-management command was rejected.";
        }

        openedSnapshot = operations.CaptureSnapshot();
        // Snapshot caches refresh asynchronously. Reflect the successful requested endpoint immediately while the
        // next detached operations snapshot catches up.
        endpointStatus.Text = $"Endpoint: {FormatEndpoint(bindAddress, port)}";
        listenerStatus.Text =
            $"Listener: {openedSnapshot.ListenerState} | generation {openedSnapshot.ListenerGeneration} | " +
            $"draining {openedSnapshot.DrainingListeners} | rebinds {openedSnapshot.ListenerRebinds}";
        connectionStatus.Text =
            $"Connections: {openedSnapshot.ActiveConnections}/{openedSnapshot.MaxPlayers} active";
        feedback.Text = listenerChange.Message + interestResult;
    }

    private void RefreshStatus(RuntimeDashboardSnapshot snapshot)
    {
        endpointStatus.Text = $"Endpoint: {FormatEndpoint(snapshot.BindAddress, snapshot.Port)}";
        listenerStatus.Text =
            $"Listener: {snapshot.ListenerState} | generation {snapshot.ListenerGeneration} | " +
            $"draining {snapshot.DrainingListeners} | rebinds {snapshot.ListenerRebinds}";
        connectionStatus.Text =
            $"Connections: {snapshot.ActiveConnections}/{snapshot.MaxPlayers} active | target {snapshot.TargetTicksPerSecond} TPS";
        feedback.Text = "Changing bind/port preserves already accepted client connections.";
    }

    private static string FormatEndpoint(string bindAddress, int port) =>
        bindAddress.Contains(':', StringComparison.Ordinal) ? $"[{bindAddress}]:{port}" : $"{bindAddress}:{port}";

    private static Label LabelAt(int x, int y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = "Base"
    };
}
