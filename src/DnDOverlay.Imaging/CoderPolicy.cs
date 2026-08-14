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
/// list still wrote. Nothing is reported, nothing throws, and only the hardening is missing.
/// </para>
/// <para>
/// Two guards, because there are two ways to end up unhardened, and they need different answers.
/// <see cref="EnsureApplied"/> catches the FORGOTTEN policy: every entrance to the codec passes
/// through it, so a missing call fails loudly at the first image rather than never. <see
/// cref="Apply"/> catches the LATE one, which is the more dangerous of the two and the one
/// measured here - it would sail past any check for "was Apply called?", because it was. So Apply
/// does not report success on the strength of having run; it touches a denied coder and treats
/// anything but a refusal as a failure.
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
    /// Applies the policy, once per process, and then PROVES that it took effect. Calling it again
    /// is harmless and does nothing - there is one policy text, so a second call cannot widen
    /// anything.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The policy was set but has no effect, which means ImageMagick was already in use.
    /// </exception>
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

            ProveItTookEffect();

            _applied = true;
        }
    }

    /// <summary>
    /// Touches a coder that MUST be refused, and treats anything else as a failure.
    /// <para>
    /// This closes the hole that <see cref="EnsureApplied"/> does not: the dangerous case is not a
    /// forgotten policy but a LATE one. Measured - applied after a single image had been
    /// constructed, the policy had no effect whatsoever, and a check for "was Apply called?" would
    /// have waved it through. ImageMagick offers no way to ask whether it has already been
    /// initialised, so the only honest question is the one asked here: does the policy bite?
    /// </para>
    /// <para>
    /// The probe has to be a coder OUR list denies and Magick's own defaults allow, and finding
    /// that out took a measurement. The obvious choice was MVG - the scripting coder this whole
    /// construction is about - and it is the wrong one: <b>Magick.NET already refuses MVG and MSL
    /// by default</b>, with the same policy exception. A probe on MVG therefore passes whether our
    /// policy took effect or not, and the guard was silently useless until this was measured.
    /// </para>
    /// <para>
    /// MIFF is ImageMagick's own native format: never absent, harmless, written in microseconds,
    /// and off our list. Measured, it writes 462 bytes without our policy and is refused with it -
    /// so it can tell the two apart, which is the only property that matters here.
    /// </para>
    /// </summary>
    private static void ProveItTookEffect()
    {
        try
        {
            using var probe = new MagickImage(MagickColors.Black, 1, 1);
            probe.ToByteArray(MagickFormat.Miff);
        }
        catch (MagickPolicyErrorException)
        {
            return;
        }
        catch (MagickException ex)
        {
            // Refused, but not by the policy. That is not the answer we asked for - and the repair
            // is to move the probe to another denied coder, never to drop the check.
            throw new InvalidOperationException(
                "The coder policy could not be proved: the probe was refused by something other "
                + "than the policy. Pick another coder that this build writes and our list denies, "
                + "rather than dropping the check (Part 5).",
                ex);
        }

        throw new InvalidOperationException(
            "The ImageMagick coder policy was set but has NO effect - a denied coder still ran. "
            + "This happens when ImageMagick was already used in this process: the policy is "
            + "process-wide state and must be applied before the first image operation (Part 5).");
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
