using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DnDOverlay.Transport.Tests;

/// <summary>What the test server answers with.</summary>
/// <param name="Status">The status line's code.</param>
/// <param name="Body">The bytes, if any.</param>
/// <param name="ContentType">Announced type; <c>null</c> announces none at all.</param>
/// <param name="Location">Set for a redirect.</param>
/// <param name="DeclareLength">
/// Whether to send <c>Content-Length</c>. Off means the body ends with the connection - the case in
/// which the byte ceiling can only be kept while reading.
/// </param>
/// <param name="Delay">How long to wait before answering, for the time budget.</param>
internal sealed record Reply(
    int Status = 200,
    byte[]? Body = null,
    string? ContentType = "image/png",
    string? Location = null,
    bool DeclareLength = true,
    TimeSpan Delay = default);

/// <summary>
/// A web server of about forty lines, speaking just enough HTTP to answer one request per
/// connection.
/// <para>
/// <b>Hand-written rather than a framework</b>, because every test here needs a server that behaves
/// BADLY on purpose - a redirect loop, a page where a picture was promised, a body with no length
/// that never stops. Those are hard to arrange in a server built to be correct and trivial in one
/// that just writes bytes.
/// </para>
/// </summary>
internal sealed class TestWebHost : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, Reply> _answer;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;
    private int _requests;

    internal TestWebHost(Func<string, Reply> answer)
    {
        _answer = answer;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serving = ServeAsync();
    }

    /// <summary>The port it came up on - always a free one, so tests can run side by side.</summary>
    internal int Port { get; }

    /// <summary>How many requests actually arrived. Zero is the assertion for "not a single byte".</summary>
    internal int Requests => Volatile.Read(ref _requests);

    /// <summary>The address of <paramref name="path"/> on this server.</summary>
    internal string At(string path) => $"http://127.0.0.1:{Port}{path}";

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();

        try
        {
            await _serving;
        }
        catch (OperationCanceledException)
        {
            // The way a listener stops.
        }

        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient connection;

            try
            {
                connection = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                return;
            }

            _ = AnswerAsync(connection);
        }
    }

    private async Task AnswerAsync(TcpClient connection)
    {
        using (connection)
        {
            try
            {
                var stream = connection.GetStream();
                var request = await ReadRequestAsync(stream);

                if (request is null)
                {
                    return;
                }

                Interlocked.Increment(ref _requests);

                var reply = _answer(request);

                if (reply.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(reply.Delay, _stopping.Token);
                }

                await WriteAsync(stream, reply);
            }
            catch (Exception)
            {
                // A client that walked away mid-answer is the normal end of half these tests.
            }
        }
    }

    /// <summary>Reads up to the blank line and hands back the path from the request line.</summary>
    private static async Task<string?> ReadRequestAsync(NetworkStream stream)
    {
        var text = new StringBuilder();
        var chunk = new byte[1024];

        while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(chunk);

            if (read == 0)
            {
                return null;
            }

            text.Append(Encoding.ASCII.GetString(chunk, 0, read));
        }

        var line = text.ToString().Split("\r\n")[0].Split(' ');

        return line.Length < 2 ? null : line[1];
    }

    private static async Task WriteAsync(NetworkStream stream, Reply reply)
    {
        var body = reply.Body ?? [];
        var head = new StringBuilder();

        head.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {reply.Status} Answer\r\n");

        if (reply.ContentType is { } type)
        {
            head.Append(CultureInfo.InvariantCulture, $"Content-Type: {type}\r\n");
        }

        if (reply.Location is { } location)
        {
            head.Append(CultureInfo.InvariantCulture, $"Location: {location}\r\n");
        }

        if (reply.DeclareLength)
        {
            head.Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n");
        }

        head.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }
}
