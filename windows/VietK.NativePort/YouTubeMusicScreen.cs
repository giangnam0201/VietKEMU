using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// Original YouTube card dimensions and existing panel theme; yt-dlp supplies
// public search/media in place of the unavailable manufacturer service.
public sealed class YouTubeMusicScreen : IDisposable
{
    private readonly string root,queueFile,settingsFile;
    private string cookieFile="";
    private readonly NativePlayback playback;
    private readonly BottomBar bottom;
    private readonly YouTubeMusicClient client;
    private readonly List<YouTubeVideo> queue=[];
    private CancellationTokenSource? searching,downloading;
    private WrapPanel? results;
    private IReadOnlyList<YouTubeVideo> videos=[];
    private readonly Dictionary<string,TextBlock> visibleTitles=[];
    private TextBlock? pageLabel;
    private int page;
    private StackPanel? queueView;
    private TextBlock? status;
    private TextBox? input;
    private int generation;
    private bool active,disposed;
    private string message="Tìm bài hát hoặc dán liên kết YouTube. Bấm bài để thêm vào hàng chờ.";
    public event Action? HomeRequested;
    public YouTubeMusicScreen(string root,string stateDirectory,NativePlayback playback,BottomBar bottom)
    {
        this.root=root;this.playback=playback;this.bottom=bottom;
        queueFile=Path.Combine(stateDirectory,"youtube-queue.json");
        settingsFile=Path.Combine(stateDirectory,"youtube-settings.json");
        if(File.Exists(settingsFile))cookieFile=JsonSerializer.Deserialize<YouTubeSettings>(File.ReadAllText(settingsFile))?.CookiesFile??"";
        client=new(Path.Combine(AppContext.BaseDirectory,"YouTubeTools"),Path.Combine(stateDirectory,"youtube-music"),()=>cookieFile);
        if(File.Exists(queueFile))queue.AddRange((JsonSerializer.Deserialize<YouTubeVideo[]>(File.ReadAllText(queueFile))??[])
            .Where(video=>YouTubeMusicClient.VideoId(video.Id)==video.Id));
        playback.CommandOverride=Command;
        playback.LocalMediaRequested+=()=> { active=false; };
    }
    public Canvas Create(string? query=null)
    {
        var canvas=new Canvas { Width=1280,Height=800,ClipToBounds=true,
            Background=new ImageBrush(new BitmapImage(new Uri(Path.Combine(root,"main_bg.jpg")))) { Stretch=Stretch.UniformToFill } };
        Put(canvas,new Image { Width=43,Height=30,Source=new BitmapImage(new Uri(Path.Combine(root,"icon_youtube.png"))) },40,98);
        Put(canvas,Label("YouTube",28),95,88);
        var back=Button("‹ Trang chính",()=>HomeRequested?.Invoke());Put(canvas,back,1035,86);
        results=new WrapPanel { Width=740,Height=440,ClipToBounds=true };
        Put(canvas,results,25,148);
        var pages=new StackPanel { Orientation=Orientation.Horizontal };
        pages.Children.Add(Button("‹",()=>ChangePage(-1)));
        pageLabel=Label("1 / 1",20);pageLabel.Width=100;pageLabel.TextAlignment=TextAlignment.Center;pages.Children.Add(pageLabel);
        pages.Children.Add(Button("›",()=>ChangePage(1)));Put(canvas,pages,280,550);
        var side=new StackPanel { Width=365 };Put(canvas,side,865,146);
        side.Children.Add(Label("Tên bài hát / liên kết YouTube",20));
        input=new TextBox { FontSize=22,Margin=new(0,12,0,8),Padding=new(10),Text=query??"",
            Background=new SolidColorBrush(Color.FromRgb(55,26,94)),Foreground=Brushes.White,BorderBrush=Brushes.MediumPurple };
        input.KeyDown+=async (_,e)=> { if(e.Key==Key.Enter) { e.Handled=true;await Search(); } };side.Children.Add(input);
        var search=Button("Tìm kiếm",()=>_=Search());search.Width=180;side.Children.Add(search);
        side.Children.Add(Label("Hàng chờ",22));
        queueView=new StackPanel();side.Children.Add(new ScrollViewer { Content=queueView,Height=160,Margin=new(0,8,0,5),
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        var actions=new StackPanel { Orientation=Orientation.Horizontal };side.Children.Add(actions);
        actions.Children.Add(Button("Thử lại",()=>_=PlayFirst()));actions.Children.Add(Button("Xóa hàng chờ",Clear));
        var login=new StackPanel { Orientation=Orientation.Horizontal };side.Children.Add(login);
        login.Children.Add(Button("Cookies YouTube…",ChooseCookies));
        login.Children.Add(Button("Bỏ cookies",()=> { cookieFile="";SaveSettings();SetStatus("Chế độ công khai; không dùng phiên đăng nhập."); }));
        status=Label(message,20);status.TextWrapping=TextWrapping.Wrap;status.Width=750;status.Height=52;Put(canvas,status,38,603);
        RefreshQueue();
        if(!string.IsNullOrWhiteSpace(query))_=Search();
        return canvas;
    }
    private async Task Search()
    {
        if(input is null || results is null)return;
        searching?.Cancel();var cancellation=new CancellationTokenSource();searching=cancellation;
        var target=results;var query=input.Text.Trim();SetStatus("Đang tìm trên YouTube…");
        try
        {
            var found=await client.Search(query,cancellation.Token);
            if(!ReferenceEquals(searching,cancellation))return;
            videos=found;page=0;RenderPage(target);
            SetStatus(found.Count==0?"Không tìm thấy video.":"Bấm bài để thêm vào hàng chờ. Bấm chuột phải để hát ngay.");
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(!disposed)SetStatus(ex.Message); }
        finally { if(ReferenceEquals(searching,cancellation))searching=null;cancellation.Dispose(); }
    }
    private void ChangePage(int delta)
    {
        page=Math.Clamp(page+delta,0,Math.Max(0,(videos.Count-1)/6));
        if(results is not null)RenderPage(results);
    }
    internal void SetVerificationResults(IReadOnlyList<YouTubeVideo> items)
    { videos=items;page=0;if(results is not null)RenderPage(results); }
    internal void VerifyPagination()
    {
        if(videos.Count!=8 || results?.Children.Count!=6)throw new InvalidDataException("YouTube first page must show six cards");
        ChangePage(1);
        if(results.Children.Count!=2 || pageLabel?.Text!="2 / 2")throw new InvalidDataException("YouTube last page lost results");
        ChangePage(1);
        if(page!=1)throw new InvalidDataException("YouTube advanced past last page");
        ChangePage(-5);
        if(page!=0 || results.Children.Count!=6)throw new InvalidDataException("YouTube first page boundary differs");
    }
    private void RenderPage(WrapPanel target)
    {
        target.Children.Clear();visibleTitles.Clear();
        if(pageLabel is not null)pageLabel.Text=$"{page+1} / {Math.Max(1,(videos.Count+5)/6)}";
        foreach(var video in videos.Skip(page*6).Take(6))
        {
            // fragment_youtube_recycler_item.xml and YouTubeAdapter.java:
            // fitXY thumbnail, centered 220x45 title, no card background.
            var card=new StackPanel { Width=230,Height=195,Margin=new(8,6,8,6),Cursor=Cursors.Hand };
            var thumbnail=new Image { Width=230,Height=140,Stretch=Stretch.Fill };
            if(Uri.TryCreate(video.Thumbnail,UriKind.Absolute,out var uri))thumbnail.Source=new BitmapImage(uri);
            card.Children.Add(thumbnail);
            var title=Label(video.Title,18);title.Width=220;title.Margin=new(0);title.VerticalAlignment=VerticalAlignment.Center;
            title.TextWrapping=TextWrapping.Wrap;title.TextAlignment=TextAlignment.Center;
            title.Foreground=queue.Any(item=>item.Id==video.Id)?new SolidColorBrush(Color.FromRgb(255,231,97)):Brushes.White;
            card.Children.Add(new Border { Height=45,Child=title });visibleTitles[video.Id]=title;
            card.MouseLeftButtonUp+=(_,_)=>Add(video,false);
            var menu=new ContextMenu();var now=new MenuItem { Header="Hát ngay" };now.Click+=(_,_)=>Add(video,true);menu.Items.Add(now);
            card.ContextMenu=menu;target.Children.Add(card);
        }
    }
    private void Add(YouTubeVideo video,bool first)
    {
        var wasEmpty=queue.Count==0;
        if(first) { queue.RemoveAll(item=>item.Id==video.Id);queue.Insert(0,video); }
        else if(queue.All(item=>item.Id!=video.Id))queue.Add(video);
        Save();RefreshQueue();
        if(first || wasEmpty)_=PlayFirst();
    }
    private void RefreshQueue()
    {
        bottom.SetConfirmedQueueCount(queue.Count);queueView?.Children.Clear();
        foreach(var (id,title) in visibleTitles)
            title.Foreground=queue.Any(item=>item.Id==id)?new SolidColorBrush(Color.FromRgb(255,231,97)):Brushes.White;
        for(var index=0;index<queue.Count;index++)
        {
            var video=queue[index];var row=new DockPanel { Margin=new(0,3,0,3) };
            var remove=Button("×",()=>Remove(video));remove.Width=38;DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
            var text=Label((index+1)+". "+video.Title,17);text.TextTrimming=TextTrimming.CharacterEllipsis;
            row.Children.Add(text);queueView?.Children.Add(row);
        }
    }
    private void Remove(YouTubeVideo video)
    {
        if(queue.FirstOrDefault()?.Id==video.Id) { Next();return; }
        queue.RemoveAll(item=>item.Id==video.Id);Save();RefreshQueue();
    }
    private async Task PlayFirst()
    {
        downloading?.Cancel();var stamp=++generation;
        playback.Player.Stop();active=false;
        if(queue.Count==0)return;
        var video=queue[0];var cancellation=new CancellationTokenSource();downloading=cancellation;
        SetStatus("Đang tải: "+video.Title);
        try
        {
            var file=await client.Download(video,progress=>Application.Current.Dispatcher.BeginInvoke(()=>
            {
                if(stamp==generation)SetStatus("Đang tải: "+video.Title+" — "+(progress.Total>0?
                    (100*progress.Received/progress.Total)+"%":(progress.Received/1048576)+" MiB"));
            }),cancellation.Token);
            if(stamp!=generation || disposed)return;
            if(!playback.PlayMedia(file,preserveStereo:true))throw new IOException("Không phát được video đã tải.");
            active=true;SetStatus("Đang phát: "+video.Title);RefreshQueue();
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(stamp==generation && !disposed)SetStatus(ex.Message+" — bấm Thử lại hoặc chọn bài khác."); }
        finally { if(ReferenceEquals(downloading,cancellation))downloading=null;cancellation.Dispose(); }
    }
    private bool Command(string command)
    {
        if(command=="replay_imv" && active && playback.Player.Source is string source)
        {
            active=playback.PlayMedia(source,preserveStereo:true);
            return true;
        }
        if(command=="decoder_completed") { if(!active)return false;Next();return true; }
        if(command=="cut_song_imv") { Next();return true; }
        if(command is "ori_imv" or "accp_imv")
        { SetStatus("Video YouTube không có thông tin kênh nguyên xướng / nhạc đệm của VietK.");return true; }
        if(command=="orderlist_imv") { RefreshQueue();return true; }
        return false;
    }
    private void Next()
    { downloading?.Cancel();++generation;active=false;playback.Player.Stop();if(queue.Count>0)queue.RemoveAt(0);Save();RefreshQueue();_=PlayFirst(); }
    private void Clear()
    { downloading?.Cancel();++generation;active=false;playback.Player.Stop();queue.Clear();Save();RefreshQueue();SetStatus("Hàng chờ trống."); }
    private void Save()
    { File.WriteAllText(queueFile+".tmp",JsonSerializer.Serialize(queue));File.Move(queueFile+".tmp",queueFile,true); }
    private void ChooseCookies()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog { Title="Chọn file cookies YouTube của chính bạn (định dạng Netscape)",
            Filter="Cookie text files|*.txt|All files|*.*",CheckFileExists=true };
        if(dialog.ShowDialog()!=true)return;
        cookieFile=dialog.FileName;SaveSettings();
        SetStatus("Đã chọn cookies của bạn. yt-dlp sẽ dùng phiên này cho yêu cầu YouTube; file ở lại trên máy này.");
    }
    private void SaveSettings()=>File.WriteAllText(settingsFile,JsonSerializer.Serialize(new YouTubeSettings(cookieFile)));
    private sealed record YouTubeSettings(string CookiesFile);
    private void SetStatus(string value) { message=value;if(status is not null)status.Text=value; }
    private static TextBlock Label(string text,double size)=>new() { Text=text,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,Margin=new(0,6,0,6) };
    private static Button Button(string text,Action action)
    {
        var button=new Button { Content=text,FontSize=18,Padding=new(12,8,12,8),Margin=new(0,3,8,3),
            Background=new LinearGradientBrush(Color.FromRgb(192,55,208),Color.FromRgb(116,55,233),0),Foreground=Brushes.White,
            BorderThickness=new(0) };
        button.Click+=(_,_)=>action();return button;
    }
    private static void Put(Canvas canvas,UIElement element,double x,double y)
    { Canvas.SetLeft(element,x);Canvas.SetTop(element,y);canvas.Children.Add(element); }
    public void Dispose() { disposed=true;++generation;searching?.Cancel();downloading?.Cancel();playback.CommandOverride=null; }
}
