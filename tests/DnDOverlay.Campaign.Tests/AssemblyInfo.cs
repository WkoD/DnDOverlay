using DnDOverlay.TestData;
using Xunit.Sdk;
using Xunit.v3;

// Campaign.Tests needs the generated stock too - here for the thumbnail path, which is only worth
// anything against a real codec, and from M5b on for the folder reconciliation (Part 2). The
// fixture applies the coder policy before the first generated file.
[assembly: AssemblyFixture(typeof(TestDataFixture))]

// Sequential, for the same reason as in Imaging.Tests: the coder policy and the resource limits
// are PROCESS-WIDE state in ImageMagick (Part 5).
[assembly: Parallelization(Mode = ParallelMode.None)]
