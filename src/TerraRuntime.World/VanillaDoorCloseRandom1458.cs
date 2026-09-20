namespace TerraRuntime.World;

/// <summary>
/// The three source draws made by <c>WorldGen.CloseDoor</c> while selecting a closed-door frame column.
/// Keeping this dependency explicit makes the 1.4.5.8 call order testable without coupling world mutation to a
/// process-global random source.
/// </summary>
public interface IVanillaDoorCloseRandom1458
{
    int NextClosedDoorFrameColumn();
}
