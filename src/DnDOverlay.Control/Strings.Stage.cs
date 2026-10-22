namespace DnDOverlay.Control;

/// <summary>
/// The texts of the stage and its frame: the main window and its menu, the screen tiles, the undo
/// steps, the log panel, the intake, the campaign desk and the start of the application.
/// </summary>
internal static partial class Strings
{
    internal static string AppNotKept => Text[nameof(AppNotKept)];

    internal static string AppConfigurationRecovered(string setAside) => Text.Format(nameof(AppConfigurationRecovered), setAside);

    internal static string AppConfigurationReplaced(string setAside) => Text.Format(nameof(AppConfigurationReplaced), setAside);

    internal static string AppPortTaken(int port, string configuration) => Text.Format(nameof(AppPortTaken), port, configuration);

    internal static string PowerBatteryLow(int percent) => Text.Format(nameof(PowerBatteryLow), percent);

    internal static string DeskUnnamed => Text[nameof(DeskUnnamed)];

    internal static string DeskUnnamedBegun(DateTime begun, int pictures) => Text.Format(nameof(DeskUnnamedBegun), begun, pictures);

    internal static string DeskUnnamedCampaign(DateTime begun) => Text.Format(nameof(DeskUnnamedCampaign), begun);

    internal static string DeskOpened(string name, int pictures, double megabytes, int scenes) =>
        Text.Format(nameof(DeskOpened), name, pictures, megabytes, scenes);

    internal static string DeskReconstructed => Text[nameof(DeskReconstructed)];

    internal static string DeskAdopted(int count) => Text.Format(nameof(DeskAdopted), count);

    internal static string DeskDuplicates(int count) => Text.Format(nameof(DeskDuplicates), count);

    internal static string DeskMissing(int count, string names) => Text.Format(nameof(DeskMissing), count, names);

    internal static string DeskForeign(int count) => Text.Format(nameof(DeskForeign), count);

    internal static string DeskUnreadableScenes(int count, string names) => Text.Format(nameof(DeskUnreadableScenes), count, names);

    internal static string DeskSkipped(int count, string names) => Text.Format(nameof(DeskSkipped), count, names);

    internal static string DeskKept(int count) => Text.Format(nameof(DeskKept), count);

    internal static string MainReportNames => Text[nameof(MainReportNames)];

    internal static string MainReportMissing => Text[nameof(MainReportMissing)];

    internal static string MainReportForeign => Text[nameof(MainReportForeign)];

    internal static string MainReportKept => Text[nameof(MainReportKept)];

    internal static string MainReportSkipped => Text[nameof(MainReportSkipped)];

    internal static string MainReportUnreadable => Text[nameof(MainReportUnreadable)];

    internal static string MainReportOtherCampaign => Text[nameof(MainReportOtherCampaign)];

    internal static string MainReportFileGone(string name) => Text.Format(nameof(MainReportFileGone), name);

    internal static string IntakeNothingUsable => Text[nameof(IntakeNothingUsable)];

    internal static string IntakeRunGoing => Text[nameof(IntakeRunGoing)];

    internal static string IntakeBackgroundRest(string name, int others) => Text.Format(nameof(IntakeBackgroundRest), name, others);

    internal static string IntakeTakingOne(string name) => Text.Format(nameof(IntakeTakingOne), name);

    internal static string IntakeProgressOf(int done, int total, string name) => Text.Format(nameof(IntakeProgressOf), done, total, name);

    internal static string IntakeTaken(int count) => Text.Format(nameof(IntakeTaken), count);

    internal static string IntakeAlreadyPresent(int count) => Text.Format(nameof(IntakeAlreadyPresent), count);

    internal static string IntakeRefused(int count, string details) => Text.Format(nameof(IntakeRefused), count, details);

    internal static string IntakeTolerated(int count, string names) => Text.Format(nameof(IntakeTolerated), count, names);

    internal static string IntakeNothingCame => Text[nameof(IntakeNothingCame)];

