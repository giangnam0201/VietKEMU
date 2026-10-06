using System.Net;
using System.Net.Sockets;
using System.Text;
using VietK.Core;

static class RealTransportChecks
{
    public static async Task Run()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var address="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        var payload=Enumerable.Range(0,131111).Select(i=>(byte)(i*31)).ToArray();
        var directory=Path.Combine(Path.GetTempPath(),"vietk-transfer-"+Guid.NewGuid());
        try
        {
            var serve=Task.Run(async()=>
            {
                for(var index=0;index<7;index++)
                {
                    using var socket=await listener.AcceptTcpClientAsync();
                    await using var stream=socket.GetStream();
                    var header=new List<byte>();
                    while(header.Count<65536)
                    {
                        var one=new byte[1];if(await stream.ReadAsync(one)==0)throw new IOException("Request EOF");
                        header.Add(one[0]);
                        if(header.Count>=4 && Encoding.ASCII.GetString(header.TakeLast(4).ToArray())=="\r\n\r\n")break;
                    }
                    var text=Encoding.ASCII.GetString(header.ToArray());
                    if(index==0)
                    {
                        if(!text.StartsWith("POST /login HTTP/1.1") || !text.Contains("sign: fixture-sign"))
                            throw new InvalidOperationException("Original server transport headers/method differ");
                        var length=int.Parse(text.Split("\r\n").Single(line=>line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
                        var bytes=new byte[length];await stream.ReadExactlyAsync(bytes);
                        var body=Encoding.ASCII.GetString(bytes);
                        if(Uri.UnescapeDataString(body.Replace("+"," "))!="body={\"cmdid\":\"bs_device_login\"}")
                            throw new InvalidOperationException("Original body form encoding differs");
                        var response=Encoding.UTF8.GetBytes("{\"errorcode\":\"0\"}");
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+response.Length+"\r\nConnection: close\r\n\r\n"));
                        await stream.WriteAsync(response);
                    }
                    else
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+payload.Length+"\r\nConnection: close\r\n\r\n"));
                        // Header-only length probes at 1 and 3; the three
                        // short write attempts follow at 4, 5 and 6.
                        if(index is not (1 or 3))await stream.WriteAsync(index==2?payload:payload.AsMemory(0,400));
                    }
                }
            });
            using var transport=new OriginalServerTransport();
            var response=transport.Post(new(address+"login","{\"cmdid\":\"bs_device_login\"}",
                new[]{new KeyValuePair<string,string?>("sign","fixture-sign")},false));
            if(response!="{\"errorcode\":\"0\"}")throw new InvalidOperationException("Real HTTP POST response differs");
            using var transfer=new OriginalMusicTransfer();long received=0,total=0;
            var file=await transfer.Download(17,address+"song.ts",directory,(r,t)=> { received=r;total=t; },CancellationToken.None);
            if(!File.ReadAllBytes(file).SequenceEqual(payload)||received!=payload.Length||total!=payload.Length||File.Exists(file+".tmp"))
                throw new InvalidOperationException("Transferred bytes/progress/temporary-file promotion differ");
            try { await transfer.Download(18,address+"short.ts",directory,(_,_)=>{},CancellationToken.None);throw new InvalidOperationException("Truncated transfer was accepted"); }
            catch(IOException) { if(File.Exists(Path.Combine(directory,"18.ts")))throw new InvalidOperationException("Incomplete transfer became playable"); }
            await serve.WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine("Real HTTP form/headers, streaming bytes, progress and truncated-transfer admission verified (loopback fixtures, no live authorization claim).");
        }
        finally { listener.Stop();if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }
}
