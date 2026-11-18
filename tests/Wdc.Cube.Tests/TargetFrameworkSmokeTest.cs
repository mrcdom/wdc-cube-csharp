using System.Runtime.InteropServices;

namespace Wdc.Cube.Tests;

/// <summary>
/// Garante que a suíte realmente executa em cada alvo declarado (net10.0 e net11.0),
/// e não só no runtime mais novo.
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
