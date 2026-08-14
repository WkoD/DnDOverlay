using DnDOverlay.TestData;

// Once per run, not once per class: parallel collections would otherwise build the same files at
// the same time (Part 10). The fixture applies the coder policy before the first generated file.
[assembly: AssemblyFixture(typeof(TestDataFixture))]
