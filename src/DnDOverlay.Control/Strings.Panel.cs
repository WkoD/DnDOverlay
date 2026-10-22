namespace DnDOverlay.Control;

/// <summary>The texts of the campaign panel: its head, the campaign switcher and the tabs Pictures, Scenes and Layouts.</summary>
internal static partial class Strings
{
    internal static string CampaignPanelTabPictures => Text[nameof(CampaignPanelTabPictures)];

    internal static string CampaignPanelTabScenes => Text[nameof(CampaignPanelTabScenes)];

    internal static string CampaignPanelTabLayouts => Text[nameof(CampaignPanelTabLayouts)];

    internal static string CampaignPanelSaveLayout => Text[nameof(CampaignPanelSaveLayout)];

    internal static string CampaignPanelBreakOff => Text[nameof(CampaignPanelBreakOff)];

    internal static string CampaignPanelSortOldest => Text[nameof(CampaignPanelSortOldest)];

    internal static string CampaignPanelSortNameAscending => Text[nameof(CampaignPanelSortNameAscending)];

    internal static string CampaignPanelSortNameDescending => Text[nameof(CampaignPanelSortNameDescending)];

    internal static string CampaignPanelSortNewest => Text[nameof(CampaignPanelSortNewest)];

    internal static string CampaignPanelAddFiles => Text[nameof(CampaignPanelAddFiles)];

    internal static string CampaignPanelPaste => Text[nameof(CampaignPanelPaste)];

    internal static string CampaignPanelFetch => Text[nameof(CampaignPanelFetch)];

    internal static string CampaignPanelFilesTitle => Text[nameof(CampaignPanelFilesTitle)];

    internal static string CampaignPanelFilterPictures => Text[nameof(CampaignPanelFilterPictures)];

    internal static string CampaignPanelFilterAll => Text[nameof(CampaignPanelFilterAll)];

    internal static string PanelHeadHint => Text[nameof(PanelHeadHint)];

    internal static string PanelHeadNothingToUndo => Text[nameof(PanelHeadNothingToUndo)];

    internal static string PanelHeadNothingToRedo => Text[nameof(PanelHeadNothingToRedo)];

    internal static string PanelHeadUndo => Text[nameof(PanelHeadUndo)];

    internal static string PanelHeadRedo => Text[nameof(PanelHeadRedo)];

    internal static string PanelHeadNoCampaign => Text[nameof(PanelHeadNoCampaign)];

    internal static string PanelHeadOverview => Text[nameof(PanelHeadOverview)];

    internal static string PanelHeadSingleView => Text[nameof(PanelHeadSingleView)];

    internal static string PanelHeadUndoTip(string act) => Text.Format(nameof(PanelHeadUndoTip), act);

    internal static string PanelHeadRedoTip(string act) => Text.Format(nameof(PanelHeadRedoTip), act);

    internal static string CampaignSwitchNone => Text[nameof(CampaignSwitchNone)];

    internal static string CampaignSwitchSetUp => Text[nameof(CampaignSwitchSetUp)];

    internal static string CampaignSwitchTitle => Text[nameof(CampaignSwitchTitle)];

    internal static string CampaignSwitchOpenLabel(string label, int pictures, double megabytes) => Text.Format(nameof(CampaignSwitchOpenLabel), label, pictures, megabytes);

    internal static string CampaignSwitchQuestion(string target, string current) => Text.Format(nameof(CampaignSwitchQuestion), target, current);

    internal static string CampaignNamingTitle => Text[nameof(CampaignNamingTitle)];

    internal static string CampaignNamingPrompt => Text[nameof(CampaignNamingPrompt)];

    internal static string CampaignNamingBusy => Text[nameof(CampaignNamingBusy)];

    internal static string CampaignNamingTaken(string name) => Text.Format(nameof(CampaignNamingTaken), name);

    internal static string StockNoCampaign => Text[nameof(StockNoCampaign)];

    internal static string StockEmpty => Text[nameof(StockEmpty)];

    internal static string StockNoMatch => Text[nameof(StockNoMatch)];

    internal static string StockMarkMissing => Text[nameof(StockMarkMissing)];

    internal static string StockMenuSendActive => Text[nameof(StockMenuSendActive)];

