using System.Windows;
using DnDOverlay.Campaign;

namespace DnDOverlay.Control;

/// <summary>
/// <b>Saving names the campaign</b> (Part 3): an unnamed one is asked for its name once, its folder
/// renamed, and then the scene or layout saved. No saved arrangement without a home.
/// </summary>
internal static class CampaignNaming
{
    /// <summary>Asks the unnamed campaign for its first name; false when the DM cancelled.</summary>
    internal static bool Ask(Window owner, CampaignDesk desk)
    {
        while (NameDialog.Ask(owner, "Name the campaign", "This campaign has no name yet. Saving needs a named campaign - what is it called?", string.Empty) is { } typed)
        {
            switch (desk.Rename(typed))
            {
                case CampaignOutcome.Done:
                    return true;

                case CampaignOutcome.Taken:
                    MessageBox.Show(owner, $"A campaign named \"{typed}\" exists already.", "Name the campaign");
                    break;

                case CampaignOutcome.Busy:
                    MessageBox.Show(owner, "The folder is in use right now - try again in a moment.", "Name the campaign");
                    break;

                default:
                    break;
            }
        }

        return false;
    }
}
