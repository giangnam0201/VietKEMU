using System.Net;
using System.Net.Sockets;
using System.Text;
using VietK.Core;

static class TransferConnectionChecks
{
    public static async Task Run()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var address="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
        var directory=Path.Combine(Path.GetTempPath(),"vietk-recovery-"+Guid.NewGuid());
        var payload=Enumerable.Range(0,70001).Select(i=>(byte)(i*17)).ToArray();
        var requests=new List<string>();
        var userAgent=OriginalMusicTransfer.UserAgentFromDataCenter(
            "KTV-Plus/1.2.b57/1.9.1/android/1.0.0","Vietnam_V1.0","fixture-serial");
        if(userAgent!="KTV-Plus/1.2.b57/1.9.1/android/Vietnam_V1.0/fixture-serial" ||
            OriginalMusicTransfer.UserAgentFromDataCenter("custom-agent","Vietnam_V1.0","fixture-serial")!="")
            throw new InvalidDataException("Downloader header confused the service version with the controlling APK version");
        try
        {
            var serving=Task.Run(async()=>
            {
                for(var index=0;index<8;index++)
                {
                    using var socket=await listener.AcceptTcpClientAsync();
                    await using var stream=socket.GetStream();
                    using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                    requests.Add(await reader.ReadLineAsync()??throw new IOException("Missing GET"));
                    var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    string? line;
                    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))
                    { var colon=line.IndexOf(':');headers[line[..colon]]=line[(colon+1)..].Trim(); }
                    if(headers.GetValueOrDefault("Range")!="bytes=0-" ||
                        headers.GetValueOrDefault("Accept-Encoding")!="identity" ||
                        headers.GetValueOrDefault("Accept")!="*/*" ||
                        headers.GetValueOrDefault("User-Agent")!=userAgent)
                        throw new InvalidDataException("Original media HTTP headers differ");
                    var status=index is 2 or 3?"200 OK":index==7?"404 Not Found":"503 Service Unavailable";
                    var length=index is 2 or 3?payload.Length:0;
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Length: "+length+"\r\nConnection: close\r\n\r\n"));
                    if(index==3)await stream.WriteAsync(payload);
                }
            });
            using var transfer=new OriginalMusicTransfer(userAgent);
            var file=await transfer.Download(31,address+"/recovery",directory,(_,_)=>{},CancellationToken.None);
            if(!File.ReadAllBytes(file).SequenceEqual(payload))throw new InvalidDataException("Third-attempt media recovery changed bytes");
            foreach(var item in new[]{(32,"/exhausted"),(33,"/missing")})
            {
                try
                {
                    await transfer.Download(item.Item1,address+item.Item2,directory,(_,_)=>{},CancellationToken.None);
                    throw new InvalidDataException("Failed connection became playable");
                }
                catch(OriginalTransferException ex) when(ex.Code==1004) { }
                if(File.Exists(Path.Combine(directory,item.Item1+".ts")) || File.Exists(Path.Combine(directory,item.Item1+".ts.tmp")))
                    throw new InvalidDataException("Failed connection left a playable or temporary file");
            }
            await serving.WaitAsync(TimeSpan.FromSeconds(10));
            if(requests.Count(line=>line.StartsWith("GET /recovery "))!=4 ||
                requests.Count(line=>line.StartsWith("GET /exhausted "))!=3 ||
                requests.Count(line=>line.StartsWith("GET /missing "))!=1)
                throw new InvalidDataException("Original three-attempt/404 connection policy differs");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            try
            {
                await transfer.Download(34,address+"/cancelled",directory,(_,_)=>{},cancelled.Token);
                throw new InvalidDataException("Cancellation was ignored");
            }
            catch(OperationCanceledException) when(cancelled.IsCancellationRequested) { }
            if(listener.Pending())throw new InvalidDataException("Cancelled transfer opened a connection");
            Console.WriteLine("Original media APK-version/serial User-Agent, Range/identity headers, third-attempt recovery, retry exhaustion, 404 stop and pre-connect cancellation verified.");
        }
        finally { listener.Stop();if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }
}
