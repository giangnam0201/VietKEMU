using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

public sealed record MusicServerConfiguration(string LoginUrl="http://viet.duochang.cc/login",
    string ChipId="",string Mac="",string UserAgent="",string MusicDirectory="");
public sealed record CachedMusic(int SongId,string Path,SongMedia Metadata);

// Windows substitutes a real writable cache directory for Android's mounted
// storage service. Cache registrations contain completed files, never catalogue
// names or assumed local paths. Server credentials remain operator-supplied.
public sealed class NativeMusicServer : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly OriginalServerTransport transport=new();
    private readonly OriginalMusicTransfer transfer=new();
    private readonly OriginalDataCenterClient client;
    private readonly OriginalMediaUrlResolver resolver;
    private readonly Dictionary<int,CachedMusic> cache=new();
    private readonly string cacheFile;
    private CancellationTokenSource? current;
    public MusicServerConfiguration Configuration { get; }
    public string ConfigurationFile { get; }
    public string DirectoryPath { get; }
    public bool NetworkConnected=>NetworkInterface.GetIsNetworkAvailable();
    public bool StorageAvailable=>Directory.Exists(DirectoryPath) &&
        new DriveInfo(Path.GetPathRoot(DirectoryPath)!).AvailableFreeSpace>=524288000L;
    public event Action<int,long,long>? Progress;
    public event Action<int,CachedMusic>? Completed;
    public event Action<int,int,string>? Failed;
    public event Action? ConnectionChanged;
    public bool IsConnected=>client.IsLoggedIn && NetworkConnected;
    public bool HasIdentity=>!string.IsNullOrWhiteSpace(Configuration.ChipId) &&
        !string.IsNullOrWhiteSpace(Configuration.Mac) && !string.IsNullOrWhiteSpace(Configuration.UserAgent);
    public async Task Connect()
    {
        if(!HasIdentity)return;
        try { await Task.Run(client.Connect);ConnectionChanged?.Invoke(); }
        catch(Exception ex) { Failed?.Invoke(0,1013,string.IsNullOrEmpty(client.LoginErrorMessage)?ex.Message:client.LoginErrorMessage); }
    }

    public NativeMusicServer(Dispatcher dispatcher,string stateDirectory,Func<int,LocalSong?> lookup)
    {
        this.dispatcher=dispatcher;
        Directory.CreateDirectory(stateDirectory);
        ConfigurationFile=Path.Combine(stateDirectory,"music-server.json");
        if(!File.Exists(ConfigurationFile))File.WriteAllText(ConfigurationFile,JsonSerializer.Serialize(
            new MusicServerConfiguration(),new JsonSerializerOptions { WriteIndented=true }));
        Configuration=JsonSerializer.Deserialize<MusicServerConfiguration>(File.ReadAllText(ConfigurationFile))
            ??throw new InvalidDataException("Invalid music-server.json");
        DirectoryPath=Path.GetFullPath(string.IsNullOrWhiteSpace(Configuration.MusicDirectory)?
            Path.Combine(stateDirectory,"music"):Configuration.MusicDirectory);
        Directory.CreateDirectory(DirectoryPath);
        cacheFile=Path.Combine(stateDirectory,"music-cache.json");
        if(File.Exists(cacheFile))foreach(var entry in JsonSerializer.Deserialize<CachedMusic[]>(File.ReadAllText(cacheFile))??[])
            if(File.Exists(entry.Path))cache[entry.SongId]=entry;
        client=new(new OriginalDataCenterTokens(),()=>NetworkConnected,()=>Configuration.ChipId,
            ()=>Configuration.Mac,()=>Configuration.UserAgent,"1.0",
            (chip,salt)=>OriginalRequestSignature.Sign(chip??throw new InvalidOperationException("Device ID missing"),
                salt??throw new InvalidOperationException("Server token missing")),transport.Post,
            (_,_)=>{},_=>{},_=>{});
        client.SetLoginUri(Configuration.LoginUrl);
        resolver=new(request=>JsonSerializer.SerializeToElement(client.Send(JsonSerializer.SerializeToNode(request)!.AsObject())),
            ()=>Configuration.Mac,_=>{},id=>dispatcher.Invoke(()=>lookup(id)),
            id=>dispatcher.Invoke(()=>lookup(id)?.ScoringEnabled==true),(_,_)=>{},()=>false,
            ()=>Path.GetPathRoot(DirectoryPath));
    }
    public CachedMusic? Get(int songId)=>cache.GetValueOrDefault(songId);
    public string? LocalPath(SongMedia media)=>Get(media.SongId)?.Path;
    public void Cancel() { var task=current;current=null;task?.Cancel(); }
    public void Request(int songId)
    {
        if(current is not null)return;
        var cancellation=new CancellationTokenSource();current=cancellation;
        _=dispatcher.InvokeAsync(()=>Run(songId,cancellation)).Task.Unwrap();
    }
    private async Task Run(int songId,CancellationTokenSource cancellation)
    {
        try
        {
            if(!NetworkConnected)throw new OriginalTransferException(1015,"Network unavailable");
            if(string.IsNullOrWhiteSpace(Configuration.ChipId)||string.IsNullOrWhiteSpace(Configuration.Mac)||
                string.IsNullOrWhiteSpace(Configuration.UserAgent))
                throw new OriginalTransferException(1013,"The original server requires a registered device ID, MAC and firmware User-Agent. Configure these in "+ConfigurationFile+" and restart. The firmware archive does not supply a device's unique chip ID.");
            var media=await Task.Run(()=>resolver.Request(songId).First(),cancellation.Token);
            var url=media.Url.Replace("%2F","/",StringComparison.Ordinal).Replace("?attname=","",StringComparison.Ordinal);
            if(string.IsNullOrEmpty(url))throw new OriginalTransferException(1012,"Server returned no music URL");
            if(!StorageAvailable)throw new OriginalTransferException(1016,"Music cache needs at least 500 MiB free");
            var path=await transfer.Download(songId,url,DirectoryPath,(received,total)=>
                dispatcher.BeginInvoke(()=>Progress?.Invoke(songId,received,total)),cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var cached=new CachedMusic(songId,path,media.Metadata with { FileName=Path.GetFileName(path) });
            // Register only after download and complete-file checks succeed.
            cache[songId]=cached;
            var temp=cacheFile+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(cache.Values));File.Move(temp,cacheFile,true);
            if(ReferenceEquals(current,cancellation))current=null;
            Completed?.Invoke(songId,cached);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { if(ReferenceEquals(current,cancellation))current=null; }
        catch(Exception ex)
        {
            if(cancellation.IsCancellationRequested)return;
            if(ReferenceEquals(current,cancellation))current=null;
            var code=ex is OriginalTransferException transferError?transferError.Code:1013;
            var detail=string.IsNullOrEmpty(client.LoginErrorMessage)?ex.Message:client.LoginErrorMessage;
            Failed?.Invoke(songId,code,detail);
        }
        finally { cancellation.Dispose(); }
    }
    public void Dispose() { Cancel();transport.Dispose();transfer.Dispose(); }
}
