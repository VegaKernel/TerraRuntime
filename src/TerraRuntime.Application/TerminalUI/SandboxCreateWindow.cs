using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

/// <summary>Operator form for typed sandbox creation.</summary>
internal sealed class SandboxCreateWindow : Window
{
    private const int SmallWorldWidth = 4200;
    private const int SmallWorldHeight = 1200;
    private const int MediumWorldWidth = 6400;
    private const int MediumWorldHeight = 1800;
    private const int LargeWorldWidth = 8400;
    private const int LargeWorldHeight = 2400;

    private static readonly string[] GameModes = ["Classic", "Expert", "Master", "Journey"];
    private static readonly string[] EvilTypes = ["Corruption", "Crimson"];

    private readonly SandboxOperations operations;
    private readonly TextField nameField;
    private readonly CheckBox inProcessIsolation;
    private readonly CheckBox dedicatedProcessIsolation;
    private readonly Label isolationSelection;
    private readonly CheckBox worldFile;
    private readonly TextField fileField;
    private readonly DropDownList generatorDropDown;
    private readonly string[] generatorIds;
    private readonly TextField seedField;
    private readonly Button randomSeedButton;
    private readonly DropDownList sizeDropDown;
    private readonly string[] sizeOptions;
    private readonly TextField widthField;
    private readonly TextField heightField;
    private readonly DropDownList modeDropDown;
    private readonly DropDownList evilDropDown;
    private readonly Label feedback;
    private bool updatingIsolation;

    public SandboxCreateWindow(SandboxOperations operations)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        Title = "Create sandbox";
        Width = 76;
        Height = 24;
        X = Pos.Center();
        Y = Pos.Center();
        SchemeName = "Base";

        nameField = Field(22, 1, 49, "sandbox");
        inProcessIsolation = IsolationCheckBox(22, 3, "In-process isolation (Level 1, same server process)", CheckState.Checked);
        dedicatedProcessIsolation = IsolationCheckBox(22, 4, "Dedicated-process isolation (Level 2, separate worker process)", CheckState.UnChecked);
        isolationSelection = new Label
        {
            X = 22,
            Y = 5,
            Width = 49,
            SchemeName = "Base"
        };

        worldFile = new CheckBox
        {
            X = 22,
            Y = 7,
            Text = "Load existing .wld file",
            SchemeName = "Base"
        };
        fileField = Field(22, 8, 49, "worlds/example.wld");

        generatorIds = operations.CaptureWorldGeneratorIds()
            .Select(static id => id.Value)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        int defaultGenerator = Array.FindIndex(
            generatorIds,
            static id => id.Equals("terraruntime:optimized", StringComparison.Ordinal));
        if (defaultGenerator < 0 && generatorIds.Length != 0)
            defaultGenerator = 0;
        generatorDropDown = DropDown(22, 10, 49, generatorIds, defaultGenerator);
        generatorDropDown.Enabled = generatorIds.Length != 0;

        seedField = Field(22, 11, 31, CreateRandomSeedText());
        randomSeedButton = new Button
        {
            X = 55,
            Y = 11,
            Text = "Random",
            SchemeName = "Base",
            NoDecorations = true
        };
        randomSeedButton.Accepted += (_, _) => seedField.Text = CreateRandomSeedText();

        sizeOptions =
        [
            $"Primary ({operations.DefaultWidthTiles}x{operations.DefaultHeightTiles})",
            $"Small ({SmallWorldWidth}x{SmallWorldHeight})",
            $"Medium ({MediumWorldWidth}x{MediumWorldHeight})",
            $"Large ({LargeWorldWidth}x{LargeWorldHeight})",
            "Custom"
        ];
        sizeDropDown = DropDown(22, 12, 32, sizeOptions, selectedIndex: 0);
        widthField = Field(22, 13, 12, operations.DefaultWidthTiles.ToString(CultureInfo.InvariantCulture));
        heightField = Field(48, 13, 12, operations.DefaultHeightTiles.ToString(CultureInfo.InvariantCulture));
        modeDropDown = DropDown(22, 14, 18, GameModes, selectedIndex: 0);
        evilDropDown = DropDown(50, 14, 18, EvilTypes, selectedIndex: 0);

