using DnDOverlay.TestData;
using Xunit.Sdk;
using Xunit.v3;

// Once per run, not once per class: parallel collections would otherwise build the same files at
// the same time (Part 10). The fixture applies the coder policy before the first generated file.
[assembly: AssemblyFixture(typeof(TestDataFixture))]

// Sequential, and for a reason rather than out of caution: ImageMagick's coder policy AND its
// resource limits are PROCESS-WIDE state. The test that proves the second net has to set those
// limits deliberately small, and in parallel it would be setting them for everyone else at the
// same time (Part 5). The suite runs in a fraction of a second, so the price is nothing.
[assembly: Parallelization(Mode = ParallelMode.None)]
