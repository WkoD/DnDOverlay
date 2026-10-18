namespace DnDOverlay.Core.Tests;

/// <summary>
/// A drop names a picture by address more often than by file: a browser, and VS Code's Markdown
/// preview, hand over no file and often no text. The tests build their addresses from a real file
/// in the temp folder, so the same assertions hold on Windows and Linux.
/// </summary>
public sealed class DroppedAddressTests : IDisposable
{
    private readonly string _picture = Path.Combine(Path.GetTempPath(), $"dropped-{Guid.NewGuid():N}.png");

    public DroppedAddressTests() => File.WriteAllBytes(_picture, [1, 2, 3]);

    public void Dispose() => File.Delete(_picture);

    /// <summary>The address VS Code's preview gives a file on disk, drive colon escaped.</summary>
    private string WebviewAddress()
    {
        var path = _picture.Replace('\\', '/');
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        return "https://file+.vscode-resource.vscode-cdn.net/" + escaped.TrimStart('/');
    }

    [Fact]
    public void A_VS_Code_preview_address_names_the_file_on_disk() =>
        AssertNames(DroppedAddress.LocalPath(WebviewAddress()));

    [Fact]
    public void A_file_URI_names_its_file() =>
        AssertNames(DroppedAddress.LocalPath(new Uri(_picture).AbsoluteUri));

    [Fact]
    public void A_bare_absolute_path_is_its_own_file() =>
        AssertNames(DroppedAddress.LocalPath(_picture));

    /// <summary>The counter-case: an ordinary web address stays an address, to be fetched and guarded.</summary>
    [Theory]
    [InlineData("https://example.org/ork.png")]
    [InlineData("http://192.168.178.20/ork.png")]
    [InlineData("not an address at all")]
    [InlineData("ork.png")]
    public void Anything_else_names_no_local_file(string candidate) =>
        Assert.Null(DroppedAddress.LocalPath(candidate));

    /// <summary>
    /// A picture dragged out of a Chromium surface: no text, the address in the URL format and in
    /// the <c>src</c> of its HTML fragment. That is the drop that used to be refused outright.
    /// </summary>
    [Fact]
    public void A_dragged_picture_without_text_still_names_its_address()
    {
        var address = WebviewAddress();
        var html = $"Version:0.9\r\nStartHTML:0\r\n<html><body><!--StartFragment--><img alt=\"Ork\" src=\"{address.Replace("&", "&amp;", StringComparison.Ordinal)}\"><!--EndFragment--></body></html>";

        var found = DroppedAddress.Candidates(text: null, urlFormat: null, uriList: null, html);

        Assert.Equal([address], found);
    }

    /// <summary>The order is the order of deliberateness, and nothing is offered twice.</summary>
    [Fact]
    public void Candidates_come_most_deliberate_first_and_once_each()
    {
        var found = DroppedAddress.Candidates(
            text: "https://a.example/1.png",
            urlFormat: "https://b.example/2.png\nA title",
            uriList: "# comment\r\nhttps://a.example/1.png\r\nhttps://c.example/3.png",
            html: "<a href=\"https://d.example/4.png\"><img src=\"https://e.example/5.png\"></a><abbr src=\"x\">");

        Assert.Equal(
            ["https://a.example/1.png", "https://b.example/2.png", "https://c.example/3.png", "https://e.example/5.png", "https://d.example/4.png"],
            found);
    }

    private void AssertNames(string? path)
    {
        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"'{path}' should name the file at '{_picture}'.");
        Assert.Equal(Path.GetFullPath(_picture), Path.GetFullPath(path), ignoreCase: OperatingSystem.IsWindows());
    }
}