    internal static string StockMenuSendPicture => Text[nameof(StockMenuSendPicture)];

    internal static string StockMenuSendBackground => Text[nameof(StockMenuSendBackground)];

    internal static string StockMenuToAll => Text[nameof(StockMenuToAll)];

    internal static string StockMenuLiesOnSeveral => Text[nameof(StockMenuLiesOnSeveral)];

    internal static string StockMenuLiesOnNone => Text[nameof(StockMenuLiesOnNone)];

    internal static string StockMenuShowFile => Text[nameof(StockMenuShowFile)];

    internal static string StockMenuRename => Text[nameof(StockMenuRename)];

    internal static string StockMenuDelete => Text[nameof(StockMenuDelete)];

    internal static string StockMenuUsedInSeveral => Text[nameof(StockMenuUsedInSeveral)];

    internal static string StockMenuUsedNowhere => Text[nameof(StockMenuUsedNowhere)];

    internal static string StockMenuUsedIn => Text[nameof(StockMenuUsedIn)];

    internal static string StockNoScreen => Text[nameof(StockNoScreen)];

    internal static string StockMarkOn(int screens) => Text.Format(nameof(StockMarkOn), screens);

    internal static string StockMarkIn(int saved) => Text.Format(nameof(StockMarkIn), saved);

    internal static string StockMenuLiesOn(int screens) => Text.Format(nameof(StockMenuLiesOn), screens);

    internal static string StockMenuDeleteMany(int count) => Text.Format(nameof(StockMenuDeleteMany), count);

    internal static string StockMenuUsedScene(string scene) => Text.Format(nameof(StockMenuUsedScene), scene);

    internal static string StockMenuUsedLayout(string layout) => Text.Format(nameof(StockMenuUsedLayout), layout);

    internal static string StockRenameTitle => Text[nameof(StockRenameTitle)];

    internal static string StockRenamePrompt => Text[nameof(StockRenamePrompt)];

    internal static string StockDeleteFileGone => Text[nameof(StockDeleteFileGone)];

    internal static string StockDeleteFilesGone => Text[nameof(StockDeleteFilesGone)];

    internal static string StockDeleteTitleOne => Text[nameof(StockDeleteTitleOne)];

    internal static string StockDeleteTitleMany => Text[nameof(StockDeleteTitleMany)];

    internal static string StockEmptiedTitle => Text[nameof(StockEmptiedTitle)];

    internal static string StockRenamed(string old, string name) => Text.Format(nameof(StockRenamed), old, name);

    internal static string StockRenameTaken(string name) => Text.Format(nameof(StockRenameTaken), name);

    internal static string StockDeleteOneQuestion(string name) => Text.Format(nameof(StockDeleteOneQuestion), name);

    internal static string StockDeleteManyQuestion(int count) => Text.Format(nameof(StockDeleteManyQuestion), count);

    internal static string StockDeleteTakenOffOne(int screens) => Text.Format(nameof(StockDeleteTakenOffOne), screens);

    internal static string StockDeleteTakenOffMany(int screens) => Text.Format(nameof(StockDeleteTakenOffMany), screens);

    internal static string StockDeleteFromScenes(string scenes) => Text.Format(nameof(StockDeleteFromScenes), scenes);

    internal static string StockDeleteFromLayouts(string layouts) => Text.Format(nameof(StockDeleteFromLayouts), layouts);

    internal static string StockDeleteStepsLost(int steps) => Text.Format(nameof(StockDeleteStepsLost), steps);

    internal static string StockDeletedOne(string name) => Text.Format(nameof(StockDeletedOne), name);

    internal static string StockDeletedMany(int count) => Text.Format(nameof(StockDeletedMany), count);

    internal static string StockEmptiedScenes(string scenes) => Text.Format(nameof(StockEmptiedScenes), scenes);

    internal static string StockEmptiedLayouts(string layouts) => Text.Format(nameof(StockEmptiedLayouts), layouts);

    internal static string StockEmptiedQuestion(string lines) => Text.Format(nameof(StockEmptiedQuestion), lines);

    internal static string StockEmptiedKept(int count) => Text.Format(nameof(StockEmptiedKept), count);

    internal static string StockEmptiedDeleted(int count) => Text.Format(nameof(StockEmptiedDeleted), count);

