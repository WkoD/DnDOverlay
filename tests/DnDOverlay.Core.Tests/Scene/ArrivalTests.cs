using DnDOverlay.Core;

namespace DnDOverlay.Core.Tests.Scene;

/// <summary>
/// Which pictures light up. The whole difficulty is in the word "new": done naively the entire table
/// flashes after every reconnect and every scene change, and a hint that fires constantly stops
/// meaning anything (Part 6, Part 11 step 20a).
/// </summary>
public sealed class ArrivalTests
{
    private static SceneState Standing(params SceneItem[] items) => Build.SceneWith(items);

    [Fact]
    public void An_item_added_to_a_standing_scene_is_marked()
    {
        var standing = Standing(Build.Item(), Build.Item());
        var arriving = Build.Item();

        Assert.Equal([arriving.ItemId], Arrival.Marked(standing, [new AddItem(arriving)]));
    }

    /// <summary>
    /// A first picture on an empty screen is not lost among others, so nothing has to point at it -
    /// and it is how the rule is written: an <c>AddItem</c> into an ALREADY STANDING scene (Part 6).
    /// </summary>
    [Fact]
    public void The_first_item_on_an_empty_screen_is_not_marked()
    {
        Assert.Empty(Arrival.Marked(SceneState.Empty, [new AddItem(Build.Item())]));
    }

    /// <summary>
    /// The restore after a restart and the first fill after connecting both arrive as a
    /// <c>SceneSnapshot</c>, which is not a patch - so this function never sees them, and that is
    /// the whole reason the question is asked of a patch rather than of two states.
    /// </summary>
    [Fact]
    public void A_patch_that_does_more_than_add_marks_nothing()
    {
        var standing = Standing(Build.Item());
        var arriving = Build.Item();

        Assert.Empty(Arrival.Marked(
            standing,
            [new AddItem(arriving), new SetName(Build.Asset(), "Ratsherr Vellin")]));
    }

    /// <summary>
    /// <b>This is what tells the two kinds of scene loading apart</b>, without either being named in
    /// the rule: <i>add</i> is a patch of nothing but <c>AddItem</c> and marks what came, <i>replace</i>
    /// carries a clearing operation and marks nothing. The clearing operation arrives in M5b; the
    /// clause that covers it is already the one being tested here.
    /// </summary>
    [Fact]
    public void Adding_several_items_marks_exactly_those_that_came()
    {
        var standing = Standing(Build.Item());
        var first = Build.Item();
        var second = Build.Item();

        Assert.Equal(
            [first.ItemId, second.ItemId],
            Arrival.Marked(standing, [new AddItem(first), new AddItem(second)]));
    }

    /// <summary>
    /// A patch delivered twice after a reconnect would otherwise light up a picture that has been
    /// lying there all evening.
    /// </summary>
    [Fact]
    public void An_item_that_was_already_there_is_not_marked_again()
    {
        var lying = Build.Item();
        var standing = Standing(lying, Build.Item());

        Assert.Empty(Arrival.Marked(standing, [new AddItem(lying)]));
    }

    [Fact]
    public void An_empty_patch_marks_nothing()
    {
        Assert.Empty(Arrival.Marked(Standing(Build.Item()), []));
    }

    /// <summary>
    /// A picture moved from another screen DOES light up - on the target screen that patch is a
    /// plain <c>AddItem</c>. It is also the right answer: for the players at that table the picture
    /// is new, and where the DM took it from is not their question.
    /// </summary>
    [Fact]
    public void A_picture_arriving_from_another_screen_lights_up()
    {
        var standing = Standing(Build.Item());
        var moved = Build.Item();

        Assert.Equal([moved.ItemId], Arrival.Marked(standing, [new AddItem(moved)]));
    }
}
