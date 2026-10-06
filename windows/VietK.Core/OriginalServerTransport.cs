using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace VietK.Core;

// Native libsign-lib.so sign(): JNI modified UTF-8, snprintf(...,64,"%s:%s"), MD5.
// The caller supplies its real device identity; no registration/token fallback.
public static class OriginalRequestSignature
{
    public static string Sign(string chipId,string salt)
    {
        static byte[] ModifiedUtf8(string value)
        {
            var bytes=new List<byte>();
            foreach(var c in value)
                if(c is > '\0' and < '\u0080')bytes.Add((byte)c);
                else if(c<'\u0800') { bytes.Add((byte)(0xc0|(c>>6)));bytes.Add((byte)(0x80|(c&63))); }
                else { bytes.Add((byte)(0xe0|(c>>12)));bytes.Add((byte)(0x80|((c>>6)&63)));bytes.Add((byte)(0x80|(c&63))); }
            return bytes.ToArray();
        }
        var input=ModifiedUtf8(chipId+":"+salt);
        return Convert.ToHexString(MD5.HashData(input.AsSpan(0,Math.Min(input.Length,63)))).ToLowerInvariant();
    }
}

public sealed class OriginalServerTransport : IDisposable
{
    private readonly HttpClient client=new(new SocketsHttpHandler {
        AutomaticDecompression=DecompressionMethods.GZip,ConnectTimeout=TimeSpan.FromSeconds(10) })
        { Timeout=TimeSpan.FromSeconds(10) };
    public string Post(DataCenterPost post)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,post.Url);
        // Original Apache UrlEncodedFormEntity: JSON is in the form field "body".
        request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"body",post.Body}});
        foreach(var header in post.Headers)
            if(!request.Headers.TryAddWithoutValidation(header.Key,header.Value??""))
                request.Content.Headers.TryAddWithoutValidation(header.Key,header.Value??"");
        using var response=client.Send(request);
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Replace("\r","").Replace("\n","");
    }
    public void Dispose()=>client.Dispose();
}

public sealed class OriginalTransferException(int code,string message,Exception? inner=null):IOException(message,inner)
{ public int Code { get; }=code; }

// Windows file/HTTP adapter for LocalOnlineSongManager.downloadVideo. No media
// becomes playable until EOF, known Content-Length and completed-file checks.
public sealed class OriginalMusicTransfer : IDisposable
{
    private readonly HttpClient client=new(new SocketsHttpHandler { ConnectTimeout=TimeSpan.FromSeconds(10) })
        { Timeout=Timeout.InfiniteTimeSpan };
    // HttpFile.open(uri, 0, 3): three connection attempts, immediate stop on
    // 404, Range and identity encoding. This covers the original opening phase;
    // AppDownItem uses a separate, single-attempt open for each file write.
    private async Task<HttpResponseMessage> Open(Uri uri,CancellationToken cancellation,int attempts=3)
    {
        Exception? lastError=null;
        for(var attempt=0;attempt<attempts;attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.Range=new RangeHeaderValue(0,null);
            request.Headers.Accept.ParseAdd("*/*");
            request.Headers.AcceptEncoding.ParseAdd("identity");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                if(response.IsSuccessStatusCode)return response;
                var status=response.StatusCode;response.Dispose();
                lastError=new IOException("Music server HTTP "+(int)status);
                if(status==HttpStatusCode.NotFound)break;
            }
            catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
            { lastError=new IOException("Music server connection timed out"); }
            catch(HttpRequestException ex) { lastError=ex; }
        }
        // Original open closes failed connections before AppDownItem reads its
        // status; getResponseCode consequently returns -1, including on 404.
        throw new OriginalTransferException(1004,lastError?.Message??"Music server connection failed",lastError);
    }
    public async Task<string> Download(int songId,string url,string directory,
        Action<long,long> progress,CancellationToken cancellation,Action<int>? notification=null)
    {
        if(songId<=0)throw new ArgumentOutOfRangeException(nameof(songId));
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https"))
            throw new OriginalTransferException(1001,"Invalid music URL");
        directory=Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var filename=songId+(url.Contains(".mp3",StringComparison.Ordinal)?".mp3":".ts");
        var temporary=Path.Combine(directory,filename+".tmp");
        var destination=Path.Combine(directory,filename);
        try
        {
            long total;
            using(var probe=await Open(uri,cancellation))
            {
                total=probe.Content.Headers.ContentLength??0;
                if(total<=0)throw new OriginalTransferException(1003,"Music server did not provide a valid file length");
            }
            // LocalOnlineSongManager deletes the old temporary file before
            // handing this song to AppDownItem. Length probing is a separate
            // GET; each write attempt reopens with the current Range offset.
            try { using var empty=File.Create(temporary); }
            catch(IOException ex) { throw new OriginalTransferException(1001,"Cannot create music temporary file",ex); }
            for(var attempt=0;attempt<3;attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    await Write(uri,temporary,total,progress,cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(temporary,destination,true);
                    return destination;
                }
                catch(OriginalTransferException ex) when(ex.Code is 1007 or 1009)
                {
                    if(ex.Code==1009 && new FileInfo(temporary).Length==total)
                    { cancellation.ThrowIfCancellationRequested();File.Move(temporary,destination,true);return destination; }
                    if(File.Exists(temporary))File.Delete(temporary);
                    notification?.Invoke(1018);
                    if(attempt==2)throw;
                }
            }
            throw new InvalidOperationException("Write attempts exhausted without a result");
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    private async Task Write(Uri uri,string temporary,long total,Action<long,long> progress,CancellationToken cancellation)
    {
        using var response=await Open(uri,cancellation,1);
        await using var input=await response.Content.ReadAsStreamAsync(cancellation);
        await using var output=new FileStream(temporary,FileMode.Append,FileAccess.Write,FileShare.None,32768,true);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var buffer=new byte[32768];long written=output.Length;
        var lastProgress=System.Diagnostics.Stopwatch.StartNew();
        while(written<total)
        {
            int count;
            try
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                count=await input.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,total-written)),timeout.Token);
            }
            catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
            { throw new OriginalTransferException(1007,"Music server read timed out"); }
            catch(IOException ex) { throw new OriginalTransferException(1007,"Music server read failed",ex); }
            // HttpURLConnection/localRead's premature-EOF loop is not reproduced
            // by this Windows stream adapter. Keep incomplete bytes unplayable.
            if(count==0)throw new OriginalTransferException(1014,"Incomplete music file");
            try { await output.WriteAsync(buffer.AsMemory(0,count),cancellation); }
            catch(IOException ex) { throw new OriginalTransferException(1009,"Music cache write failed",ex); }
            written+=count;
            if(lastProgress.ElapsedMilliseconds>=2000 || written>=total)
            { progress(written,total);lastProgress.Restart(); }
        }
        try { await output.FlushAsync(cancellation); }
        catch(IOException ex) { throw new OriginalTransferException(1009,"Music cache flush failed",ex); }
    }
    public void Dispose()=>client.Dispose();
}