    internal static string IntakeBrokenOff(string collected) => Text.Format(nameof(IntakeBrokenOff), collected);

    internal static string EntrancesNothingCameBack => Text[nameof(EntrancesNothingCameBack)];

    internal static string LogPanelNotConnected(string label) => Text.Format(nameof(LogPanelNotConnected), label);

    internal static string LogPanelOnlyThese => Text[nameof(LogPanelOnlyThese)];

    internal static string LogPanelShowAll => Text[nameof(LogPanelShowAll)];

    internal static string LogPanelLinesDropped(int lost) => Text.Format(nameof(LogPanelLinesDropped), lost);

    internal static string MainNoCampaign => Text[nameof(MainNoCampaign)];

    internal static string MainNoticeOk => Text[nameof(MainNoticeOk)];

    internal static string MainClearAll => Text[nameof(MainClearAll)];

    internal static string MainSendToAll => Text[nameof(MainSendToAll)];

    internal static string MainDevices => Text[nameof(MainDevices)];

    internal static string MainLog => Text[nameof(MainLog)];

    internal static string MainSettings => Text[nameof(MainSettings)];

    internal static string MainDockPanel => Text[nameof(MainDockPanel)];

    internal static string MainPinned => Text[nameof(MainPinned)];

    internal static string MainDockLog => Text[nameof(MainDockLog)];

    internal static string MainEdgeLeft => Text[nameof(MainEdgeLeft)];

    internal static string MainEdgeRight => Text[nameof(MainEdgeRight)];

    internal static string MainEdgeTop => Text[nameof(MainEdgeTop)];

    internal static string MainEdgeBottom => Text[nameof(MainEdgeBottom)];

    internal static string MainBackgroundOnePicture => Text[nameof(MainBackgroundOnePicture)];

    internal static string MainNotInStock => Text[nameof(MainNotInStock)];

    internal static string MainNothingToUndo => Text[nameof(MainNothingToUndo)];

    internal static string MainUndone(string step) => Text.Format(nameof(MainUndone), step);

    internal static string MainNothingToRedo => Text[nameof(MainNothingToRedo)];

    internal static string MainRedone(string step) => Text.Format(nameof(MainRedone), step);

    internal static string RecordingAddNamed(string name) => Text.Format(nameof(RecordingAddNamed), name);

    internal static string RecordingAddPictures(int count) => Text.Format(nameof(RecordingAddPictures), count);

    internal static string RecordingRemoveNamed(string name) => Text.Format(nameof(RecordingRemoveNamed), name);

    internal static string RecordingRemovePicture => Text[nameof(RecordingRemovePicture)];

    internal static string RecordingRemovePictures(int count) => Text.Format(nameof(RecordingRemovePictures), count);

    internal static string RecordingMoveNamed(string name, string target) => Text.Format(nameof(RecordingMoveNamed), name, target);

    internal static string RecordingMovePicture(string target) => Text.Format(nameof(RecordingMovePicture), target);

    internal static string RecordingMovePictures(int count, string target) => Text.Format(nameof(RecordingMovePictures), count, target);

    internal static string RecordingCopyNamed(string name, string target) => Text.Format(nameof(RecordingCopyNamed), name, target);

    internal static string RecordingCopyPicture(string target) => Text.Format(nameof(RecordingCopyPicture), target);

    internal static string RecordingCopyPictures(int count, string target) => Text.Format(nameof(RecordingCopyPictures), count, target);

    internal static string RecordingSendNamed(string name) => Text.Format(nameof(RecordingSendNamed), name);

    internal static string RecordingSendPictures(int count) => Text.Format(nameof(RecordingSendPictures), count);

    internal static string RecordingSetBackground(string name, string screen) => Text.Format(nameof(RecordingSetBackground), name, screen);

    internal static string RecordingSetBackgroundOnAll(string name) => Text.Format(nameof(RecordingSetBackgroundOnAll), name);

