using DnDOverlay.Core;
using DnDOverlay.Core.Protocol;
using DnDOverlay.Core.Tests.Configuration;
using Microsoft.Extensions.Options;

namespace DnDOverlay.Hub.Tests;

/// <summary>
/// The state machine of Part 4 on its own: <b>unknown - waiting - paired - rejected</b>, the four
/// inputs a <c>Hello</c> can carry, and the ways out again.
/// <para>
/// Without a socket, because none of this needs one. What does need one - that a refusal really
/// ends the connection, that a fresh token travels in the <c>Welcome</c> - is checked over the
/// wire next door.
/// </para>
/// </summary>
public sealed class PairingDirectoryTests
{
    private static readonly DeviceId Device = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void An_unknown_device_ends_up_waiting()
    {
        var directory = Directory();

        var admission = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.7"));

        Assert.True(admission.IsNew);

        var request = Assert.Single(directory.Pending);

        Assert.Equal("TISCH-PC", request.Name);
        Assert.Equal("4271", request.PairingCode);
        Assert.Equal("10.0.0.7", request.Address);
        Assert.False(request.IsClone);
        Assert.False(admission.Request.Decision.IsCompleted);
    }

    /// <summary>
    /// The point is not the count but the log line hanging off <c>IsNew</c>: an unpaired device on
    /// weak Wi-Fi comes back every few seconds, and the code survives that. Same code, same
    /// request, no second entry and no second line (Part 4).
    /// </summary>
    [Fact]
    public void A_second_hello_refreshes_the_request_instead_of_laying_a_second_one_beside_it()
    {
        var directory = Directory();

        var first = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.7"));
        var seen = first.Request.Snapshot.FirstSeen;

        var again = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.9"));

        Assert.False(again.IsNew);
        Assert.Same(first.Request, again.Request);
        Assert.Single(directory.Pending);
        Assert.Equal(seen, again.Request.Snapshot.FirstSeen);
        Assert.Equal("10.0.0.9", again.Request.Snapshot.Address);
    }

    [Fact]
    public void A_valid_token_goes_straight_in()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        var admission = Assert.IsType<Admission.Admitted>(directory.Consider(Hello(token: "s3cret"), "10.0.0.7"));

