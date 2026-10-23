using DnDOverlay.Core;
using Landing = DnDOverlay.Control.CarryLanding.Landing;

namespace DnDOverlay.Control.Tests;

/// <summary>What something carried out of the panel does where it is let go (Part 7).</summary>
public sealed class CarryLandingTests
{
    private static readonly ScreenRef Screen = new(new DeviceId(Guid.Parse("eeeeeeee-0000-0000-0000-0000000000a9")), new ScreenId("DISPLAY1"));
    private static readonly AssetRef Orc = Picture("Ork", 'a');
    private static readonly AssetRef Goblin = Picture("Goblin", 'c');

    /// <summary>A layout reaches the screens it names wherever over the stage it lands; off the stage it does nothing.</summary>
    [Fact]
    public void A_layout_loads_anywhere_over_the_stage_and_nowhere_else()
    {
        var load = new CarryLoad([], null, null, Layout: "Taverne");

        Assert.Equal(new Landing.Layout("Taverne"), CarryLanding.Decide(load, overStage: true, aim: null));
        Assert.IsType<Landing.Nothing>(CarryLanding.Decide(load, overStage: false, (Screen, null, false)));
    }

    /// <summary>Over no tile, nothing lands - the picture stays in the stock.</summary>
    [Fact]
    public void Over_no_tile_nothing_lands() =>
        Assert.IsType<Landing.Nothing>(CarryLanding.Decide(new CarryLoad([Orc], null, null), overStage: true, aim: null));

    /// <summary>A scene is loaded onto the tile under the hand - on the strip as well; it is not a picture.</summary>
    [Fact]
    public void A_scene_loads_onto_the_tile_under_the_hand() =>
        Assert.Equal(new Landing.Scene(Screen, "Ratsversammlung"), CarryLanding.Decide(new CarryLoad([], "Ratsversammlung", null), false, (Screen, new Point(0.5, 0.5), true)));

    /// <summary>The background strip takes exactly one picture; several are refused, none placed.</summary>
    [Fact]
    public void The_background_strip_takes_one_picture_and_refuses_several()
    {
        Assert.Equal(new Landing.Background(Screen, Orc), CarryLanding.Decide(new CarryLoad([Orc], null, null), false, (Screen, null, true)));
        Assert.Equal(new Landing.BackgroundRefused(Screen), CarryLanding.Decide(new CarryLoad([Orc, Goblin], null, null), false, (Screen, null, true)));
    }

    /// <summary>One picture lands where it was let go; several are one command laid side by side.</summary>
    [Fact]
    public void One_picture_lands_where_it_was_let_go_and_several_flow()
    {
        var at = new Point(0.2, 0.8);

        Assert.Equal(new Landing.Picture(Screen, Orc, at), CarryLanding.Decide(new CarryLoad([Orc], null, null), false, (Screen, at, false)));

        var several = Assert.IsType<Landing.Pictures>(CarryLanding.Decide(new CarryLoad([Orc, Goblin], null, null), false, (Screen, at, false)));

        Assert.Equal([Orc, Goblin], several.Assets);
    }

    private static AssetRef Picture(string name, char seed) =>
        new(new AssetId(new string(seed, 64)), new AssetMeta(800, 600, "png", 1024, IsAnimated: false, new string('b', 64)), name);
}
