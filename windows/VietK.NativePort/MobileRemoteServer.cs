using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietK.Core;

namespace VietK.NativePort;

// Local replacement transport. This does not impersonate the manufacturer's cloud.
public sealed class MobileRemoteServer : IDisposable
{
    private readonly WebApplication web;
    private readonly Dispatcher dispatcher;
    private readonly YouTubeMusicScreen music;
    private readonly NativePlayback playback;
    private readonly AmbienceExpressions? ambience;
    private readonly SemaphoreSlim searchGate=new(1);
    private readonly Dictionary<string,YouTubeVideo> found=[];
    private string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string ConnectionInfo { get; private set; }="Điều khiển điện thoại đang khởi động…";
    public int Port { get; private set; }
    internal string TestToken=>token;
    internal Func<string,CancellationToken,Task<IReadOnlyList<YouTubeVideo>>>? SearchFixture { get; set; }
    public MobileRemoteServer(Dispatcher dispatcher,YouTubeMusicScreen music,NativePlayback playback,int port=9167,bool loopback=false,AmbienceExpressions? ambience=null)
    {
        this.dispatcher=dispatcher;this.music=music;this.playback=playback;
        this.ambience=ambience;
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args=[] });
        builder.Logging.ClearProviders(); // Never log pairing tokens, URLs or cookies.
        builder.WebHost.ConfigureKestrel(options=>
        {
            options.Listen(loopback?IPAddress.Loopback:IPAddress.Any,port);
            options.Limits.MaxRequestBodySize=8192;options.Limits.MaxConcurrentConnections=32;
            options.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(10);
        });
        web=builder.Build();
        web.Use(async (context,next)=>
        {
            context.Response.Headers.CacheControl="no-store";
            context.Response.Headers["Referrer-Policy"]="no-referrer";
            context.Response.Headers["X-Content-Type-Options"]="nosniff";
            context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' https://i.ytimg.com https://img.youtube.com; frame-ancestors 'none'";
            if(context.Request.Path.StartsWithSegments("/api"))
            {
                var authorization=context.Request.Headers.Authorization.ToString();
                var supplied=authorization.StartsWith("Bearer ",StringComparison.Ordinal)?authorization[7..]:"";
                if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(token)))
                { context.Response.StatusCode=401;return; }
                var origin=context.Request.Headers.Origin.ToString();
                if(origin.Length>0 && origin!="http://"+context.Request.Host)
                { context.Response.StatusCode=403;return; }
            }
            try { await next(context); }
            catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested) { }
            catch(Exception error) when(error is ArgumentException or JsonException or BadHttpRequestException)
            { context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new { error="Yêu cầu không hợp lệ." }); }
            catch(Exception)
            { context.Response.StatusCode=503;await context.Response.WriteAsJsonAsync(new { error="Không thực hiện được. Kiểm tra trạng thái trên màn hình VietK." }); }
        });
        web.MapGet("/",async context=>
        {
            context.Response.ContentType="text/html; charset=utf-8";
            using var stream=typeof(MobileRemoteServer).Assembly.GetManifestResourceStream("VietK.NativePort.mobile-remote.html")!;
            await stream.CopyToAsync(context.Response.Body,context.RequestAborted);
        });
        web.MapGet("/api/state",async context=>await context.Response.WriteAsJsonAsync(await Ui(()=>music.RemoteState())));
        web.MapGet("/api/search",async context=>
        {
            var query=context.Request.Query["q"].ToString().Trim();
            if(query.Length is <1 or >200)throw new ArgumentException();
            if(!await searchGate.WaitAsync(0,context.RequestAborted)) { context.Response.StatusCode=429;return; }
            try
            {
                var pending=await Ui(()=>SearchFixture?.Invoke(query,context.RequestAborted)??music.RemoteSearch(query,context.RequestAborted));
                var items=await pending;
                lock(found) { if(found.Count>1000)found.Clear();foreach(var item in items)found[item.Id]=item; }
                await context.Response.WriteAsJsonAsync(items);
            }
            finally { searchGate.Release(); }
        });
        web.MapPost("/api/action",async context=>
        {
            var request=await context.Request.ReadFromJsonAsync<RemoteAction>(cancellationToken:context.RequestAborted)??throw new ArgumentException();
            await Ui(()=>
            {
                if(request.Action=="add")
                {
                    YouTubeVideo? video;lock(found)found.TryGetValue(request.Id,out video);
                    if(video is null)throw new ArgumentException("Search before adding");
                    music.RemoteAdd(video,request.First);
                }
                else if(request.Action=="command")
                {
                    if(request.Id is not ("play_imv" or "pause_imv" or "cut_song_imv" or "replay_imv" or "volinc" or "voldec"))throw new ArgumentException();
                    playback.Command(request.Id);
                }
                else if(request.Action=="expression")
                { if(ambience?.Show(request.Id)!=true)throw new ArgumentException("Original expression assets unavailable"); }
                else if(request.Action=="wish")
                {
                    if(string.IsNullOrWhiteSpace(request.Id)||request.Id.Length>30||request.Id.Any(char.IsControl)||!playback.Television.Overlay.Barrage.Send(request.Id))throw new ArgumentException("Invalid wish or missing local assets");
                }
                else if(request.Action=="screen")playback.Television.SetScreenMask(!playback.Television.IsScreenMasked);
                else music.RemoteQueue(request.Action,request.Id,request.Target);
                return true;
            });
            await context.Response.WriteAsJsonAsync(new { accepted=true });
        });
    }
    private Task<T> Ui<T>(Func<T> action)=>dispatcher.InvokeAsync(action).Task;
    public async Task StartAsync(bool advertise=true)
    {
        try
        {
            await web.StartAsync();
            var address=web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            Port=new Uri(address).Port;
            var ips=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address)
                .Where(ip=>ip.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && !ip.ToString().StartsWith("169.254.")).Distinct().ToArray();
            ConnectionInfo=ips.Length==0?"Chưa có địa chỉ mạng LAN. Kết nối PC và điện thoại cùng Wi-Fi.":
                "Cùng Wi-Fi, quét QR trên TV. Nếu không kết nối: cho phép VietK qua Windows Firewall (mạng riêng). Địa chỉ: "+string.Join(" / ",ips.Select(ip=>$"http://{ip}:{Port}/"));
            if(advertise && ips.Length>0)await Ui(()=> { ShowPairing(ips[0]);return true; });
        }
        catch(Exception) { ConnectionInfo="Không mở được điều khiển điện thoại (cổng 9167). Đóng bản VietK khác rồi thử lại.";throw; }
    }
    private void ShowPairing(IPAddress address)
    {
        // No hardware serial. Fragment keeps the secret out of HTTP requests and logs.
        playback.Television.Overlay.Qr.ConfigureLocalRemote($"http://{address}:{Port}/#token={token}");
    }
    public void RePair()
    {
        token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var ip=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up)
            .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address)
            .FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)&&!a.ToString().StartsWith("169.254."));
        if(ip is not null)ShowPairing(ip);
    }
    public void ShowPairingPanel(System.Windows.Window owner)
    {
        if(owner.Content is not System.Windows.Controls.Viewbox { Child:System.Windows.Controls.Canvas panel })return;
        if(panel.Children.OfType<System.Windows.Controls.Canvas>().Any(c=>Equals(c.Tag,"mobile-pairing")))return;
        var dim=new System.Windows.Controls.Canvas { Width=1280,Height=800,Tag="mobile-pairing",Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(128,0,0,0)) };
        var content=new System.Windows.Controls.StackPanel { Margin=new System.Windows.Thickness(22) };
        var box=new System.Windows.Controls.Border { Width=560,Height=700,CornerRadius=new(12),Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72,23,64)),Child=content };
        System.Windows.Controls.Canvas.SetLeft(box,360);System.Windows.Controls.Canvas.SetTop(box,50);dim.Children.Add(box);
        content.Children.Add(new System.Windows.Controls.TextBlock { Text="Kết nối điện thoại",FontFamily=OriginalFont.Family,FontSize=28,Foreground=System.Windows.Media.Brushes.White });
        var adapters=new System.Windows.Controls.ComboBox { Margin=new(0,14,0,12),FontSize=20 };
        var addresses=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up)
            .SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address)
            .Where(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)&&!a.ToString().StartsWith("169.254.")).Distinct().ToArray();
        foreach(var address in addresses)adapters.Items.Add(address.ToString());content.Children.Add(adapters);
        var image=new System.Windows.Controls.Image { Width=240,Height=240,Margin=new(0,0,0,12) };content.Children.Add(image);
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(image,System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        void Update()
        {
            if(adapters.SelectedItem is not string address||Port==0)return;
            var uri=$"http://{address}:{Port}/#token={token}";
            var pixels=OriginalMobileQr.Render(uri,true);
            image.Source=System.Windows.Media.Imaging.BitmapSource.Create(pixels.Width,pixels.Height,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels.Pixels,pixels.Width*4);
            ShowPairing(IPAddress.Parse(address));
        }
        adapters.SelectionChanged+=(_,_)=>Update();if(adapters.Items.Count>0)adapters.SelectedIndex=0;
        content.Children.Add(new System.Windows.Controls.TextBlock { Text=ConnectionInfo,FontFamily=OriginalFont.Family,FontSize=18,TextWrapping=System.Windows.TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.White,Height=105 });
        var renew=new System.Windows.Controls.Button { Content="Ngắt điện thoại cũ / tạo QR mới",Height=44,FontSize=18 };
        renew.Click+=(_,_)=> { RePair();Update(); };content.Children.Add(renew);
        var close=new System.Windows.Controls.Button { Content="Đóng",Height=44,FontSize=20,Margin=new(0,10,0,0) };
        close.Click+=(_,_)=>panel.Children.Remove(dim);content.Children.Add(close);
        dim.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource==dim)panel.Children.Remove(dim); };panel.Children.Add(dim);
    }
    public void Dispose()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { web.StopAsync(timeout.Token).GetAwaiter().GetResult(); }
        catch(OperationCanceledException) { }
        web.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    public sealed record RemoteAction(string Action="",string Id="",bool First=false,int Target=0);
}
