using Xunit;

namespace TerraRuntime.Tests.GemStaffContacts;

public sealed class OwnedGemStaffNpcContact1458Tests
{
    [Fact]
    public void Source_ordered_live_contacts_preserve_birth_damage_local_immunity_and_kill_area_currentness() =>
        GemStaffContact1458Checks.Run();
}
