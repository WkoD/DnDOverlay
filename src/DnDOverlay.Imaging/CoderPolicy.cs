using ImageMagick;
using ImageMagick.Configuration;

namespace DnDOverlay.Imaging;

/// <summary>
/// ImageMagick's security policy in its safe shape: deny everything, then allow the raster coders,
/// and forbid delegates without exception (Part 5).
/// <para>
/// It is not a setting on an image but PROCESS-WIDE STATE, and it has to stand before the first
/// image operation. Measured, because the failure is silent: applied after a single
/// <c>MagickImage</c> had been constructed, the policy had NO effect at all - a coder left off the
/// list still wrote. Nothing is reported, nothing throws, and only the hardening is missing. That
/// is the whole reason for <see cref="EnsureApplied"/>: a forgotten policy must fail loudly at the
/// first image rather than never.
/// </para>
/// <para>
/// The owner is the application, in its start-up before the first call into the codec - the same
/// place the data root and the secret store are assembled. Not Campaign: it sees the interface
/// only and must know nothing of Magick (rule 8). In test processes the assembly fixture applies
/// it before the first generated file, because generator and codec share one process there and the
/// allowed coders have to suffice for writing as well as reading (Part 5, Part 10).
/// </para>
/// </summary>
public static class CoderPolicy
{
    /// <summary>
    /// Deny all, then allow raster coders by name. A blocklist of the dangerous ones would be the
    /// exact mistake the positive list exists to avoid - it loses its effect silently on a library
    /// bump (Part 5).
    /// <para>
    /// Three entries on the list are not obvious and are deliberate:
    /// </para>
    /// <para>
    /// <c>XC</c> is the solid-colour canvas. Without it nothing can be SYNTHESISED, and the test
    /// data generator cannot produce a single file - measured, it fails on the first image. It
    /// neither reads a file nor fetches anything, so allowing it costs nothing.
    /// </para>
    /// <para>
    /// <c>HEIC</c> and <c>HEIF</c> stay allowed although we reject that format. The rejection is a
    /// legal one (HEVC patents) and has to be OUR explicit entry with our reason - denied here it
    /// would fail as a policy error instead, and the check "rejected although the build can read
    /// it" would pass for the wrong reason (Part 5, Part 11).
    /// </para>
    /// <para>
    /// <c>APNG</c> is absent, and it cannot be added: measured, the APNG coder runs through the
    /// <c>ffmpeg</c> delegate in BOTH directions, and delegates are forbidden without exception.
    /// An APNG is a valid PNG, so it arrives through the PNG coder as its first frame - which is
    /// what Part 5 already foresaw for it.
    /// </para>
    /// </summary>
    private const string PolicyXml = """
        <policymap>
          <policy domain="delegate" rights="none" pattern="*"/>
          <policy domain="coder" rights="none" pattern="*"/>
          <policy domain="coder" rights="read|write" pattern="{XC,PNG,PNG8,PNG24,PNG32,JPEG,JPG,PJPEG,GIF,GIF87,BMP,BMP2,BMP3,DIB,WEBP,AVIF,TIFF,TIF,ICO,ICON,CUR,PNM,PBM,PGM,PPM,PAM,PSD,PSB,TGA,ICB,VDA,VST,DDS,SVG,SVGZ,MSVG,JXL,XCF,PCX,DCX,JP2,J2K,JPC,JPT,QOI,HEIC,HEIF}"/>
        </policymap>
        """;

    private static readonly Lock Gate = new();
    private static bool _applied;

    /// <summary>
    /// Applies the policy, once per process. Calling it again is harmless and does nothing - there
    /// is one policy text, so a second call cannot widen anything.
    /// <para>
    /// It must run before ANY use of ImageMagick in this process. That cannot be asserted from
    /// here: ImageMagick offers no way to ask whether it has already been initialised, and a
    /// policy applied too late fails silently rather than loudly.
    /// </para>
    /// </summary>
    public static void Apply()
    {
        lock (Gate)
        {
            if (_applied)
            {
                return;
            }

            var files = ConfigurationFiles.Default;
            files.Policy.Data = PolicyXml;
            MagickNET.Initialize(files);

            _applied = true;
        }
    }

    /// <summary>
    /// Throws unless <see cref="Apply"/> has run. Every entry point of the codec passes through
    /// here, and that is the point rather than a formality: a forgotten policy would otherwise
    /// never show up - everything keeps working, only the hardening that separates the URL import
    /// from a remote control is missing (Part 5).
    /// </summary>
    /// <exception cref="InvalidOperationException">The policy has not been applied.</exception>
    public static void EnsureApplied()
    {
        lock (Gate)
        {
            if (!_applied)
            {
                throw new InvalidOperationException(
                    "The ImageMagick coder policy has not been applied. Call CoderPolicy.Apply() "
                    + "in application start-up before the first image operation (Part 5).");
            }
        }
    }
}
