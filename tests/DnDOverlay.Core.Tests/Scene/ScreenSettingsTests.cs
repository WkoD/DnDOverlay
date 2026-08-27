using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// The delta mechanism the two-sided configuration rests on. It has to be a delta because the
/// same value has two writers, and every one of these tests is about the difference between
/// "unchanged" and "cleared" (Part 4, Part 6).
/// </summary>
public sealed class ScreenSettingsTests
{
    private static readonly ScreenContext Context = ScreenContext.Default(new PixelSize(1920, 1080), 96);

    /// <summary>
    /// The heart of it: null means UNCHANGED. A full set in one direction would reset the other
    /// side's change without anybody ordering it.
    /// </summary>
    [Fact]
    public void What_a_delta_does_not_mention_it_does_not_touch()
    {
        var before = Context with { ParkEdge = ParkEdge.Left, ScaleOnLoad = 0.25, DefaultRotationDeg = 180 };

        var after = new ScreenSettings(ScaleOnLoad: 0.75).ApplyTo(before);

        Assert.Equal(0.75, after.ScaleOnLoad);
        Assert.Equal(ParkEdge.Left, after.ParkEdge);
        Assert.Equal(180, after.DefaultRotationDeg);
    }

    /// <summary>
    /// Size and DPI are hardware facts and are deliberately not in the settings at all - a device
    /// that could set them would be able to lie about its own monitor.
    /// </summary>
    [Fact]
    public void A_delta_cannot_touch_size_or_dpi()
    {
        var after = ScreenSettings.Of(ScreenContext.Default(new PixelSize(800, 600), 192), null).ApplyTo(Context);

        Assert.Equal(new PixelSize(1920, 1080), after.Size);
        Assert.Equal(96, after.Dpi);
    }

    [Fact]
    public void A_full_set_survives_being_laid_back_over_the_defaults()
    {
        var set = Context with
        {
            MinVisiblePixels = 120,
            MaxScale = 4,
            Placement = PlacementMode.Cascade,
            DefaultRotationDeg = 90,
            ParkEdge = ParkEdge.Top,
        };

        Assert.Equal(set, ScreenSettings.Of(set, null).ApplyTo(Context));
    }

    [Fact]
    public void A_diff_carries_only_what_moved()
    {
        var before = ScreenSettings.Of(Context, "Touch table");
        var after = ScreenSettings.Of(Context with { ParkEdge = ParkEdge.Bottom }, "Touch table");

        var delta = ScreenSettings.Diff(before, after);

        Assert.Equal(ParkEdge.Bottom, delta.ParkEdge);
        Assert.Null(delta.CustomName);
        Assert.Null(delta.ScaleOnLoad);
        Assert.Null(delta.Placement);
    }

    [Fact]
    public void A_diff_over_two_equal_sets_says_nothing()
    {
        var settings = ScreenSettings.Of(Context, "Touch table");

        Assert.True(ScreenSettings.Diff(settings, settings).IsEmpty);
    }

    /// <summary>
    /// A renamed screen is a change like any other - the name flows both ways because it may be
    /// given at the device as well (Part 6).
    /// </summary>
    [Fact]
    public void A_changed_name_is_part_of_the_delta()
    {
        var delta = ScreenSettings.Diff(
            ScreenSettings.Of(Context, null),
            ScreenSettings.Of(Context, "Touch table"));

        Assert.Equal("Touch table", delta.CustomName);
        Assert.False(delta.IsEmpty);
    }

    /// <summary>
    /// <b>Every parameter of a screen goes the whole way round</b>, driven off the type rather than
    /// listed by hand: a value is changed, taken to a full set, laid back over the defaults, and
    /// asked for as a delta. Four places have to know about a parameter -
    /// <see cref="ScreenSettings.Of"/>, <see cref="ScreenSettings.ApplyTo"/>,
    /// <see cref="ScreenSettings.Diff"/>, <see cref="ScreenSettings.IsEmpty"/> and
    /// <see cref="ScreenSettings.Merge"/> - and forgetting one of them does not fail anywhere. It
    /// drops a value silently.
    /// <para>
    /// <b>Five, not four, and the fifth was found by walking into it</b> (M3a): <c>Merge</c> lived
    /// in the hub, written out positionally, and had never heard of <c>ImageTextSize</c> - a
    /// parameter added a milestone earlier fell out of every merge of two pending deltas. The four
    /// places this test already held were all correct; the place it did not know about was not.
    /// </para>
    /// <para>
    /// <b>Measured, not foreseen:</b> the tests above set five of the eight parameters and would
    /// have passed with a sixth missing from all four - they compare a changed context against
    /// itself, so a parameter that never moved reads as correct. <c>ImageTextSize</c> was decided
    /// in M2 as "18 DIP, settable per screen", built as a constant, and recorded as a missing
    /// GRIP for a whole milestone; it was in fact a missing FIELD, and no test could have said so.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Parameters))]
    public void Every_parameter_of_a_screen_survives_the_whole_round_trip(string name)
    {
        var changed = Moved(Context, name);

        // Of and ApplyTo: the full set carries it out, and laying it back puts it there.
        Assert.Equal(changed, ScreenSettings.Of(changed, null).ApplyTo(Context));

        // Diff: it is reported as having moved, under its own name.
        var delta = ScreenSettings.Diff(ScreenSettings.Of(Context, null), ScreenSettings.Of(changed, null));
        var reported = typeof(ScreenSettings).GetProperty(name);

        Assert.NotNull(reported);
        Assert.NotNull(reported.GetValue(delta));

        // IsEmpty: a delta that carries this one is not empty. Without it a forgotten parameter
        // would be diffed correctly and then thrown away as "nothing to send".
        Assert.False(delta.IsEmpty, $"a delta carrying only {name} says it is empty");

        // Merge: laid over an empty older delta it survives, and it beats an older value for the
        // same key. Both directions, because dropping a field looks identical to "the older one
        // won" from the outside.
        Assert.NotNull(reported.GetValue(ScreenSettings.Merge(ScreenSettings.None, delta)));
        Assert.Equal(
            reported.GetValue(delta),
            reported.GetValue(ScreenSettings.Merge(ScreenSettings.Of(Context, null), delta)));
    }

