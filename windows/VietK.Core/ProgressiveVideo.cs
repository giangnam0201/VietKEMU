using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VietK.Core;

// A loopback-only growing-file stream. Temporary EOF waits for more bytes;
// it never tells the decoder that a partially downloaded song has ended.
public sealed class ProgressiveVideo : IDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop;
    private readonly string file;
    private readonly string route="/"+Guid.NewGuid().ToString("N")+"/video.ts";
    private readonly Task accepting;
    private bool finished;
    public string Url { get; }
    public Task<string> Completion { get; }
    public long Received => File.Exists(file)?new FileInfo(file).Length:0;
    public ProgressiveVideo(string file,Func<Stream,CancellationToken,Task> writer,CancellationToken cancellation)
    {
        this.file=Path.GetFullPath(file);Directory.CreateDirectory(Path.GetDirectoryName(this.file)!);
        stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+route;
        accepting=Accept();Completion=Write(writer);
    }
    private async Task<string> Write(Func<Stream,CancellationToken,Task> writer)
    {
        try
        {
            await using(var output=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete,65536,true))
            { await writer(output,stop.Token);await output.FlushAsync(stop.Token); }
            return file;
        }
        finally { Volatile.Write(ref finished,true); }
    }
    public async Task WaitUntilReady(CancellationToken cancellation)
    {
        while(Received<256*1024 && !Completion.IsCompleted)await Task.Delay(50,cancellation);
        if(Completion.IsCompleted)await Completion;
        if(Received==0)throw new IOException("Video transfer did not produce playable bytes");
    }
    private async Task Accept()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            { var socket=await listener.AcceptTcpClientAsync(stop.Token);_=Serve(socket); }
        }
        catch(OperationCanceledException) { }
        catch(SocketException) when(stop.IsCancellationRequested) { }
    }
    private async Task Serve(TcpClient socket)
    {
        using(socket)
        try
        {
            var token=stop.Token;await using var network=socket.GetStream();
            using var header=new StreamReader(network,Encoding.ASCII,false,1024,true);
            var first=await header.ReadLineAsync(token);
            while(!string.IsNullOrEmpty(await header.ReadLineAsync(token))) { }
            if(first is null || (!first.StartsWith("GET "+route+" ") && !first.StartsWith("HEAD "+route+" ")))
            { await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"),token);return; }
            await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nTransfer-Encoding: chunked\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"),token);
            if(first.StartsWith("HEAD "))return;
            while(!File.Exists(file)) { if(finished)return;await Task.Delay(50,token); }
            await using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,65536,true);
            var buffer=new byte[65536];
            while(true)
            {
                var count=await input.ReadAsync(buffer,token);
                if(count==0)
                { if(Volatile.Read(ref finished))break;await Task.Delay(30,token);continue; }
                await network.WriteAsync(Encoding.ASCII.GetBytes(count.ToString("X")+"\r\n"),token);
                await network.WriteAsync(buffer.AsMemory(0,count),token);
                await network.WriteAsync("\r\n"u8.ToArray(),token);
            }
            await network.WriteAsync("0\r\n\r\n"u8.ToArray(),token);
        }
        catch(Exception error) when(error is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public void Dispose() { stop.Cancel();listener.Stop(); }
}
