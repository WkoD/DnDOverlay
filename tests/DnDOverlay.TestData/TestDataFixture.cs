using DnDOverlay.Imaging;

namespace DnDOverlay.TestData;

/// <summary>
/// Builds the stock once per test run, not once per class - parallel collections would otherwise
/// write the same files at the same time (Part 10). Each test project points it at its own
/// <c>TestData/</c>, and both are ignored and rebuilt on every run.
/// <para>
/// The same fixture applies the coder policy, and it does so BEFORE the first generated file. That
/// order is the whole point: generator and codec share one process here, the policy is
/// process-wide, and applied late it silently does nothing at all (Part 5,
/// <see cref="CoderPolicy"/>).
/// </para>
/// </summary>
public sealed class TestDataFixture
{
    /// <summary>
    /// Applies the policy and builds the stock into the <c>TestData</c> folder next to the test
    /// assembly - which is why each test project gets its own without having to say so.
    /// <para>
    /// Exactly one public constructor, and it takes nothing: an assembly fixture is built by the
    /// runner, which allows no second one. Anyone needing a different directory calls
    /// <see cref="TestAssets.Build"/> directly.
    /// </para>
    /// </summary>
    public TestDataFixture()
    {
        CoderPolicy.Apply();
        Assets = TestAssets.Build(Path.Combine(AppContext.BaseDirectory, "TestData"));
    }

    /// <summary>What the run produced.</summary>
    public TestAssetSet Assets { get; }
}
