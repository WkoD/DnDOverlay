using System.Collections.Immutable;

namespace DnDOverlay.TestData;

/// <summary>
/// What one run of the generator produced: where the files are, which formats this build carried,
/// and which tolerated ones it could not write.
/// </summary>
/// <param name="Directory">The directory the files were written into.</param>
/// <param name="Promised">Format name to file path, for every promised format (Part 5).</param>
/// <param name="Tolerated">
/// Format name to file path, for the tolerated formats this build could write. Free to differ per
/// platform - nothing is asserted about them, so nothing can diverge (Part 5).
/// </param>
/// <param name="SkippedTolerated">
/// Tolerated formats this build cannot write. Reported, never a failure - the counterpart to the
/// hard stop on a promised one.
/// </param>
/// <param name="Crafted">The files built byte by byte, without Magick.</param>
/// <param name="Tokens">The MapTool token containers, in both generations.</param>
public sealed record TestAssetSet(
    string Directory,
    ImmutableDictionary<string, string> Promised,
    ImmutableDictionary<string, string> Tolerated,
    ImmutableArray<string> SkippedTolerated,
    CraftedSet Crafted,
    TokenSet Tokens);

/// <summary>
/// The five cases Part 10 asks for, plus the second container GENERATION and the two ways an
/// extension can lie. All of them come into being the same way rather than two by hand and the
/// rest derived from them.
/// </summary>
/// <param name="WithPortrait">
/// Portrait and map token, in the shape of a 2024 token. The portrait is told apart by its
/// measurements, 400x600 against the map token's 100x100 - never by a hash and never by a MapTool
/// version, so the token may be rebuilt at any time without the assertion turning false.
/// </param>
/// <param name="Legacy">
/// The same content in the shape of a 2009 token: no entry carrying an extension, and the image
/// base64-encoded INSIDE the note. An unpacker written to Part 5's table alone answers "not found"
/// here - which is the correction this file exists to hold in place.
/// </param>
/// <param name="WithoutPortrait">Map token only: the fallback, not a rejection.</param>
/// <param name="WithoutImage">Neither: a rejection with a reason, never a wrong image.</param>
/// <param name="ZipBomb">A few kilobytes on disk, 32 MB of <c>content.xml</c> unpacked.</param>
/// <param name="ExternalEntity">
/// A DTD that reads a local file and reaches the network - both shut by prohibiting DTDs and by a
/// null resolver, which this file proves rather than assumes.
/// </param>
/// <param name="Renamed">A genuine token under a <c>.zip</c> name.</param>
/// <param name="Foreign">An ordinary archive under the token name.</param>
public sealed record TokenSet(
    string WithPortrait,
    string Legacy,
    string WithoutPortrait,
    string WithoutImage,
    string ZipBomb,
    string ExternalEntity,
    string Renamed,
    string Foreign);

/// <summary>
/// The files that are pure byte work: they must be malformed, forged or dangerous in a way no
/// encoder would produce, so an encoder is the wrong tool for them (Part 10).
/// </summary>
/// <param name="ScriptDisguisedAsPng">
/// An MVG script named <c>.png</c>. The decisive case: the decision must fall on the CONTENT, and
/// the policy has to hold even when our own format check is skipped (Part 5, Part 11).
/// </param>
/// <param name="SvgWithExternalReference">
/// An SVG that points at an outside resource. If it ever loads it, SVG leaves both the promise and
/// the allowed coders (Part 5).
/// </param>
/// <param name="WrongExtension">A PNG carrying a <c>.jpg</c> name.</param>
/// <param name="Truncated">A PNG cut off mid-file.</param>
/// <param name="HeicStub">
/// A file that declares itself HEIC through its <c>ftyp</c> brand and contains nothing else. The
/// rejection has to bite at the DECLARATION, before any decoding - which makes the stub the more
/// precise test, not the cheaper one (Part 10).
/// </param>
/// <param name="ForgedHeaderBomb">
/// A PNG whose header claims 60000x60000. It tests the REAL production limits, and it is 69 bytes:
/// the expensive case gives the cheapest test.
/// <para>
/// Measured, against the plan's "and almost nothing after it": header and end marker alone are NOT
/// enough - the PNG reader then refuses as truncated BEFORE it ever reports a size, and the test
/// would pass without touching the dimension gate. One tiny <c>IDAT</c> chunk is what makes the
/// forged size readable.
/// </para>
/// </param>
/// <param name="SmallBomb">
/// A 2000x2000 bomb for the second net, where the MECHANISM is the subject and not the number: the
/// test sets the limits deliberately small, and this one then breaks them as reliably as a 60000
/// one breaks the real ones. A full-size bomb exists nowhere in the automated stock - building it
/// would push some 3.6 GB through a deflate stream on every single <c>dotnet test</c>.
/// </param>
/// <param name="DecodedBomb">
/// The same bomb in a format that is DECODED on the way in. Since M2b the PNG path hands its bytes
/// through untouched (the re-encode cost 11.6 s on a real 24 MB picture), so <see cref="SmallBomb"/>
/// never reaches the decode limits at all - and a test that still used one for them would measure
/// nothing while looking exactly as green.
/// </param>
/// <param name="ManyFrames">
/// Six hundred frames of one pixel - the bomb in the FRAME dimension, which sits under every byte
/// and pixel limit and decodes like six hundred pictures (Part 5).
/// </param>
public sealed record CraftedSet(
    string ScriptDisguisedAsPng,
    string SvgWithExternalReference,
    string WrongExtension,
    string Truncated,
    string HeicStub,
    string ForgedHeaderBomb,
    string SmallBomb,
    string DecodedBomb,
    string ManyFrames);
