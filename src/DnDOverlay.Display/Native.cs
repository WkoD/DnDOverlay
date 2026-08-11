using System.Runtime.InteropServices;

namespace DnDOverlay.Display;

/// <summary>
/// The Win32 surface that CHANGES a window of our own - and only that.
/// <para>
/// What the operating system is merely asked lives in <c>DnDOverlay.Platform.Windows</c>,
/// because the control has to ask the same questions and both processes must arrive at
/// character-identical answers (Part 2). Styles and placement of our overlay stay here: they
/// belong to the window, and nobody else has one.
/// </para>
/// </summary>
internal static partial class Native
{
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;

    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint newLong);
}
