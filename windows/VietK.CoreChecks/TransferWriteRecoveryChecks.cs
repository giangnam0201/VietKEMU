using System.Net;
using System.Net.Sockets;
using System.Text;
using VietK.Core;

static class TransferWriteRecoveryChecks
{
    public static async Task Run()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var address="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
        var directory=Path.Combine(Path.GetTempPath(),"vietk-write-recovery-"+Guid.NewGuid());
        var payload=Enumerable.Range(0,100007).Select(i=>(byte)(i*23)).ToArray();
        var requests=new List<string>();
        try
        {
            var serving=Task.Run(async()=>
            {
                for(var index=0;index<6;index++)
                {
                    using var socket=await listener.AcceptTcpClientAsync();
                    await using var stream=socket.GetStream();
                    using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                    requests.Add(await reader.ReadLineAsync()??throw new IOException("Missing GET"));
                    var range="";string? line;
                    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))
                        if(line.StartsWith("Range:",StringComparison.OrdinalIgnoreCase))range=line[6..].Trim();
                    if(range!="bytes=0-")throw new InvalidDataException("Retry resumed a deleted partial file");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+payload.Length+"\r\nConnection: close\r\n\r\n"));
                    // Probe connections 0/4 deliberately have no body; data
                    // attempts 1/2/5 close early, exposing real HTTP read errors.
                    if(index is not (0 or 4))await stream.WriteAsync(index==3?payload:payload.AsMemory(0,33168));
                }
            });
            using var transfer=new OriginalMusicTransfer();var notices=new List<int>();long finalProgress=0;
            var path=await transfer.Download(51,address+"/recover",directory,(received,_)=>finalProgress=received,
                CancellationToken.None,notices.Add);
            if(!File.ReadAllBytes(path).SequenceEqual(payload) || finalProgress!=payload.Length ||
                !notices.SequenceEqual(new[]{1018,1018}) || File.Exists(path+".tmp"))
                throw new InvalidDataException("Read recovery failed to restart, notify and promote exact complete bytes");
            using var cancellation=new CancellationTokenSource();var cancelNotices=0;
            try
            {
                await transfer.Download(52,address+"/cancel",directory,(_,_)=>{},cancellation.Token,_=>
                { cancelNotices++;cancellation.Cancel(); });
                throw new InvalidDataException("Cancelled recovery became playable");
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
            await serving.WaitAsync(TimeSpan.FromSeconds(10));
            if(requests.Count(line=>line.StartsWith("GET /recover "))!=4 ||
                requests.Count(line=>line.StartsWith("GET /cancel "))!=2 || cancelNotices!=1 ||
                File.Exists(Path.Combine(directory,"52.ts")) || File.Exists(Path.Combine(directory,"52.ts.tmp")) || listener.Pending())
                throw new InvalidDataException("Length-probe/write attempt count or cancellation cleanup differs");
            Console.WriteLine("Original separate length probe, three write attempts, read-error restart, 1018 notifications, exact recovered bytes and cancellation between attempts verified.");
        }
        finally { listener.Stop();if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }
}