    internal static string StockListedMore(string names, int more) => Text.Format(nameof(StockListedMore), names, more);

    internal static string ScenesNoneSaved => Text[nameof(ScenesNoneSaved)];

    internal static string ScenesNoCampaign => Text[nameof(ScenesNoCampaign)];

    internal static string ScenesNoMatch => Text[nameof(ScenesNoMatch)];

    internal static string ScenesNoCampaignToSave => Text[nameof(ScenesNoCampaignToSave)];

    internal static string ScenesSaveTitle => Text[nameof(ScenesSaveTitle)];

    internal static string ScenesMenuLoad => Text[nameof(ScenesMenuLoad)];

    internal static string ScenesMenuReplace => Text[nameof(ScenesMenuReplace)];

    internal static string ScenesMenuAdd => Text[nameof(ScenesMenuAdd)];

    internal static string ScenesMenuOverwrite => Text[nameof(ScenesMenuOverwrite)];

    internal static string ScenesMenuRename => Text[nameof(ScenesMenuRename)];

    internal static string ScenesMenuDelete => Text[nameof(ScenesMenuDelete)];

    internal static string ScenesNoScreen => Text[nameof(ScenesNoScreen)];

    internal static string ScenesRenameTitle => Text[nameof(ScenesRenameTitle)];

    internal static string ScenesRenamePrompt => Text[nameof(ScenesRenamePrompt)];

    internal static string ScenesDeleteTitle => Text[nameof(ScenesDeleteTitle)];

    internal static string ScenesSavePrompt(string screen) => Text.Format(nameof(ScenesSavePrompt), screen);

    internal static string ScenesSuggestedName(string screen, string stamp) => Text.Format(nameof(ScenesSuggestedName), screen, stamp);

    internal static string ScenesOverwriteQuestion(string name) => Text.Format(nameof(ScenesOverwriteQuestion), name);

    internal static string ScenesSaved(string name) => Text.Format(nameof(ScenesSaved), name);

    internal static string ScenesTileTip(string name, int width, int height, DateTime saved) => Text.Format(nameof(ScenesTileTip), name, width, height, saved);

    internal static string ScenesLoaded(string name) => Text.Format(nameof(ScenesLoaded), name);

    internal static string ScenesLoadedMissing(string name, int missing, int pictures) => Text.Format(nameof(ScenesLoadedMissing), name, missing, pictures);

    internal static string ScenesRenameTaken(string name) => Text.Format(nameof(ScenesRenameTaken), name);

    internal static string ScenesDeleteQuestion(string name) => Text.Format(nameof(ScenesDeleteQuestion), name);

    internal static string LayoutsNoCampaign => Text[nameof(LayoutsNoCampaign)];

    internal static string LayoutsNoneSaved => Text[nameof(LayoutsNoneSaved)];

    internal static string LayoutsNoMatch => Text[nameof(LayoutsNoMatch)];

    internal static string LayoutsNoCampaignToSave => Text[nameof(LayoutsNoCampaignToSave)];

    internal static string LayoutsNothingToSave => Text[nameof(LayoutsNothingToSave)];

    internal static string LayoutsSaveTitle => Text[nameof(LayoutsSaveTitle)];

    internal static string LayoutsMenuLoad => Text[nameof(LayoutsMenuLoad)];

    internal static string LayoutsMenuAdd => Text[nameof(LayoutsMenuAdd)];

    internal static string LayoutsMenuReplaceUsed => Text[nameof(LayoutsMenuReplaceUsed)];

    internal static string LayoutsMenuReplaceAll => Text[nameof(LayoutsMenuReplaceAll)];

    internal static string LayoutsMenuSaveAsScenes => Text[nameof(LayoutsMenuSaveAsScenes)];

    internal static string LayoutsMenuRename => Text[nameof(LayoutsMenuRename)];

    internal static string LayoutsMenuOverwrite => Text[nameof(LayoutsMenuOverwrite)];

    internal static string LayoutsMenuDelete => Text[nameof(LayoutsMenuDelete)];

    internal static string LayoutsNoSceneSaved => Text[nameof(LayoutsNoSceneSaved)];

    internal static string LayoutsRenameTitle => Text[nameof(LayoutsRenameTitle)];

