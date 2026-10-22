using DnDOverlay.Core;

namespace DnDOverlay.Campaign;

/// <summary>
/// The texts this library hands to the control (Part 8) - why a picture or a campaign was refused,
/// why a move did not happen. English in <c>Strings.resx</c>, German beside it.
/// </summary>
internal static class Strings
{
    private static readonly ResourceText Text = new("DnDOverlay.Campaign.Strings", typeof(Strings).Assembly);

    internal static string NoSpace(long free, long needed, string directory) => Text.Format(nameof(NoSpace), free, needed, directory);

    internal static string CampaignTooNew(string path, int schema, int readable) => Text.Format(nameof(CampaignTooNew), path, schema, readable);

    internal static string SceneFileTooNew(string path, int schema, int readable) => Text.Format(nameof(SceneFileTooNew), path, schema, readable);

    internal static string NoSuchCampaign(string name, string basePath) => Text.Format(nameof(NoSuchCampaign), name, basePath);

    internal static string MoveFailed => Text[nameof(MoveFailed)];

    internal static string MoveCollisions(string names) => Text.Format(nameof(MoveCollisions), names);

    internal static string MoveNoRoom(double needed, double free) => Text.Format(nameof(MoveNoRoom), needed, free);

    internal static string MoveCampaignFailed(string name, string reason) => Text.Format(nameof(MoveCampaignFailed), name, reason);

    internal static string MoveCopyMismatch(string name) => Text.Format(nameof(MoveCopyMismatch), name);

    internal static string MoveCopyFailed(string reason) => Text.Format(nameof(MoveCopyFailed), reason);

    internal static string NothingCameOfIt => Text[nameof(NothingCameOfIt)];
}
