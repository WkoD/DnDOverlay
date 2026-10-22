namespace DnDOverlay.Control;

/// <summary>
/// The texts of the settings window and the network panel, and of the dialogs around campaigns, names,
/// device assignment and layout mapping.
/// </summary>
internal static partial class Strings
{
    internal static string SettingsTitle => Text[nameof(SettingsTitle)];

    internal static string SettingsBreakOff => Text[nameof(SettingsBreakOff)];

    internal static string SettingsChangePlace => Text[nameof(SettingsChangePlace)];

    internal static string SettingsHeadingLanguage => Text[nameof(SettingsHeadingLanguage)];

    internal static string SettingsHeadingLoading => Text[nameof(SettingsHeadingLoading)];

    internal static string SettingsHeadingPictures => Text[nameof(SettingsHeadingPictures)];

    internal static string SettingsHeadingCampaigns => Text[nameof(SettingsHeadingCampaigns)];

    internal static string SettingsHeadingLog => Text[nameof(SettingsHeadingLog)];

    internal static string SettingsHeadingNetwork => Text[nameof(SettingsHeadingNetwork)];

    internal static string SettingsMoveUnderWay => Text[nameof(SettingsMoveUnderWay)];

    internal static string SettingsSceneReplace => Text[nameof(SettingsSceneReplace)];

    internal static string SettingsSceneAdd => Text[nameof(SettingsSceneAdd)];

    internal static string SettingsLayoutAdd => Text[nameof(SettingsLayoutAdd)];

    internal static string SettingsLayoutReplaceUsed => Text[nameof(SettingsLayoutReplaceUsed)];

    internal static string SettingsLayoutReplaceAll => Text[nameof(SettingsLayoutReplaceAll)];

    internal static string SettingsSceneLoadsBy => Text[nameof(SettingsSceneLoadsBy)];

    internal static string SettingsLayoutLoadsBy => Text[nameof(SettingsLayoutLoadsBy)];

    internal static string SettingsLoadMenuNote => Text[nameof(SettingsLoadMenuNote)];

    internal static string SettingsPatternNeedsNumber => Text[nameof(SettingsPatternNeedsNumber)];

    internal static string SettingsPatternNext(string next) => Text.Format(nameof(SettingsPatternNext), next);

    internal static string SettingsTouchPoints => Text[nameof(SettingsTouchPoints)];

    internal static string SettingsPatternLabel => Text[nameof(SettingsPatternLabel)];

    internal static string SettingsOpenFolder => Text[nameof(SettingsOpenFolder)];

    internal static string SettingsLanguageNote => Text[nameof(SettingsLanguageNote)];

    internal static string SettingsLanguageAutomatic => Text[nameof(SettingsLanguageAutomatic)];

    internal static string SettingsLanguageLabel => Text[nameof(SettingsLanguageLabel)];

    internal static string SettingsLogTrace => Text[nameof(SettingsLogTrace)];

    internal static string SettingsLogDebug => Text[nameof(SettingsLogDebug)];

    internal static string SettingsLogInformation => Text[nameof(SettingsLogInformation)];

    internal static string SettingsLogWarnings => Text[nameof(SettingsLogWarnings)];

    internal static string SettingsLogErrors => Text[nameof(SettingsLogErrors)];

    internal static string SettingsLogLabel => Text[nameof(SettingsLogLabel)];

    internal static string SettingsPortLater(int running) => Text.Format(nameof(SettingsPortLater), running);

    internal static string SettingsPortRange => Text[nameof(SettingsPortRange)];

    internal static string SettingsPortChanged(int running) => Text.Format(nameof(SettingsPortChanged), running);

    internal static string SettingsPortLabel => Text[nameof(SettingsPortLabel)];

    internal static string SettingsPlace(string path) => Text.Format(nameof(SettingsPlace), path);

    internal static string SettingsPickerTitle => Text[nameof(SettingsPickerTitle)];

    internal static string SettingsMoveCaption => Text[nameof(SettingsMoveCaption)];

    internal static string SettingsMoveInside => Text[nameof(SettingsMoveInside)];

    internal static string SettingsMoveCollisions(string names) => Text.Format(nameof(SettingsMoveCollisions), names);

    internal static string SettingsTaken(string to) => Text.Format(nameof(SettingsTaken), to);

    internal static string SettingsTakenLeftBehind(string to, int count, string from) => Text.Format(nameof(SettingsTakenLeftBehind), to, count, from);

    internal static string SettingsMoving => Text[nameof(SettingsMoving)];

    internal static string SettingsCopying(double doneMegabytes, double totalMegabytes) => Text.Format(nameof(SettingsCopying), doneMegabytes, totalMegabytes);

    internal static string SettingsMoved(int count, string to) => Text.Format(nameof(SettingsMoved), count, to);

