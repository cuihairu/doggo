using Doggo.Domain;

namespace Doggo.Domain.Tests;

public class DomainSmokeTests
{
    [Fact]
    public void Domain_assembly_is_loadable()
    {
        Assert.Equal("Doggo.Domain", DomainMarker.Name);
    }
}