    internal static string LayoutsRenamePrompt => Text[nameof(LayoutsRenamePrompt)];

    internal static string LayoutsDeleteTitle => Text[nameof(LayoutsDeleteTitle)];

    internal static string LayoutsSavePrompt(int screens) => Text.Format(nameof(LayoutsSavePrompt), screens);

    internal static string LayoutsSuggestedName(string stamp) => Text.Format(nameof(LayoutsSuggestedName), stamp);

    internal static string LayoutsOverwriteQuestion(string name) => Text.Format(nameof(LayoutsOverwriteQuestion), name);

    internal static string LayoutsSaved(string name, int screens) => Text.Format(nameof(LayoutsSaved), name, screens);

    internal static string LayoutsNothingLaid(string name) => Text.Format(nameof(LayoutsNothingLaid), name);

    internal static string LayoutsLoaded(string name, int screens) => Text.Format(nameof(LayoutsLoaded), name, screens);

    internal static string LayoutsScenesLeftOut(int scenes) => Text.Format(nameof(LayoutsScenesLeftOut), scenes);

    internal static string LayoutsSavedAsScenes(string names) => Text.Format(nameof(LayoutsSavedAsScenes), names);

    internal static string LayoutsPicturesMissing(int pictures) => Text.Format(nameof(LayoutsPicturesMissing), pictures);

    internal static string LayoutsSceneName(string screen, string stamp) => Text.Format(nameof(LayoutsSceneName), screen, stamp);

    internal static string LayoutsTileTip(string name, string screens, DateTime saved) => Text.Format(nameof(LayoutsTileTip), name, screens, saved);

    internal static string LayoutsTileScreen(int count) => Text.Format(nameof(LayoutsTileScreen), count);

    internal static string LayoutsTileScreens(int count) => Text.Format(nameof(LayoutsTileScreens), count);

    internal static string LayoutsRenameTaken(string name) => Text.Format(nameof(LayoutsRenameTaken), name);

    internal static string LayoutsDeleteQuestion(string name) => Text.Format(nameof(LayoutsDeleteQuestion), name);

    internal static string CampaignIntakeRunning => Text[nameof(CampaignIntakeRunning)];

    internal static string CampaignDeleteHeld(string name) => Text.Format(nameof(CampaignDeleteHeld), name);

    internal static string CampaignDeleteIncomplete(string name, string folder) => Text.Format(nameof(CampaignDeleteIncomplete), name, folder);

    internal static string CampaignCheckFailed(string name, string reason) => Text.Format(nameof(CampaignCheckFailed), name, reason);

    /// <summary>
    /// Whether the verb of an undo label stands at its end - "true" in German ("\"Grimmbart\"
    /// hinzugefügt"), "false" in English ("Add \"Grimmbart\""). A fact of the language, so it lives
    /// beside its sentences: the narrow button keeps the verb (Part 7), wherever the language puts it.
    /// </summary>
    internal static string PanelHeadVerbLast => Text[nameof(PanelHeadVerbLast)];

    internal static string ScenesLoadStep(string name) => Text.Format(nameof(ScenesLoadStep), name);

    internal static string LayoutsLoadStep(string name) => Text.Format(nameof(LayoutsLoadStep), name);

    internal static string ScenesWriteFailed(string name, string reason) => Text.Format(nameof(ScenesWriteFailed), name, reason);

    internal static string ScenesDeleteFailed(string name, string reason) => Text.Format(nameof(ScenesDeleteFailed), name, reason);

    internal static string LayoutsWriteFailed(string name, string reason) => Text.Format(nameof(LayoutsWriteFailed), name, reason);

    internal static string LayoutsDeleteFailed(string name, string reason) => Text.Format(nameof(LayoutsDeleteFailed), name, reason);

    internal static string StockRenameFailed(string name, string reason) => Text.Format(nameof(StockRenameFailed), name, reason);

    internal static string StockRenamePartly(string old, string name, string reason) => Text.Format(nameof(StockRenamePartly), old, name, reason);

    internal static string StockDeletePartly(string reason) => Text.Format(nameof(StockDeletePartly), reason);

    internal static string ShelfRefused => Text[nameof(ShelfRefused)];

    internal static string StockDeleteNotWritten(string names) => Text.Format(nameof(StockDeleteNotWritten), names);
}
