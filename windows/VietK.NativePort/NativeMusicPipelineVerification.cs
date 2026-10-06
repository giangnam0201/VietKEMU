using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// Explicit loopback protocol fixtures. This checks real HTTP -> original login
// state -> media request -> file/cache -> queues -> actual decoder. It does not
// assert authorization or availability on VietK's production music server.
static class NativeMusicPipelineVerification
{
    public static async Task Run(NativePlayback playback,string root,string fixtures,string output)
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var address="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        var source=Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv"));
        var directory=Path.Combine(output,"server-pipeline");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"music-server.json"),JsonSerializer.Serialize(
            new MusicServerConfiguration(address+"login","loopback-fixture-device","00:00:00:00:00:01",
                "VietK-loopback-verification",Path.GetFullPath(Path.Combine(directory,"music")),"VietK-loopback-downloader")));
        try
        {
            var serving=Task.Run(async()=>
            {
                for(var index=0;index<4;index++)
                {
                    using var socket=await listener.AcceptTcpClientAsync();
                    await using var stream=socket.GetStream();
                    using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                    var first=await reader.ReadLineAsync();var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    string? line;
                    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))
                    { var colon=line.IndexOf(':');headers[line[..colon]]=line[(colon+1)..].Trim(); }
                    if(index<2)
                    {
                        var length=int.Parse(headers["Content-Length"]);var body=new char[length];
                        await reader.ReadBlockAsync(body,0,length);
                        var encoded=new string(body);
                        var request=JsonNode.Parse(Uri.UnescapeDataString(encoded[5..].Replace("+"," ")))!.AsObject();
                        if(first?.StartsWith("POST ")!=true || !encoded.StartsWith("body=") || !headers.ContainsKey("sign"))
                            throw new InvalidDataException("Original signed HTTP form request missing");
                        JsonObject response;
                        if(index==0)
                        {
                            if(request["cmdid"]?.ToString()!="bs_device_login")throw new InvalidDataException("Original device login missing");
                            response=new() { ["errorcode"]="0",["errormessage"]="",["token"]="loopback-fixture-token",
                                ["serverip"]=address+"service/",["validatecode"]="loopback-fixture-validation" };
                        }
                        else
                        {
                            if(request["cmdid"]?.ToString()!="sn_song_media_list" || request["songid"]?.ToString()!="101000" ||
                                headers["validcode"]!="loopback-fixture-validation")
                                throw new InvalidDataException("Original authenticated media request missing");
                            response=new() { ["errorcode"]="0",["origininfo"]="0",["accompanyinfo"]="1",["vol"]="100",
                                ["subtitletype"]="-1",["subtitleurl"]="",["medialist"]=new JsonArray(new JsonObject {
                                    ["type"]=0,["url"]=address+"video",["filesize"]=new FileInfo(source).Length }) };
                        }
                        var bytes=Encoding.UTF8.GetBytes(response.ToJsonString());
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+bytes.Length+"\r\nConnection: close\r\n\r\n"));
                        await stream.WriteAsync(bytes);
                    }
                    else
                    {
                        if(first?.StartsWith("GET /video ")!=true)throw new InvalidDataException("Returned media URL was not downloaded");
                        if(headers.GetValueOrDefault("User-Agent")!="VietK-loopback-downloader")
                            throw new InvalidDataException("Configured downloader User-Agent did not reach the media server");
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+new FileInfo(source).Length+"\r\nConnection: close\r\n\r\n"));
                        if(index==3) { await using var file=File.OpenRead(source);await file.CopyToAsync(stream); }
                    }
                }
            });
            var song=new LocalSong(101000,"Loopback protocol fixture","LP",3,"Fixture",new int[4],new int[4],new int[4],
                0,1,0,"","",1,"",0,0);
            using var database=new LocalSongDatabase(Path.Combine(root,"local-seed.db"),Path.Combine(directory,"queue.db"));
            using var server=new NativeMusicServer(Dispatcher.CurrentDispatcher,directory,_=>song);
            var done=new TaskCompletionSource<string>();
            var selected=new OriginalSelectedQueue(_=>song,_=>{},()=>{},()=>
            {
                var cached=server.Get(song.Id)!;
                if(!playback.PlayMedia(cached.Path,cached.Metadata))throw new InvalidDataException("Completed queue media rejected by decoder");
            });
            selected.Initialize(database.SelectedList,()=>[]);
            OriginalDownloadSelection? selection=null;
            var download=new OriginalDownloadQueue(_=>{},()=>{},()=>selection!.DownloadFirst(),server.Cancel,_=>{},_=>{});
            selection=new(download,()=>{},server.Request,(_,_)=>throw new InvalidDataException("Unexpected non-VietK fixture route"));
            download.Initialize(()=>{},()=>[],true);
            server.Progress+=(id,received,total)=>download.SetProgressBySong(id,total,received);
            server.Failed+=(id,code,message)=>done.TrySetException(new InvalidDataException($"Pipeline failed {id}/{code}: {message}"));
            server.Completed+=(id,cached)=>
            {
                var item=download.At(0)!;item.PlayUrl=cached.Path;item.LocalFlag=1;item.DownloadState=203;
                selected.Add(item);download.DeleteByIndex(0);selection.Reset();selection.DownloadFirst();
                done.TrySetResult(cached.Path);
            };
            download.Add(OriginalOrderExecutor.CreateSongItem(song,"",[],_=>null));
            var path=await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await serving.WaitAsync(TimeSpan.FromSeconds(10));
            if(download.Count!=0 || selected.Count!=1 || selection.IsDownloading || !server.IsConnected ||
                !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))
                throw new InvalidDataException("Completed file/cache/queue promotion differs");
            var deadline=DateTime.UtcNow.AddSeconds(15);
            while(playback.Player.State!=OriginalVideoState.Play || playback.Decoder.Position<=0)
            { if(DateTime.UtcNow>=deadline)throw new TimeoutException("Downloaded queue media did not decode");await Task.Delay(50); }
            File.WriteAllText(Path.Combine(directory,"pipeline-verification.json"),JsonSerializer.Serialize(new {
                signedHttpLogin=true,returnedServiceUrlAndValidationCode=true,mediaUrlRequest=true,
                downloadedFileHash=true,completedCacheRegistration=true,downloadToSelectedQueuePromotion=true,
                actualWindowsDecoderProgress=true,
                scope="Loopback protocol and media fixtures; production server authorization and karaoke availability unverified."
            },new JsonSerializerOptions { WriteIndented=true }));
            playback.Player.Stop();
        }
        finally { listener.Stop(); }
    }
}
