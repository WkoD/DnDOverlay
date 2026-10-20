using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>A screen as it stands now: its address and the name the control knows it by.</summary>
public sealed record LayoutScreen(ScreenRef Screen, string Name);

/// <summary>
/// Which screen each scene of a layout goes to (Part 3): <b>by address first, then by name</b>.
/// <para>
/// A layout is a lasting document, but a <see cref="ScreenRef"/> dies with <c>display.json</c> - a
/// reinstalled device would make every saved layout worthless. The name survives that, because it
/// lives in the control. The other way round, the address catches a screen the DM has renamed.
/// </para>
/// <para>
/// <b>A screen takes one scene of the layout here, never two</b>: a part whose screen is already
/// taken - a name that now belongs to a screen another part found by address - stays unmatched and
/// goes to the mapping table, where the DM may put it there on purpose. The answer is in the order of
/// the parts; null where nothing was found.
/// </para>
/// </summary>
public static class LayoutMatching
{
    public static IReadOnlyList<ScreenRef?> Match(IReadOnlyList<LayoutPart> parts, IReadOnlyList<LayoutScreen> screens)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(screens);

        var found = new ScreenRef?[parts.Count];
        var taken = new HashSet<ScreenRef>();

        for (var index = 0; index < parts.Count; index++)
        {
            var wanted = parts[index].Screen;

            if (screens.Any(screen => screen.Screen == wanted) && taken.Add(wanted))
            {
                found[index] = wanted;
            }
        }

        for (var index = 0; index < parts.Count; index++)
        {
            if (found[index] is not null)
            {
                continue;
            }

            var named = screens.FirstOrDefault(screen =>
                !taken.Contains(screen.Screen)
                && string.Equals(screen.Name, parts[index].ScreenName, StringComparison.OrdinalIgnoreCase));

            if (named is not null)
            {
                taken.Add(named.Screen);
                found[index] = named.Screen;
            }
        }

        return found;
    }
}