    /// <summary>
    /// The settable parameters of a screen. <c>Size</c> and <c>Dpi</c> are hardware facts and
    /// deliberately absent from the settings; the computed properties have no setter and are not
    /// parameters at all.
    /// </summary>
    public static TheoryData<string> Parameters =>
        [.. typeof(ScreenContext)
            .GetProperties()
            .Where(property => property.CanWrite && property.Name is not ("Size" or "Dpi"))
            .Select(property => property.Name)];

    /// <summary>
    /// The same context with one parameter moved to a different value, whatever its type. Built
    /// through the primary constructor because a record's <c>with</c> cannot be reached by name.
    /// </summary>
    private static ScreenContext Moved(ScreenContext context, string name)
    {
        var constructor = typeof(ScreenContext)
            .GetConstructors()
            .MaxBy(candidate => candidate.GetParameters().Length)!;

        var arguments = constructor.GetParameters()
            .Select(parameter =>
            {
                var current = typeof(ScreenContext).GetProperty(parameter.Name!)!.GetValue(context);

                return string.Equals(parameter.Name, name, StringComparison.Ordinal)
                    ? Different(current!)
                    : current;
            })
            .ToArray();

        return (ScreenContext)constructor.Invoke(arguments);
    }

    /// <summary>Any value of the same type that is not the one given.</summary>
    private static object Different(object current) => current switch
    {
        double number => number + 7,
        int number => number + 90,
        bool value => !value,
        Enum value => Enum.GetValues(value.GetType())
            .Cast<Enum>()
            .First(candidate => !candidate.Equals(value)),
        _ => throw new NotSupportedException(
            $"a screen parameter of type {current.GetType().Name} has no 'different' value here - "
            + "add one, or this parameter is silently not being checked"),
    };

    /// <summary>
    /// <b>A screen that has no opinion writes nothing</b> - the state Part 6 carried as an open
    /// contradiction until the end of M3. Without it a corrected default reaches no machine that
    /// has ever run, because every known screen had the full set standing in its file as though
    /// somebody had chosen it (Guide G13).
    /// </summary>
    [Fact]
    public void A_screen_that_follows_every_default_has_nothing_to_say()
    {
        Assert.True(ScreenSettings.Opinion(Context, customName: null).IsEmpty);
    }

    /// <summary>
    /// And it says exactly what it does have an opinion about - one field, not the other fourteen.
    /// A test that only asked "not empty" would pass on the old behaviour too.
    /// </summary>
    [Fact]
    public void One_changed_value_is_the_only_thing_written()
    {
        var opinion = ScreenSettings.Opinion(Context with { ScaleOnLoad = 0.25 }, customName: null);

        Assert.Equal(0.25, opinion.ScaleOnLoad);
        Assert.Equal(new ScreenSettings(ScaleOnLoad: 0.25), opinion);
    }

    /// <summary>A name is an opinion like any other, and the only one that is not a number.</summary>
    [Fact]
    public void A_name_somebody_gave_the_screen_is_an_opinion()
    {
        Assert.Equal(new ScreenSettings(CustomName: "Tisch"), ScreenSettings.Opinion(Context, "Tisch"));
    }

    /// <summary>
    /// The round trip that makes the thinning safe: what is written, laid back over the defaults of
    /// a screen of that size, is the set it came from. Every field at a value of its own, so a
    /// field forgotten in Opinion cannot hide behind a default that happens to match.
    /// </summary>
    [Fact]
    public void What_is_written_lays_back_over_the_defaults_unchanged()
    {
        var mine = Context with
        {
            MinVisiblePixels = 111,
            MinScale = 0.11,
            MaxScale = 11,
            ScaleOnLoad = 0.11,
            MaxWidthOnLoad = 0.11,
            Placement = PlacementMode.Cascade,
            DefaultRotationDeg = 180,
            ParkEdge = ParkEdge.Top,
            ParkPushOutFraction = 0.11,
            ImageTextSize = 11,
            RotationDeadZoneDeg = 11,
            RotationSnapToleranceDeg = 11,
            ArrivalHighlightSeconds = 11,
            Inertia = false,
            ScrollUpZoomsIn = false,
        };

        var written = ScreenSettings.Opinion(mine, "Tisch");

        Assert.Equal(mine, written.ApplyTo(ScreenContext.Default(mine.Size, mine.Dpi)));
        Assert.Equal("Tisch", written.CustomName);
    }

    /// <summary>
    /// The price, written down as a test so nobody rediscovers it as a fault: a value deliberately
    /// set TO the default is indistinguishable from one nobody touched, so a later change of that
    /// default moves this screen too. Wanted behaviour at a table where every parameter is a
    /// starting point - but a choice, not an oversight.
    /// </summary>
    [Fact]
    public void A_value_set_to_the_default_is_not_told_apart_from_no_opinion()
    {
        Assert.True(ScreenSettings.Opinion(Context with { MaxScale = Context.MaxScale }, null).IsEmpty);
    }
}
