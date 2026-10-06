using System.Net;
using System.Net.Http;
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
    public async Task<string> Download(int songId,string url,string directory,
        Action<long,long> progress,CancellationToken cancellation)
    {
        if(songId<=0)throw new ArgumentOutOfRangeException(nameof(songId));
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https"))
            throw new OriginalTransferException(1001,"Invalid music URL");
        Directory.CreateDirectory(directory);
        var filename=songId+(url.Contains(".mp3",StringComparison.Ordinal)?".mp3":".ts");
        var temporary=Path.Combine(directory,filename+".tmp");
        var destination=Path.Combine(directory,filename);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode)throw new OriginalTransferException(1002,"Music server HTTP "+(int)response.StatusCode);
            var total=response.Content.Headers.ContentLength;
            if(total is null or <=0)throw new OriginalTransferException(1003,"Music server did not provide a valid file length");
            await using var input=await response.Content.ReadAsStreamAsync(cancellation);
            await using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,65536,true))
            {
                var buffer=new byte[65536];long written=0;
                while(true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    var count=await input.ReadAsync(buffer,timeout.Token);
                    if(count==0)break;
                    await output.WriteAsync(buffer.AsMemory(0,count),cancellation);
                    written+=count;progress(written,total.Value);
                }
                await output.FlushAsync(cancellation);
                if(written<total.Value)throw new OriginalTransferException(1014,"Incomplete music file");
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary,destination,true);
            return destination;
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
        { throw new OriginalTransferException(1007,"Music server read timed out"); }
        catch(HttpRequestException ex) { throw new OriginalTransferException(1002,"Music server connection failed",ex); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public void Dispose()=>client.Dispose();
}
