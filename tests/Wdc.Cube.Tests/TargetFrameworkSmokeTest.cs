using System.Runtime.InteropServices;

namespace Wdc.Cube.Tests;

/// <summary>
/// Ensures the suite really runs on each declared target (net10.0 and net11.0),
/// not only on the newest runtime.
/// </summary>
public sealed class TargetFrameworkSmokeTest
{
    [Fact]
    public void RunsOnTheRuntimeOfItsTargetFramework()
    {
#if NET11_0
        const int expectedMajor = 11;
#elif NET10_0
        const int expectedMajor = 10;
#endif
        Assert.Equal(expectedMajor, Environment.Version.Major);
        Assert.StartsWith($".NET {expectedMajor}.", RuntimeInformation.FrameworkDescription, StringComparison.Ordinal);
    }
}
