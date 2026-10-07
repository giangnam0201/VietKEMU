using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Arirang.Core;

internal static class HttpRangeChecks
{
    private sealed class Handler(string mode = "valid") : HttpMessageHandler
    {
        internal int Calls;
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var range = request.Headers.Range?.Ranges.Single() ?? throw new Exception("Missing Range header.");
            long start = range.From!.Value, end = range.To!.Value;
            var response = new HttpResponseMessage(mode == "full" ? HttpStatusCode.OK : HttpStatusCode.PartialContent);
            int size = checked((int)(end - start + 1));
            var bytes = Enumerable.Range(0, mode.StartsWith("short") ? size - 1 : mode.StartsWith("long") ? size + 1 : size)
                .Select(i => (byte)(start + i)).ToArray();
            response.Content = new ByteArrayContent(bytes);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(mode == "wrong" ? start + 1 : start,
                mode == "wrong" ? end + 1 : end, mode == "total" ? 32 * 1024 * 1024 + 1 : 32 * 1024 * 1024);
            if (mode.EndsWith("body")) response.Content.Headers.ContentLength = size;
            if (mode == "encoded") response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    internal static void Run()
    {
        var handler = new Handler(); using var http = new HttpClient(handler);
        using var stream = new HttpRangeStream(http, new("https://example.invalid/disc.iso"), 32 * 1024 * 1024);
        stream.Position = 123; var data = new byte[16]; stream.ReadExactly(data);
        if (!data.SequenceEqual(Enumerable.Range(123, 16).Select(i => (byte)i)) || stream.Position != 139)
            throw new Exception("Remote seek/read returned wrong bytes.");
        stream.Position = 123; stream.ReadExactly(data);
        if (handler.Calls != 1 || stream.BytesFetched != 16 || stream.Requests != 1)
            throw new Exception("Repeated disc ranges must reuse the bounded cache.");
        stream.Seek(-16, SeekOrigin.End); stream.ReadExactly(data);
        if (stream.Read(data, 0, data.Length) != 0) throw new Exception("Remote EOF must be empty.");
        foreach (string mode in new[] { "full", "wrong", "total", "short", "long", "shortbody", "longbody", "encoded" })
        {
            using var badHttp = new HttpClient(new Handler(mode));
            using var bad = new HttpRangeStream(badHttp, new("https://example.invalid/disc.iso"), 32 * 1024 * 1024);
            bool rejected = false;
            try { bad.ReadExactly(data); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Unsupported remote range response was accepted: " + mode);
        }
        var chunk = new byte[HttpRangeStream.MaximumRequestBytes + 1]; stream.Position = 0;
        int read = stream.Read(chunk, 0, chunk.Length);
        if (read != HttpRangeStream.MaximumRequestBytes) throw new Exception("Remote reads exceed request bound.");
        bool limit = false;
        try { for (int i = 1; i <= 16; i++) { stream.Position = i * (long)HttpRangeStream.MaximumRequestBytes; stream.Read(chunk, 0, chunk.Length); } }
        catch (InvalidDataException) { limit = true; }
        if (!limit) throw new Exception("Remote transfer budget was not enforced.");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        using var canceled = new HttpRangeStream(http, new("https://example.invalid/disc.iso"), 32 * 1024 * 1024, stop.Token);
        bool stopped = false;
        try { canceled.Read(data, 0, 1); } catch (OperationCanceledException) { stopped = true; }
        if (!stopped) throw new Exception("Remote reads must honor cancellation.");
    }

    internal static void ExportOriginal(string output)
    {
        using var http = new HttpClient();
        using var stream = new HttpRangeStream(http, new("https://archive.org/download/midi-vision-karaoke-vol-40/UNDEFINED.iso"), 3800203264);
        var inventory = DiscInventory.Read(stream);
        var index = MultakIndex.ReadIso(stream, inventory);
        var catalogue = MasecoIndex.ReadIso(stream, inventory);
        var supported = catalogue.Where(s => s.SupportedTitle is not null && index.FindSong(s.DeviceCode) is not null).ToArray();
        var entry = catalogue.Single(s => s.DeviceCode == 50001);
        var raw = index.ReadSongRecordIso(stream, inventory, entry.DeviceCode);
        var song = MultakPlaybackSong.Parse(raw, "Original remote verification", entry.LanguageId);
        var font = MultakBitmapFont.ReadIso(stream, inventory, entry.LanguageId);
        if (supported.Length != 9001 || raw.Length != 14336 || song.Lyrics.Count != 1194 ||
            song.Messages.Count != 5426 || song.Notice is not null || font is null ||
            stream.BytesFetched > 2 * 1024 * 1024 || stream.BytesFetched < 1_000_000)
            throw new InvalidDataException("Remote original-disc decode differs from bounded local verification.");
        long fetched = stream.BytesFetched;
        if (!index.ReadSongRecordIso(stream, inventory, entry.DeviceCode).SequenceEqual(raw) || stream.BytesFetched != fetched)
            throw new InvalidDataException("Repeated remote song reads must reuse their cached range.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            originalRemoteDiscRead = true, rangeResponsesValidated = true,
            supportedCatalogueSongs = supported.Length, selectedSongCode = entry.DeviceCode,
            selectedSongRecordBytes = raw.Length, originalNoteEvents = song.Messages.Count,
            originalLyricGlyphs = song.Lyrics.Count, originalFontLoaded = true,
            totalBytesFetched = stream.BytesFetched, requests = stream.Requests,
            repeatSongRangeCached = true, fullIsoDownloaded = false,
            audiblePlaybackVerified = false, originalInstrumentsVerified = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
