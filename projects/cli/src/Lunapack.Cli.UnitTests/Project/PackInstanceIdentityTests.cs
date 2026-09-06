using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Project;

public sealed class PackInstanceIdentityTests
{
    [Test]
    public async Task InstanceIdentity_WhenNameOmitted_UsesPackId()
    {
        var requestedPack = new ProjectConfiguration.RequestedPack { Id = "dotnet-api" };

        await Assert
            .That(requestedPack.GetInstanceIdentity())
            .IsEqualTo(new PackInstanceIdentity("dotnet-api", "dotnet-api"));
    }

    [Test]
    public async Task Equality_WhenAliasCaseDiffers_TreatsInstancesAsDistinct()
    {
        var lowerCase = new PackInstanceIdentity("dotnet-api", "orders");
        var upperCase = new PackInstanceIdentity("dotnet-api", "Orders");

        await Assert.That(lowerCase).IsNotEqualTo(upperCase);
    }
}
