using System.Collections.ObjectModel;
using System.Text;

namespace TerraRuntime.World;

/// <summary>The canonical SaveWorldHeader Angler quest and names completed during the current day.</summary>
public sealed class WorldAnglerQuestData1458
{
    public const int MaximumCompletedNames = 4_096;
    public const int MaximumNameBytes = 4_096;
    public static WorldAnglerQuestData1458 Empty { get; } = new(0, []);

    public WorldAnglerQuestData1458(int quest, IEnumerable<string> completedNames)
    {
        ArgumentNullException.ThrowIfNull(completedNames);
        if ((uint)quest >= 41) throw new ArgumentOutOfRangeException(nameof(quest));
        var names = new List<string>();
        foreach (string name in completedNames)
        {
            if (name is null || Encoding.UTF8.GetByteCount(name) > MaximumNameBytes ||
                names.Count >= MaximumCompletedNames)
                throw new ArgumentException("Angler completion names exceed the bounded world representation.", nameof(completedNames));
            names.Add(name);
        }
        Quest = quest;
        CompletedNames = names.AsReadOnly();
    }

    public int Quest { get; }
    public ReadOnlyCollection<string> CompletedNames { get; }
}