        var create = new Button { X = 22, Y = 17, Text = "Create", SchemeName = "Base" };
        var cancel = new Button { X = 34, Y = 17, Text = "Cancel", SchemeName = "Base" };
        feedback = new Label { X = 1, Y = 19, Width = Dim.Fill(1), Height = 2, Text = string.Empty, SchemeName = "Base" };

        Add(
            LabelAt(1, 1, "Name"), nameField,
            LabelAt(1, 3, "Isolation"), inProcessIsolation,
            dedicatedProcessIsolation, isolationSelection,
            LabelAt(1, 7, "Source"), worldFile,
            LabelAt(1, 8, "World file"), fileField,
            LabelAt(1, 10, "Generator"), generatorDropDown,
            LabelAt(1, 11, "Seed"), seedField, randomSeedButton,
            LabelAt(1, 12, "Size preset"), sizeDropDown,
            LabelAt(1, 13, "Width"), widthField, LabelAt(40, 13, "Height"), heightField,
            LabelAt(1, 14, "Mode"), modeDropDown, LabelAt(43, 14, "Evil"), evilDropDown,
            create, cancel, feedback);

        inProcessIsolation.ValueChanged += (_, _) => KeepIsolationExclusive(inProcessIsolation, dedicatedProcessIsolation);
        dedicatedProcessIsolation.ValueChanged += (_, _) => KeepIsolationExclusive(dedicatedProcessIsolation, inProcessIsolation);
        worldFile.ValueChanged += (_, _) => UpdateEnabledFields();
        sizeDropDown.ValueChanged += (_, _) => ApplySelectedSize();
        create.Accepted += (_, _) => Submit();
        cancel.Accepted += (_, _) => CloseRequested?.Invoke();

