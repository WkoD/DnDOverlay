using DnDOverlay.TestData;
using Xunit.Sdk;
using Xunit.v3;

// Once per run, and it applies the coder policy before the first generated file - the generator
// and the codec share this process too (Part 5, Part 10).
[assembly: AssemblyFixture(typeof(TestDataFixture))]

// Sequential for the same reason as in Imaging.Tests: ImageMagick's coder policy and its resource
// limits are PROCESS-WIDE state.
[assembly: Parallelization(Mode = ParallelMode.None)]
