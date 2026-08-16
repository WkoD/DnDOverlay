namespace DnDOverlay.Rendering.Windows.Tests;

/// <summary>
/// The caption cascade: wrap, shorten, drop - each step only once the one before it stops carrying
/// (<c>checks/M1.md</c>). Measured against real text metrics, because "how many characters fit"
/// depends on which characters they are.
/// </summary>
public sealed class CaptionLayoutTests
{
    /// <summary>A picture large enough that a short name is never the interesting case.</summary>
    private const double Width = 400;
    private const double Height = 600;

    [Fact]
    public void A_name_that_fits_is_drawn_whole()
    {
        var caption = CaptionLayout.Fit("Grimmbart", Width, Height);

        Assert.Equal("Grimmbart", caption.Text);
        Assert.True(caption.IsVisible);
        Assert.True(caption.Height > 0);
    }

    /// <summary>
    /// The caption lies inside the picture and never takes more than a third of it. A portrait is
    /// there to be recognised.
    /// </summary>
    [Theory]
    [InlineData("Grimmbart")]
    [InlineData("Ratsherr Vellin von der Sturmküste")]
    [InlineData("Ein ausgesprochen langer Name, wie ihn nur ein Spieler vergibt, der die Eingabemaske testen will")]
    public void Nothing_ever_takes_more_than_a_third_of_the_picture(string name)
    {
        var caption = CaptionLayout.Fit(name, Width, Height);

        Assert.True(
            caption.Height <= (Height * CaptionLayout.MaxHeightFraction) + 1e-9,
            $"{caption.Height} dip of {Height} is past the third");
    }

    /// <summary>
    /// The middle step, and the one that has to be shown rather than assumed: a name too long to
    /// wrap into a third comes back shortened, with the ellipsis, and shorter than it went in.
    /// </summary>
    [Fact]
    public void A_name_too_long_to_wrap_is_shortened_with_an_ellipsis()
    {
        const string Name = "Ratsherr Vellin von der Sturmküste, Zweiter seines Namens und Hüter der Hafenkette";

        // A picture narrow enough that the name cannot wrap into a third of it.
        var caption = CaptionLayout.Fit(Name, widthInDip: 120, heightInDip: 120);

        Assert.True(caption.IsVisible);
        Assert.EndsWith("…", caption.Text, StringComparison.Ordinal);
        Assert.True(caption.Text.Length < Name.Length);
    }

    /// <summary>
    /// The last step. Below a handful of characters the caption is dropped rather than shortened
    /// further: <c>H…</c> covers a third of the figure and says nothing, which is worse than no
    /// caption at all.
    /// </summary>
    [Fact]
    public void A_picture_too_small_for_a_useful_caption_gets_none()
    {
        var caption = CaptionLayout.Fit("Ratsherr Vellin von der Sturmküste", widthInDip: 24, heightInDip: 24);

        Assert.False(caption.IsVisible);
        Assert.Empty(caption.Text);
    }

    /// <summary>
    /// Whatever survives is at least worth reading - the promise the threshold exists for. Checked
    /// across a range of sizes rather than at one, because the step where it bites depends on the
    /// picture.
    /// <para>
    /// <b>Against the literal 5, not against the constant.</b> Reading
    /// <see cref="CaptionLayout.MinimumCharacters"/> here would make the assertion move with the
    /// code it is meant to hold - measured, not supposed: with the constant lowered to 1 this test
    /// passed unchanged, which made it a tautology (Guide <c>C2</c>, <c>G8</c>).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(160)]
    [InlineData(320)]
    public void What_survives_is_never_a_stub(double size)
    {
        var caption = CaptionLayout.Fit("Ratsherr Vellin von der Sturmküste", size, size);

        if (caption.IsVisible)
        {
            Assert.True(caption.Text.Length >= 5, $"'{caption.Text}' is a stub");
        }
    }

    /// <summary>
    /// The size at which the THRESHOLD is what decides, rather than "nothing fits at all". The
    /// picture is tall enough for exactly one line and narrow enough that only a stub would fit on
    /// it - a shorter cut would still be drawable, and it is dropped anyway.
    /// <para>
    /// This is the case the range test above cannot make: there, every size either fits a real name
    /// or fits nothing, so the threshold never gets a turn.
    /// </para>
    /// </summary>
    [Fact]
    public void A_stub_that_would_fit_is_still_dropped()
    {
        const string Name = "Ratsherr Vellin von der Sturmküste";

        // Measured rather than guessed: at 56 x 72 one line of 18 DIP text fits, and the shortest
        // cut the threshold allows - "Ratsh…" - does not. The first attempt used numbers worked out
        // on paper and landed in the range where nothing fits at all, which proves nothing.
        var caption = CaptionLayout.Fit(Name, widthInDip: 56, heightInDip: 72);

        Assert.False(caption.IsVisible);

        // The counter-check that says the size was chosen right rather than luckily: a name short
        // enough to be worth reading DOES fit here, so the picture is not simply too small.
        Assert.True(CaptionLayout.Fit("Vell", widthInDip: 56, heightInDip: 72).IsVisible);
    }

    /// <summary>
    /// The size is settable because of the viewing distance, which the DPI knows nothing about -
    /// the table is an arm away, the projector three metres. So it has to actually do something.
    /// </summary>
    [Fact]
    public void A_larger_text_size_makes_a_taller_caption()
    {
        var small = CaptionLayout.Fit("Grimmbart", Width, Height, textSize: 12);
        var large = CaptionLayout.Fit("Grimmbart", Width, Height, textSize: 36);

        Assert.True(large.Height > small.Height, $"{large.Height} is not taller than {small.Height}");
    }

    /// <summary>An item without a name draws nothing, and that is not a special case.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_name_means_no_caption(string? name)
    {
        Assert.False(CaptionLayout.Fit(name, Width, Height).IsVisible);
    }
}
