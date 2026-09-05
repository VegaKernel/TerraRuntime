using TerraRuntime.HostContracts.TerminalUI;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Extensibility;

internal sealed class TrustedHostModuleHealthDashboardProvider : IDashboardProvider
{
    internal const string DashboardId = "terraruntime.hostmodules.health";

    private readonly Func<ReadOnlyMemory<TrustedHostModuleFault>> captureFaults;

    public TrustedHostModuleHealthDashboardProvider(
        Func<ReadOnlyMemory<TrustedHostModuleFault>> captureFaults)
    {
        this.captureFaults = captureFaults ?? throw new ArgumentNullException(nameof(captureFaults));
    }

    public string Id => DashboardId;
    public string Title => "Host Module Health";

    public View CreateDashboard() => new HealthView(captureFaults);

    public void Refresh(View rootView)
    {
        ArgumentNullException.ThrowIfNull(rootView);
        if (rootView is not HealthView healthView)
            throw new ArgumentException("Unexpected host-module health dashboard root.", nameof(rootView));

        healthView.RefreshFaults();
    }

    private sealed class HealthView : View
    {
        private readonly Func<ReadOnlyMemory<TrustedHostModuleFault>> captureFaults;
        private readonly Label summary;

        public HealthView(Func<ReadOnlyMemory<TrustedHostModuleFault>> captureFaults)
        {
            this.captureFaults = captureFaults;
            Width = Dim.Fill();
            Height = Dim.Fill();
            summary = new Label
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(2),
                Height = Dim.Fill(2)
            };
            Add(summary);
            RefreshFaults();
        }

        public void RefreshFaults()
        {
            TrustedHostModuleFault[] faults = captureFaults().ToArray();
            if (faults.Length == 0)
            {
                summary.Text = "Trusted host modules: healthy\nContained faults: 0";
                return;
            }

            TrustedHostModuleFault latest = faults[^1];
            summary.Text =
                $"Trusted host modules: degraded\n" +
                $"Contained faults: {faults.Length}\n" +
                $"Latest: {latest.FileName} / {latest.ModuleName ?? "unknown"}\n" +
                $"Phase: {latest.Phase}; required: {latest.Required}\n" +
                $"Exception: {latest.ExceptionType}\n" +
                $"Message: {latest.Message}\n" +
                $"UTC: {latest.TimestampUtc:O}";
        }
    }
}