        Assert.Equal("TISCH-PC", admission.Device.Name);
        Assert.Empty(directory.Pending);
    }

    /// <summary>
    /// A token this control does not know is laid in front of the DM, not turned away - and the
    /// reason is that turning it away led nowhere. The way out it pointed at is a hand at the
    /// device, and after a replaced <c>control.json</c> that would be every display in the flat, on
    /// machines that have no keyboard (Part 4).
    /// <para>
    /// The gate does not move: nobody gets in without the DM either way. What changed is whether
    /// he is offered the decision at all.
    /// </para>
    /// </summary>
    [Fact]
    public void A_token_we_do_not_know_becomes_a_request_rather_than_a_refusal()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        var waiting = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(token: "guessed"), "10.0.0.7"));

        Assert.True(waiting.IsNew);
        Assert.Empty(directory.Refused);

        var pending = Assert.Single(directory.Pending);

        Assert.Equal("TISCH-PC", pending.Name);

        // Said in the row, because it changes what the DM is looking at - almost always his own
        // display after the control lost its file, not a stranger.
        Assert.True(pending.BroughtUnknownToken);

        // And the code travels even with a token, or he would be allowing a device by its name -
        // exactly what an impostor would supply.
        Assert.Equal("4271", pending.PairingCode);
    }

    /// <summary>
    /// The loosening above must not leak into the neighbours, so the three stand together: a
    /// device the DM turned away stays turned away even when it brings a token, and the gate
    /// "accept new devices" still holds.
    /// </summary>
    [Fact]
    public void An_unknown_token_does_not_reopen_what_the_DM_closed()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        _ = directory.Consider(Hello(), "10.0.0.7");
        directory.Reject(Device);

        var refused = Assert.IsType<Admission.Refused>(directory.Consider(Hello(token: "guessed"), "10.0.0.7"));

        Assert.Equal(RejectionReason.Denied, refused.Reason);
        Assert.Empty(directory.Pending);

        // And with the gate shut, a token gets no further than anything else.
        var open = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        open.AcceptNewDevices = false;

        Assert.IsType<Admission.Refused>(open.Consider(Hello(token: "guessed"), "10.0.0.7"));
        Assert.Empty(open.Pending);
    }

    [Fact]
    public async Task Approving_settles_the_waiting_connection_and_the_token_opens_the_next_one()
    {
        var directory = Directory();
        var waiting = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.7"));

        Assert.True(directory.Approve(Device, "fresh-token", PairingRole.Display));

        var decision = Assert.IsType<PairingDecision.Approved>(await waiting.Request.Decision);

        Assert.Equal("fresh-token", decision.Device.Token);
        Assert.Empty(directory.Pending);

        Assert.IsType<Admission.Admitted>(directory.Consider(Hello(token: "fresh-token"), "10.0.0.7"));
    }

    /// <summary>
    /// Once rejected, a device stays rejected until the DM takes it back - asking him again every
    /// five minutes would make the decision worthless (Part 4).
    /// </summary>
    [Fact]
    public async Task Rejecting_keeps_the_device_with_its_reason_and_does_not_ask_again()
    {
        var directory = Directory();
        var waiting = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.7"));

        Assert.True(directory.Reject(Device));

        var decision = Assert.IsType<PairingDecision.Refused>(await waiting.Request.Decision);

        Assert.Equal(RejectionReason.Denied, decision.Reason);
        Assert.Equal(RejectionReason.Denied, Assert.Single(directory.Refused).Reason);

        var again = Assert.IsType<Admission.Refused>(directory.Consider(Hello(), "10.0.0.7"));

        Assert.Equal(RejectionReason.Denied, again.Reason);
        Assert.Empty(directory.Pending);
    }

    /// <summary>
    /// The only way out of "rejected" the DM walks himself. Without it a mistaken no could only be
    /// healed at the device, on a machine that has no keyboard (Part 4).
    /// </summary>
    [Fact]
    public void A_rejection_can_be_taken_back()
    {
        var directory = Directory();

        _ = directory.Consider(Hello(), "10.0.0.7");
        _ = directory.Reject(Device);

        Assert.True(directory.ClearRejection(Device));
        Assert.Empty(directory.Refused);
        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.7"));
    }

    /// <summary>
    /// Nothing expires and nothing lingers: what stands in the list is what is knocking right now
    /// (Part 4).
    /// </summary>
    [Fact]
    public void A_connection_that_goes_away_leaves_nothing_behind()
    {
        var directory = Directory();
        var waiting = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(), "10.0.0.7"));

        directory.Withdraw(waiting.Request);

        Assert.Empty(directory.Pending);
        Assert.Empty(directory.Refused);
        Assert.False(waiting.Request.Decision.IsCompleted);
    }

    [Fact]
    public void Over_the_open_request_limit_further_devices_are_refused()
    {
        var directory = Directory(options: new HubOptions { MaxOpenPairingRequests = 2 });

        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(Some(1)), "10.0.0.1"));
        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(Some(2)), "10.0.0.2"));

        var refused = Assert.IsType<Admission.Refused>(directory.Consider(Hello(Some(3)), "10.0.0.3"));

        Assert.Equal(RejectionReason.LimitExceeded, refused.Reason);
        Assert.Equal(2, directory.Pending.Count);
    }

    /// <summary>
    /// Both flood vectors run through the same counter: many requests, and many token guesses.
    /// A display reconnecting with a VALID token never counts - only guessing does.
    /// </summary>
    [Fact]
    public void Too_many_attempts_from_one_address_are_refused()
    {
        var directory = Directory(options: new HubOptions { MaxPairingAttemptsPerAddressPerMinute = 3 });

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.IsType<Admission.Waiting>(directory.Consider(Hello(Some(attempt)), "10.0.0.7"));
        }

        var refused = Assert.IsType<Admission.Refused>(directory.Consider(Hello(Some(4)), "10.0.0.7"));

        Assert.Equal(RejectionReason.LimitExceeded, refused.Reason);
    }

    /// <summary>
    /// The direction that matters more than the refusal: it has to let go again. An unpaired
    /// display on weak Wi-Fi reaches twenty attempts by itself, and a window that never reset
    /// would lock it out for good - a false alarm costs more than the attack the limit guards
    /// against (Part 4).
    /// </summary>
    [Fact]
    public void The_attempt_window_lets_go_after_a_minute()
    {
        var time = new ManualTime();
        var directory = Directory(
            options: new HubOptions { MaxPairingAttemptsPerAddressPerMinute = 2 },
            time: time);

        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(Some(1)), "10.0.0.7"));
        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(Some(2)), "10.0.0.7"));
        Assert.IsType<Admission.Refused>(directory.Consider(Hello(Some(3)), "10.0.0.7"));

        time.Advance(TimeSpan.FromSeconds(61));

        Assert.IsType<Admission.Waiting>(directory.Consider(Hello(Some(3)), "10.0.0.7"));
    }

    /// <summary>
    /// A compromised display PC gets no authority over the session. The role is read from our own
    /// entry, never parsed out of what arrived over the wire (Part 4).
    /// </summary>
    [Fact]
    public void A_display_token_does_not_open_the_control_endpoint()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        Assert.True(directory.Authorises(Device, "s3cret", PairingRole.Display));
        Assert.False(directory.Authorises(Device, "s3cret", PairingRole.Control));
        Assert.False(directory.Authorises(Device, "guessed", PairingRole.Display));
    }

    [Fact]
    public void Not_accepting_new_devices_turns_them_away_without_asking()
    {
        var directory = Directory();
        directory.AcceptNewDevices = false;

        var refused = Assert.IsType<Admission.Refused>(directory.Consider(Hello(), "10.0.0.7"));

        Assert.Equal(RejectionReason.Denied, refused.Reason);
        Assert.Empty(directory.Pending);
    }

    [Fact]
    public void Unpairing_makes_the_token_worthless()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        Assert.True(directory.Unpair(Device));

        // It no longer opens anything - which is the point - and the device lands where an unknown
        // one lands: in front of the DM. Unpairing takes the token away, it does not ban the
        // device; banning is what rejecting is for (Part 4).
        var waiting = Assert.IsType<Admission.Waiting>(directory.Consider(Hello(token: "s3cret"), "10.0.0.7"));

        Assert.True(waiting.Request.Snapshot.BroughtUnknownToken);
        Assert.Empty(directory.Refused);
    }

    /// <summary>
    /// Cloning a disk is the usual way to set up a second display PC, so this is a normal path
    /// and not an incident: the clone is laid in front of the DM rather than turned away
    /// (Part 4, Part 7).
    /// </summary>
    [Fact]
    public async Task A_clone_is_told_to_take_a_fresh_identity()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        var request = directory.NoteClone(Hello(token: "s3cret"), "10.0.0.8");

        Assert.True(Assert.Single(directory.Pending).IsClone);
        Assert.True(directory.AcceptAsOwnDevice(Device));

        var decision = Assert.IsType<PairingDecision.Refused>(await request.Decision);

        Assert.Equal(RejectionReason.DuplicateDevice, decision.Reason);
        Assert.Empty(directory.Pending);
    }

    /// <summary>
    /// A rejected clone leaves NO entry behind, and that is not tidiness: the DeviceId belongs to
    /// the machine that is legitimately connected under it, so a refusal filed there would stand
    /// in the device list next to a device that is working perfectly well.
    /// </summary>
    [Fact]
    public void Rejecting_a_clone_does_not_blame_the_device_it_collided_with()
    {
        var directory = Directory(new PairedDevice(Device, "TISCH-PC", PairingRole.Display, "s3cret"));

        _ = directory.NoteClone(Hello(token: "s3cret"), "10.0.0.8");

        Assert.True(directory.Reject(Device));
        Assert.Empty(directory.Refused);
        Assert.IsType<Admission.Admitted>(directory.Consider(Hello(token: "s3cret"), "10.0.0.7"));
    }

    private static DeviceId Some(int number) =>
        new(new Guid($"00000000-0000-0000-0000-0000000000{number:00}"));

    private static HelloMessage Hello(DeviceId? device = null, string? token = null) =>
        new(device ?? Device,
            "TISCH-PC",
            "1.0.0",
            Protocol.Version,
            [new ScreenInfo(new ScreenId(@"\\?\DISPLAY#TEST#1"), "TISCH-PC//DISPLAY1", null, new PixelSize(1920, 1080), 96, true)],
            token,

            // The code travels ALWAYS, token or not: a device whose token this control does not
            // know is a pairing request now, and one the DM cannot compare with the table would
            // leave him allowing a device by its name (Part 4).
            "4271");

    private static PairingDirectory Directory(
        PairedDevice? paired = null,
        HubOptions? options = null,
        TimeProvider? time = null)
    {
        options ??= new HubOptions();

        if (paired is not null)
        {
            options.KnownDevices = [paired];
        }

        return new PairingDirectory(Options.Create(options), time ?? TimeProvider.System);
    }
}