    internal static string SettingsMovedLeftBehind(int count, string to, string left) => Text.Format(nameof(SettingsMovedLeftBehind), count, to, left);

    internal static string SettingsMoveBrokenOff(string from) => Text.Format(nameof(SettingsMoveBrokenOff), from);

    internal static string SettingsMoveRefused(string reason, string from) => Text.Format(nameof(SettingsMoveRefused), reason, from);

    internal static string MoveTitle => Text[nameof(MoveTitle)];

    internal static string MoveFrom(string from) => Text.Format(nameof(MoveFrom), from);

    internal static string MoveTo(string to) => Text.Format(nameof(MoveTo), to);

    internal static string MoveSize(int count, double gigabytes) => Text.Format(nameof(MoveSize), count, gigabytes);

    internal static string MoveDeep(int room) => Text.Format(nameof(MoveDeep), room);

    internal static string MoveClosing => Text[nameof(MoveClosing)];

    internal static string MoveOnlyPlaceNote => Text[nameof(MoveOnlyPlaceNote)];

    internal static string MoveMove => Text[nameof(MoveMove)];

    internal static string MoveOnlyPlace => Text[nameof(MoveOnlyPlace)];

    internal static string MoveCancel => Text[nameof(MoveCancel)];

    internal static string NetworkFirstRunHeading => Text[nameof(NetworkFirstRunHeading)];

    internal static string NetworkThisControl => Text[nameof(NetworkThisControl)];

    internal static string NetworkNetworks => Text[nameof(NetworkNetworks)];

    internal static string NetworkFirewall => Text[nameof(NetworkFirewall)];

    internal static string NetworkNoAddress => Text[nameof(NetworkNoAddress)];

    internal static string NetworkReachable(string addresses, string machine) => Text.Format(nameof(NetworkReachable), addresses, machine);

    internal static string NetworkListening => Text[nameof(NetworkListening)];

    internal static string NetworkNotListening => Text[nameof(NetworkNotListening)];

    internal static string NetworkFirewallNotAsked => Text[nameof(NetworkFirewallNotAsked)];

    internal static string NetworkActiveProfiles(string profiles) => Text.Format(nameof(NetworkActiveProfiles), profiles);

    internal static string NetworkAllowInForce => Text[nameof(NetworkAllowInForce)];

    internal static string NetworkAllowFor(string network) => Text.Format(nameof(NetworkAllowFor), network);

    internal static string NetworkChangeClassification => Text[nameof(NetworkChangeClassification)];

    internal static string NetworkPublicTitle => Text[nameof(NetworkPublicTitle)];

    internal static string NetworkPublicMessage => Text[nameof(NetworkPublicMessage)];

    internal static string NetworkPublicMessageCovering(string networks) => Text.Format(nameof(NetworkPublicMessageCovering), networks);

    internal static string NetworkRuleBlocks => Text[nameof(NetworkRuleBlocks)];

    internal static string NetworkNoRule => Text[nameof(NetworkNoRule)];

    internal static string NetworkNoneLetsThrough => Text[nameof(NetworkNoneLetsThrough)];

    internal static string NetworkRulesFound(int count) => Text.Format(nameof(NetworkRulesFound), count);

    internal static string NetworkPublicStanding => Text[nameof(NetworkPublicStanding)];

    internal static string NetworkVerdictAllows => Text[nameof(NetworkVerdictAllows)];

    internal static string NetworkVerdictBlocks => Text[nameof(NetworkVerdictBlocks)];

    internal static string NetworkVerdictOverruled => Text[nameof(NetworkVerdictOverruled)];

    internal static string NetworkVerdictOtherProfile => Text[nameof(NetworkVerdictOtherProfile)];

    internal static string NetworkVerdictDisabled => Text[nameof(NetworkVerdictDisabled)];

    internal static string NetworkVerdictOtherProgram => Text[nameof(NetworkVerdictOtherProgram)];

    internal static string NetworkActionAllow => Text[nameof(NetworkActionAllow)];

    internal static string NetworkActionBlock => Text[nameof(NetworkActionBlock)];

    internal static string NetworkNoProgram => Text[nameof(NetworkNoProgram)];

    internal static string NetworkAllProfiles => Text[nameof(NetworkAllProfiles)];

    internal static string NetworkNoProfile => Text[nameof(NetworkNoProfile)];

    internal static string NetworkProfileDomain => Text[nameof(NetworkProfileDomain)];

    internal static string NetworkProfilePrivate => Text[nameof(NetworkProfilePrivate)];

    internal static string NetworkProfilePublic => Text[nameof(NetworkProfilePublic)];

    internal static string NetworkRemoveAll => Text[nameof(NetworkRemoveAll)];

    internal static string NetworkRemoveTitle => Text[nameof(NetworkRemoveTitle)];

