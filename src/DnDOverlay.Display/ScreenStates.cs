using DnDOverlay.Core;

namespace DnDOverlay.Display;

/// <summary>
/// How each screen of this device stands: the DM's wish from the control, and the local finding laid
/// over it - hidden at the device with the rescue mark. It answers the one question the windows
/// follow, whether a screen carries an overlay, so "suppressed behaves like inactive" is a property
/// of one rule rather than something somebody has to remember (Part 3).
/// <para>
/// <b>A finding is not a state.</b> A suppression from the control and a hiding at the device both
/// take the overlay away and leave the wish untouched underneath; when the finding falls away the
/// window is simply back, in the state that stood there all along (Part 3, Part 6).
/// </para>
/// <para>
/// UI thread only, like the windows it decides about.
/// </para>
/// </summary>
internal sealed class ScreenStates
{
    private static readonly ScreenCommand Start = new(ScreenState.Inactive);

    private readonly Dictionary<ScreenId, ScreenCommand> _wishes = [];

    /// <summary>
    /// The screens hidden at the device with the rescue mark - the local finding, kept apart from the
    /// control's, which only the control may lift (Part 3).
    /// </summary>
    private readonly HashSet<ScreenId> _hiddenHere = [];

    /// <summary>How many screens have a wish - every screen Windows has offered in this run.</summary>
    internal int Count => _wishes.Count;

    /// <summary>
    /// A screen Windows offers. It starts <see cref="ScreenState.Inactive"/> and stays that way until a
    /// control says otherwise - the silent start (Part 3). One already known keeps its wish.
    /// </summary>
    internal void Found(ScreenId screen) => _wishes.TryAdd(screen, Start);

    /// <summary>What the control wishes for a screen, suppression included - the field is always complete.</summary>
    internal void Wish(ScreenId screen, ScreenCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _wishes[screen] = command;
    }

    /// <summary>
    /// The command an overlay on this screen stands under, or <see langword="null"/> when the screen
    /// carries none: no wish, <see cref="ScreenState.Inactive"/>, a suppression from the control, or
    /// hidden at the device. The command rather than a yes, because the state decides whether the
    /// window takes gestures.
    /// </summary>
    internal ScreenCommand? Overlay(ScreenId screen) =>
        !_wishes.TryGetValue(screen, out var command)
            || command.State == ScreenState.Inactive
            || command.Suppress is not null
            || _hiddenHere.Contains(screen)
            ? null
            : command;

    /// <summary>
    /// Whether the DM handed this screen back to Windows. A screen without a wish is not - it is
    /// unknown, which is a different answer.
    /// </summary>
    internal bool IsInactive(ScreenId screen) =>
        _wishes.TryGetValue(screen, out var command) && command.State is ScreenState.Inactive;

    /// <summary>"Hide" on the rescue mark: a finding at the device, not a state (Part 6, step 43a).</summary>
    internal void HideHere(ScreenId screen) => _hiddenHere.Add(screen);

    /// <summary>
    /// Lifts what the rescue mark hid and hands back which screens those were - <b>whoever has their
    /// control back wants to play on</b>, so every arriving <c>ConfigUpdate</c> does this first (Part 6).
    /// </summary>
    internal List<ScreenId> LiftHidden()
    {
        var hidden = _hiddenHere.ToList();

        _hiddenHere.Clear();

        return hidden;
    }

    /// <summary>
    /// The start state, as after switching on (Part 3): every screen <see cref="ScreenState.Inactive"/>.
    /// Hands back every screen, each of which has to give up its overlay.
    /// </summary>
    internal List<ScreenId> GoIdle()
    {
        var screens = _wishes.Keys.ToList();

        foreach (var screen in screens)
        {
            _wishes[screen] = Start;
        }

        return screens;
    }
}
