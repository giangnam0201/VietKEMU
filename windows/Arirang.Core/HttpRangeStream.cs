using System.Net;
using System.Net.Http.Headers;

namespace Arirang.Core;

// Seekable, bounded view of a remote ISO. Use on a worker thread; rejected
// 200/full-file responses are disposed before reading their body.
public sealed class HttpRangeStream : Stream
{
    public const int MaximumRequestBytes = 1024 * 1024;
    public const long MaximumTransferBytes = 16 * 1024 * 1024;
    private readonly HttpClient http;
    private readonly Uri uri;
    private readonly CancellationToken cancellation;
    private readonly Dictionary<(long Offset, int Bytes), byte[]> cache = [];
    private readonly long length;
    private long position;
    private bool disposed;
    public long BytesFetched { get; private set; }
    public int Requests { get; private set; }

    public HttpRangeStream(HttpClient http, Uri uri, long length, CancellationToken cancellation = default)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || length is <= 0 or > 64L * 1024 * 1024 * 1024)
            throw new ArgumentException("Unsupported remote disc URL or size.");
        this.http = http; this.uri = uri; this.length = length; this.cancellation = cancellation;
    }
    public override bool CanRead => !disposed;
    public override bool CanSeek => !disposed;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => position;
        set { ObjectDisposedException.ThrowIf(disposed, this); if (value < 0 || value > length) throw new ArgumentOutOfRangeException(nameof(value)); position = value; }
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
        ObjectDisposedException.ThrowIf(disposed, this); cancellation.ThrowIfCancellationRequested();
        int size = (int)Math.Min(Math.Min(count, MaximumRequestBytes), length - position);
        if (size == 0) return 0;
        var key = (position, size);
        if (!cache.TryGetValue(key, out byte[]? bytes))
        {
            if (BytesFetched > MaximumTransferBytes - size) throw new InvalidDataException("Remote disc transfer exceeds bounded operation limit.");
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            stop.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(position, position + size - 1);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            using var response = http.Send(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range is null || range.Unit != "bytes" ||
                range.From != position || range.To != position + size - 1 || range.Length != length ||
                response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentLength is long actual && actual != size)
                throw new InvalidDataException("Server did not return the exact requested disc range.");
            bytes = new byte[size];
            using var body = response.Content.ReadAsStream(stop.Token);
            int received = 0;
            while (received < size)
            {
                int read = body.ReadAsync(bytes.AsMemory(received), stop.Token).GetAwaiter().GetResult();
                if (read == 0) throw new InvalidDataException("Truncated remote disc range.");
                received += read;
            }
            var extra = new byte[1];
            if (body.ReadAsync(extra.AsMemory(), stop.Token).GetAwaiter().GetResult() != 0)
                throw new InvalidDataException("Oversized remote disc range.");
            BytesFetched += size; Requests++; cache.Add(key, bytes);
        }
        bytes.CopyTo(buffer, offset); position += size; return size;
    }
    public override int Read(Span<byte> buffer)
    {
        var bytes = new byte[Math.Min(buffer.Length, MaximumRequestBytes)];
        int count = Read(bytes, 0, bytes.Length); bytes.AsSpan(0, count).CopyTo(buffer); return count;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = checked(offset + (origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position,
            SeekOrigin.End => length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }));
        return position;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { disposed = true; if (disposing) cache.Clear(); base.Dispose(disposing); }
}
