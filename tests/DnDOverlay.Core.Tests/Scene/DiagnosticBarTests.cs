namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// Where the diagnostic bar lies: where it was set, never on the fan, and opposite the fan when
/// nobody said (Part 6, step 37d).
/// </summary>
public sealed class DiagnosticBarTests
{
    private static readonly ScreenContext Context = ScreenContext.Default(new PixelSize(1920, 1080), 96);

    [Theory]
    [InlineData(ParkEdge.Right, ParkEdge.Left)]
    [InlineData(ParkEdge.Left, ParkEdge.Right)]
    [InlineData(ParkEdge.Top, ParkEdge.Bottom)]
    [InlineData(ParkEdge.Bottom, ParkEdge.Top)]
    public void Unset_it_lies_opposite_the_fan(ParkEdge park, ParkEdge bar)
    {
        Assert.Equal(bar, DiagnosticBar.EdgeOf(Context with { ParkEdge = park }));
    }

    [Fact]
    public void Set_it_lies_where_it_was_set()
    {
        Assert.Equal(ParkEdge.Top, DiagnosticBar.EdgeOf(Context with { ParkEdge = ParkEdge.Right, DiagnosticEdge = ParkEdge.Top }));
    }

    /// <summary>
    /// Setting it onto the fan is not possible, and a fan moved onto the bar pushes the bar away by
    /// itself - the parked pictures stay reachable while somebody looks for a problem.
    /// </summary>
    [Fact]
    public void It_never_lies_on_the_fan()
    {
        var set = Context with { ParkEdge = ParkEdge.Right, DiagnosticEdge = ParkEdge.Top };

        Assert.Equal(ParkEdge.Bottom, DiagnosticBar.EdgeOf(set with { ParkEdge = ParkEdge.Top }));
        Assert.Equal(ParkEdge.Left, DiagnosticBar.EdgeOf(Context with { ParkEdge = ParkEdge.Right, DiagnosticEdge = ParkEdge.Right }));
    }
}