    internal static string RecordingRemoveBackground(string screen) => Text.Format(nameof(RecordingRemoveBackground), screen);

    internal static string RecordingClear(string screen) => Text.Format(nameof(RecordingClear), screen);

    internal static string RecordingLoadScene(string screen) => Text.Format(nameof(RecordingLoadScene), screen);

    internal static string RecordingLoadLayout => Text[nameof(RecordingLoadLayout)];

    internal static string RecordingClearAll => Text[nameof(RecordingClearAll)];

    internal static string ScreenTileImages => Text[nameof(ScreenTileImages)];

    internal static string ScreenTileWindows => Text[nameof(ScreenTileWindows)];

    internal static string ScreenTileBackground => Text[nameof(ScreenTileBackground)];

    internal static string ScreenTileUnlockAll => Text[nameof(ScreenTileUnlockAll)];

    internal static string ScreenTileBackgroundStrip => Text[nameof(ScreenTileBackgroundStrip)];

    internal static string ScreenTileDeviceNotConnected => Text[nameof(ScreenTileDeviceNotConnected)];

    internal static string ScreenTileDiagnosisDevice(string device, string address, string version, string roundTrip) =>
        Text.Format(nameof(ScreenTileDiagnosisDevice), device, address, version, roundTrip);

    internal static string ScreenTileDiagnosisWaiting => Text[nameof(ScreenTileDiagnosisWaiting)];

    internal static string ScreenTileDiagnosisFrames(double median, double? p95, double? max, int frames, int stalls) =>
        Text.Format(nameof(ScreenTileDiagnosisFrames), median, p95, max, frames, stalls);

    internal static string ScreenTileDiagnosisFramesTooFew => Text[nameof(ScreenTileDiagnosisFramesTooFew)];

    internal static string ScreenTileDiagnosisLoad(double cpuPercent, int downloading, int waiting) =>
        Text.Format(nameof(ScreenTileDiagnosisLoad), cpuPercent, downloading, waiting);

    internal static string ScreenTileDiagnosisStore(int pictures, long cacheMegabytes, long memoryMegabytes, long memoryCapMegabytes) =>
        Text.Format(nameof(ScreenTileDiagnosisStore), pictures, cacheMegabytes, memoryMegabytes, memoryCapMegabytes);

    internal static string ScreenTileStateActive => Text[nameof(ScreenTileStateActive)];

    internal static string ScreenTileStateFrozen => Text[nameof(ScreenTileStateFrozen)];

    internal static string ScreenTileStateDiagnostic => Text[nameof(ScreenTileStateDiagnostic)];

    internal static string ScreenTileStateInactive => Text[nameof(ScreenTileStateInactive)];

    internal static string ThumbnailWindowElevated(string program) => Text.Format(nameof(ThumbnailWindowElevated), program);

    internal static string ThumbnailWindowInactive(string program) => Text.Format(nameof(ThumbnailWindowInactive), program);

    internal static string ThumbnailWindowNotAllowed(string program) => Text.Format(nameof(ThumbnailWindowNotAllowed), program);

    internal static string GripsMinimise => Text[nameof(GripsMinimise)];

    internal static string GripsMaximise => Text[nameof(GripsMaximise)];

    internal static string GripsRestore => Text[nameof(GripsRestore)];

    internal static string GripsFillScreen => Text[nameof(GripsFillScreen)];

    internal static string ScreenTileFocusOff => Text[nameof(ScreenTileFocusOff)];

    internal static string ScreenTileFocusTip => Text[nameof(ScreenTileFocusTip)];

    internal static string ScreenTileFocusOn(int count) => Text.Format(nameof(ScreenTileFocusOn), count);

    internal static string ScreenTileFocusTooMany(int limit) => Text.Format(nameof(ScreenTileFocusTooMany), limit);

    internal static string TileLoadingCount(int arrived, int total) => Text.Format(nameof(TileLoadingCount), arrived, total);
}