    internal static string NetworkRemoveMessage(string? program) => Text.Format(nameof(NetworkRemoveMessage), program);

    internal static string NetworkCheckAgain => Text[nameof(NetworkCheckAgain)];

    internal static string NetworkHelperMissing(string helper) => Text.Format(nameof(NetworkHelperMissing), helper);

    internal static string NetworkHelperNotStarted => Text[nameof(NetworkHelperNotStarted)];

    internal static string NetworkHelperFinished(string helper, int exitCode) => Text.Format(nameof(NetworkHelperFinished), helper, exitCode);

    internal static string NetworkElevationDeclined => Text[nameof(NetworkElevationDeclined)];

    internal static string NetworkSettingsNotOpened => Text[nameof(NetworkSettingsNotOpened)];

    internal static string NetworkStepInstall => Text[nameof(NetworkStepInstall)];

    internal static string NetworkStepRequest => Text[nameof(NetworkStepRequest)];

    internal static string NetworkStepAllow => Text[nameof(NetworkStepAllow)];

    internal static string NetworkDisplayPcNote => Text[nameof(NetworkDisplayPcNote)];

    internal static string CampaignSetupTitle => Text[nameof(CampaignSetupTitle)];

    internal static string CampaignSetupApply => Text[nameof(CampaignSetupApply)];

    internal static string CampaignSetupOpenFolder => Text[nameof(CampaignSetupOpenFolder)];

    internal static string CampaignSetupDelete => Text[nameof(CampaignSetupDelete)];

    internal static string CampaignSetupName => Text[nameof(CampaignSetupName)];

    internal static string CampaignSetupFacts(int pictures, double megabytes, int scenes, int layouts, string directory) => Text.Format(nameof(CampaignSetupFacts), pictures, megabytes, scenes, layouts, directory);

    internal static string CampaignSetupRenamed => Text[nameof(CampaignSetupRenamed)];

    internal static string CampaignSetupNameTaken(string name) => Text.Format(nameof(CampaignSetupNameTaken), name);

    internal static string CampaignSetupBusy => Text[nameof(CampaignSetupBusy)];

    internal static string CampaignSetupDeleteTitle => Text[nameof(CampaignSetupDeleteTitle)];

    internal static string CampaignSetupDeletePrompt(string name, int pictures, int scenes, int layouts, double megabytes, string directory) => Text.Format(nameof(CampaignSetupDeletePrompt), name, pictures, scenes, layouts, megabytes, directory);

    internal static string CampaignSetupNoMatch => Text[nameof(CampaignSetupNoMatch)];

    internal static string NameOk => Text[nameof(NameOk)];

    internal static string NameCancel => Text[nameof(NameCancel)];

    internal static string NameEmpty => Text[nameof(NameEmpty)];

    internal static string NameForbiddenCharacter => Text[nameof(NameForbiddenCharacter)];

    internal static string NameReserved => Text[nameof(NameReserved)];

    internal static string NameBadEnding => Text[nameof(NameBadEnding)];

    internal static string NameTooLong(int maxLength) => Text.Format(nameof(NameTooLong), maxLength);

    internal static string AdoptTitle => Text[nameof(AdoptTitle)];

    internal static string AdoptCandidate(string name, int screens) => Text.Format(nameof(AdoptCandidate), name, screens);

    internal static string AdoptIntro(string name, string address, string code) => Text.Format(nameof(AdoptIntro), name, address, code);

    internal static string AdoptTakesOver => Text[nameof(AdoptTakesOver)];

    internal static string AdoptRisk => Text[nameof(AdoptRisk)];

    internal static string AdoptNoRestore => Text[nameof(AdoptNoRestore)];

    internal static string AdoptAssign => Text[nameof(AdoptAssign)];

    internal static string AdoptCancel => Text[nameof(AdoptCancel)];

    internal static string LayoutMappingTitle => Text[nameof(LayoutMappingTitle)];

    internal static string LayoutMappingIntro(string name) => Text.Format(nameof(LayoutMappingIntro), name);

    internal static string LayoutMappingLoad => Text[nameof(LayoutMappingLoad)];

    internal static string LayoutMappingCancel => Text[nameof(LayoutMappingCancel)];

    internal static string LayoutMappingAsScene => Text[nameof(LayoutMappingAsScene)];

    internal static string LayoutMappingSkip => Text[nameof(LayoutMappingSkip)];

    internal static string LayoutMappingNotHere(string screen) => Text.Format(nameof(LayoutMappingNotHere), screen);

    internal static string SettingsStockThresholdInvalid => Text[nameof(SettingsStockThresholdInvalid)];

    internal static string SettingsFolderUnreachable(string path) => Text.Format(nameof(SettingsFolderUnreachable), path);
}
