namespace DnDOverlay.Control;

/// <summary>The texts of the tidy-up view, the stock notice and the offer of the last campaign (M5b8).</summary>
internal static partial class Strings
{
    internal static string TidyTitle => Text[nameof(TidyTitle)];

    internal static string TidyRescan => Text[nameof(TidyRescan)];

    internal static string TidyVerify => Text[nameof(TidyVerify)];

    internal static string TidyBreakOff => Text[nameof(TidyBreakOff)];

    internal static string TidyColumnName => Text[nameof(TidyColumnName)];

    internal static string TidyColumnSize => Text[nameof(TidyColumnSize)];

    internal static string TidyColumnPixels => Text[nameof(TidyColumnPixels)];

    internal static string TidyColumnUsed => Text[nameof(TidyColumnUsed)];

    internal static string TidyUnused => Text[nameof(TidyUnused)];

    internal static string TidyDeleteNote => Text[nameof(TidyDeleteNote)];

    internal static string TidyRescanning => Text[nameof(TidyRescanning)];

    internal static string TidyRescanned => Text[nameof(TidyRescanned)];

    internal static string TidyVerifying => Text[nameof(TidyVerifying)];

    internal static string TidyVerifyBrokenOff => Text[nameof(TidyVerifyBrokenOff)];

    internal static string TidySize(double megabytes) => Text.Format(nameof(TidySize), megabytes);

    internal static string TidyPixels(int width, int height) => Text.Format(nameof(TidyPixels), width, height);

    internal static string TidyUsedIn(int count) => Text.Format(nameof(TidyUsedIn), count);

    internal static string TidySummary(string campaign, int pictures, double megabytes, double freeGigabytes) => Text.Format(nameof(TidySummary), campaign, pictures, megabytes, freeGigabytes);

    internal static string TidyVerifiedAllWell(int count) => Text.Format(nameof(TidyVerifiedAllWell), count);

    internal static string TidyVerifiedDamaged(int count, int damaged, string names) => Text.Format(nameof(TidyVerifiedDamaged), count, damaged, names);

    internal static string MainStockFreeSpace(double freeGigabytes) => Text.Format(nameof(MainStockFreeSpace), freeGigabytes);

    internal static string MainStockPictures(string campaign, int pictures, int threshold) => Text.Format(nameof(MainStockPictures), campaign, pictures, threshold);

    internal static string MainStockDontReport => Text[nameof(MainStockDontReport)];

    internal static string CampaignSwitchTidy => Text[nameof(CampaignSwitchTidy)];

    internal static string SettingsStockFreeSpace => Text[nameof(SettingsStockFreeSpace)];

    internal static string SettingsStockFreeSpaceUnit => Text[nameof(SettingsStockFreeSpaceUnit)];

    internal static string SettingsStockPictures => Text[nameof(SettingsStockPictures)];

    internal static string SettingsStockPicturesUnit => Text[nameof(SettingsStockPicturesUnit)];

    internal static string SettingsStockNote => Text[nameof(SettingsStockNote)];

    internal static string MainTableFromCampaign(string campaign) => Text.Format(nameof(MainTableFromCampaign), campaign);

    internal static string MainOpenCampaign => Text[nameof(MainOpenCampaign)];

    internal static string CampaignSwitchLastOpen(string label) => Text.Format(nameof(CampaignSwitchLastOpen), label);
}