        UpdateIsolationStatus();
        ApplySelectedSize();
        UpdateEnabledFields();
    }

    public event Action<SandboxOperation.Create>? CreateRequested;
    public event Action? CloseRequested;

    internal void SetFeedback(string text) => feedback.Text = text;

    private void Submit()
    {
        WorldIsolationLevel isolation = dedicatedProcessIsolation.Value == CheckState.Checked
            ? WorldIsolationLevel.DedicatedProcess
            : WorldIsolationLevel.InProcess;
        string name = nameField.Text.Trim();

        SandboxOperation.Create? operation;
        string? error;
        if (worldFile.Value == CheckState.Checked)
        {
            if (!operations.TryBuildWorldFileCreate(name, isolation, fileField.Text.Trim(), out operation, out error))
            {
                SetFeedback(error ?? "Invalid world file request.");
                return;
            }
        }
        else
        {
            if (generatorIds.Length == 0 || string.IsNullOrWhiteSpace(generatorDropDown.Text))
            {
                SetFeedback("No world generators are registered.");
                return;
            }
            if (!Enum.TryParse(modeDropDown.Text.Trim(), ignoreCase: true, out WorldGenerationGameMode mode) || !Enum.IsDefined(mode))
            {
                SetFeedback("Select a valid game mode.");
                return;
            }
            if (!Enum.TryParse(evilDropDown.Text.Trim(), ignoreCase: true, out WorldGenerationEvil evil) || !Enum.IsDefined(evil))
            {
                SetFeedback("Select a valid world evil.");
                return;
            }

            ResolveSelectedSize(out int? width, out int? height);
            if (sizeDropDown.SelectedItem == 4 &&
                (!int.TryParse(widthField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedWidth) || parsedWidth <= 0 ||
                 !int.TryParse(heightField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedHeight) || parsedHeight <= 0))
            {
                SetFeedback("Custom width and height must be positive integers.");
                return;
            }
            if (sizeDropDown.SelectedItem == 4)
            {
                width = int.Parse(widthField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
                height = int.Parse(heightField.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            }

            if (!operations.TryBuildGeneratedCreate(
                    name,
                    isolation,
                    generatorDropDown.Text.Trim(),
                    seedField.Text.Trim(),
                    width,
                    height,
                    mode,
                    evil,
                    out operation,
                    out error))
            {
                SetFeedback(error ?? "Invalid generation request.");
                return;
            }
        }

        if (operation is null)
        {
            SetFeedback("Sandbox request was not created.");
            return;
        }

        SetFeedback("Submitting sandbox request...");
        CreateRequested?.Invoke(operation);
    }

    private void KeepIsolationExclusive(CheckBox selected, CheckBox other)
    {
        if (updatingIsolation)
            return;

        updatingIsolation = true;
        try
        {
            if (selected.Value == CheckState.Checked)
                other.Value = CheckState.UnChecked;
            else if (other.Value != CheckState.Checked)
                selected.Value = CheckState.Checked;
        }
        finally
        {
            updatingIsolation = false;
        }

        UpdateIsolationStatus();
    }

    private void UpdateIsolationStatus()
    {
        bool dedicated = dedicatedProcessIsolation.Value == CheckState.Checked;
        isolationSelection.Text = dedicated
            ? "Selected: Dedicated-process isolation (Level 2)"
            : "Selected: In-process isolation (Level 1)";
    }

    private void ApplySelectedSize()
    {
        switch (sizeDropDown.SelectedItem)
        {
            case 0:
                widthField.Text = operations.DefaultWidthTiles.ToString(CultureInfo.InvariantCulture);
                heightField.Text = operations.DefaultHeightTiles.ToString(CultureInfo.InvariantCulture);
                break;
            case 1:
                widthField.Text = SmallWorldWidth.ToString(CultureInfo.InvariantCulture);
                heightField.Text = SmallWorldHeight.ToString(CultureInfo.InvariantCulture);
                break;
            case 2:
                widthField.Text = MediumWorldWidth.ToString(CultureInfo.InvariantCulture);
                heightField.Text = MediumWorldHeight.ToString(CultureInfo.InvariantCulture);
                break;
            case 3:
                widthField.Text = LargeWorldWidth.ToString(CultureInfo.InvariantCulture);
                heightField.Text = LargeWorldHeight.ToString(CultureInfo.InvariantCulture);
                break;
        }

        UpdateEnabledFields();
    }

    private void ResolveSelectedSize(out int? width, out int? height)
    {
        switch (sizeDropDown.SelectedItem)
        {
            case 0:
                width = null;
                height = null;
                return;
            case 1:
                width = SmallWorldWidth;
                height = SmallWorldHeight;
                return;
            case 2:
                width = MediumWorldWidth;
                height = MediumWorldHeight;
                return;
            case 3:
                width = LargeWorldWidth;
                height = LargeWorldHeight;
                return;
            default:
                width = null;
                height = null;
                return;
        }
    }

    private void UpdateEnabledFields()
    {
        bool file = worldFile.Value == CheckState.Checked;
        bool customSize = sizeDropDown.SelectedItem == 4;
        fileField.Enabled = file;
        generatorDropDown.Enabled = !file && generatorIds.Length != 0;
        seedField.Enabled = !file;
        randomSeedButton.Enabled = !file;
        sizeDropDown.Enabled = !file;
        widthField.Enabled = !file && customSize;
        heightField.Enabled = !file && customSize;
        modeDropDown.Enabled = !file;
        evilDropDown.Enabled = !file;
    }

    private static string CreateRandomSeedText()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes).ToString(CultureInfo.InvariantCulture);
    }

    private static CheckBox IsolationCheckBox(int x, int y, string text, CheckState value) => new()
    {
        X = x,
        Y = y,
        Width = 50,
        Text = text,
        Value = value,
        SchemeName = "Base"
    };

    private static Label LabelAt(int x, int y, string text) => new()
    {
        X = x,
        Y = y,
        Text = text,
        SchemeName = "Base"
    };

    private static TextField Field(int x, int y, int width, string text) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Text = text,
        SchemeName = "Base"
    };

    private static DropDownList DropDown(
        int x,
        int y,
        int width,
        IReadOnlyList<string> items,
        int selectedIndex)
    {
        var dropDown = new DropDownList
        {
            X = x,
            Y = y,
            Width = width,
            ReadOnly = true,
            Source = new ListWrapper<string>(new ObservableCollection<string>(items.ToArray())),
            SchemeName = "Base"
        };
        if (selectedIndex >= 0 && selectedIndex < items.Count)
        {
            dropDown.SelectedItem = selectedIndex;
            dropDown.Text = items[selectedIndex];
        }
        return dropDown;
    }
}
