namespace DnDOverlay.Campaign.Tests;

/// <summary>Moving the campaign folder (Part 7, step 39g): renamed where possible, copied where necessary, all or nothing.</summary>
public sealed class CampaignMoveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dndoverlay-move-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string From => Path.Combine(_root, "old");

    private string To => Path.Combine(_root, "new");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The commonest case: the target does not exist and lies on the same volume - one rename of the whole folder.</summary>
    [Fact]
    public void A_new_place_on_the_same_volume_is_one_rename()
    {
        Campaign("Sturmküste", files: 3);
        Campaign("Vandalia", files: 2);

        var result = CampaignMove.Move(From, To, cancellationToken: Cancellation);

        Assert.False(result.Copied);
        Assert.False(Directory.Exists(From));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(To, "Sturmküste"), "*", SearchOption.AllDirectories).Length);
        Assert.True(Directory.Exists(Path.Combine(To, "Vandalia")));
    }

    /// <summary>Campaigns already at the target stay; the moved ones join them one by one.</summary>
    [Fact]
    public void An_existing_place_takes_each_campaign_in()
    {
        Campaign("Vandalia", files: 2);
        Directory.CreateDirectory(Path.Combine(To, "Tyranny"));

        CampaignMove.Move(From, To, cancellationToken: Cancellation);

        Assert.True(Directory.Exists(Path.Combine(To, "Vandalia")));
        Assert.True(Directory.Exists(Path.Combine(To, "Tyranny")));
    }

    /// <summary>A campaign of the same name at the target stops the move before it begins - with the name, and nothing touched.</summary>
    [Fact]
    public void A_name_at_the_target_stops_the_move_before_it_begins()
    {
        Campaign("Vandalia", files: 1);
        Directory.CreateDirectory(Path.Combine(To, "VANDALIA"));

        Assert.Equal(["Vandalia"], CampaignMove.Plan(From, To).Collisions);

        var refused = Assert.Throws<CampaignMoveException>(() => CampaignMove.Move(From, To, cancellationToken: Cancellation));

        Assert.Contains("Vandalia", refused.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(From, "Vandalia", "file0.bin")));
    }

    /// <summary>
    /// The system refuses the rename - a volume boundary: the campaigns are copied, checked, and the
    /// sources go only when asked to, after the new place is in use.
    /// </summary>
    [Fact]
    public void Across_a_volume_boundary_the_campaigns_are_copied()
    {
        Campaign("Vandalia", files: 4);

        var reports = new List<(long Done, long Total)>();
        var result = CampaignMove.Move(From, To, new Progress(reports), rename: Refuse, cancellationToken: Cancellation);

        Assert.True(result.Copied);
        Assert.True(Directory.Exists(Path.Combine(From, "Vandalia")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(From, "Vandalia", "scenes", "file1.bin")),
            File.ReadAllBytes(Path.Combine(To, "Vandalia", "scenes", "file1.bin")));
        Assert.Equal(reports[^1].Total, reports[^1].Done);

        Assert.Empty(CampaignMove.RemoveSources(result));
        Assert.False(Directory.Exists(Path.Combine(From, "Vandalia")));
    }

    /// <summary>A copy that fails half-way takes its copies away; the old place is whole and unchanged.</summary>
    [Fact]
    public void A_copy_that_fails_leaves_nothing_behind()
    {
        Campaign("Vandalia", files: 2);
        Campaign("Sturmküste", files: 3);

        var copies = 0;

        Assert.Throws<CampaignMoveException>(() => CampaignMove.Move(
            From,
            To,
            rename: Refuse,
            copyFile: (source, target) =>
            {
                if (++copies == 4)
                {
                    throw new IOException("disk gone");
                }

                File.Copy(source, target);
            },
            cancellationToken: Cancellation));

        Assert.Empty(Directory.GetDirectories(To));
        Assert.Equal(2, Directory.GetDirectories(From).Length);
    }

    /// <summary>Broken off by the DM: the same as a failure - nothing at the new place, everything at the old.</summary>
    [Fact]
    public void A_copy_broken_off_leaves_nothing_behind()
    {
        Campaign("Vandalia", files: 3);

        using var breaking = new CancellationTokenSource();
        var copies = 0;

        Assert.ThrowsAny<OperationCanceledException>(() => CampaignMove.Move(
            From,
            To,
            cancellationToken: breaking.Token,
            rename: Refuse,
            copyFile: (source, target) =>
            {
                File.Copy(source, target);

                if (++copies == 2)
                {
                    breaking.Cancel();
                }
            }));

        Assert.Empty(Directory.GetDirectories(To));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(From, "Vandalia"), "*", SearchOption.AllDirectories).Length);
    }

    /// <summary>Not room for the campaigns lying there twice: refused before a single byte is copied.</summary>
    [Fact]
    public void Without_room_for_the_copy_nothing_begins()
    {
        Campaign("Vandalia", files: 2);

        Assert.Throws<CampaignMoveException>(() => CampaignMove.Move(From, To, rename: Refuse, freeSpace: _ => 1024, cancellationToken: Cancellation));

        Assert.False(Directory.Exists(Path.Combine(To, "Vandalia")));
    }

    /// <summary>The deeper the new place, the less room for names - the question warns below the line.</summary>
    [Fact]
    public void A_deep_place_leaves_little_room_for_names()
    {
        var shallow = CampaignMove.Plan(From, @"D:\Kampagnen");
        var deep = CampaignMove.Plan(From, @"D:\" + string.Join('\\', Enumerable.Repeat("verschachtelt", 12)));

        Assert.True(shallow.RoomForNames > MovePlan.TightRoom);
        Assert.True(deep.RoomForNames < MovePlan.TightRoom);
    }

    private void Campaign(string name, int files)
    {
        var folder = Path.Combine(From, name);

        Directory.CreateDirectory(Path.Combine(folder, "scenes"));

        for (var index = 0; index < files; index++)
        {
            var sub = index % 2 == 0 ? folder : Path.Combine(folder, "scenes");

            File.WriteAllBytes(Path.Combine(sub, $"file{index}.bin"), [.. Enumerable.Repeat((byte)(index + 1), 100 + index)]);
        }
    }

    private static void Refuse(string from, string to) => throw new IOException("not the same device");

    /// <summary>Synchronous progress - <see cref="Progress{T}"/> posts to a context and would report after the test looked.</summary>
    private sealed class Progress(List<(long Done, long Total)> reports) : IProgress<(long Done, long Total)>
    {
        public void Report((long Done, long Total) value) => reports.Add(value);
    }
}
