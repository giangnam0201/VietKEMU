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
    private bool useFirefoxCookies;
    private readonly NativePlayback playback;
    private readonly BottomBar bottom;
    private readonly YouTubeMusicClient client;
    private readonly List<YouTubeVideo> queue=[];
    private CancellationTokenSource? searching,downloading;
    private ProgressiveVideo? liveTransfer;
    private WrapPanel? results;
    private IReadOnlyList<YouTubeVideo> videos=[];
    private readonly Dictionary<string,TextBlock> visibleTitles=[];
    private TextBlock? pageLabel;
    private int page;
    private StackPanel? queueView;
    private TextBlock? status;
    private TextBox? input;
    private Image? preview;
    private string category="Karaoke";
    private int generation;
    private bool active,disposed;
    private string message="Tìm bài hát hoặc dán liên kết YouTube. Bấm bài để thêm vào hàng chờ.";
    public event Action? HomeRequested;
    public YouTubeMusicScreen(string root,string stateDirectory,NativePlayback playback,BottomBar bottom)
    {
        this.root=root;this.playback=playback;this.bottom=bottom;
        queueFile=Path.Combine(stateDirectory,"youtube-queue.json");
        settingsFile=Path.Combine(stateDirectory,"youtube-settings.json");
        if(File.Exists(settingsFile))
        {
            var settings=JsonSerializer.Deserialize<YouTubeSettings>(File.ReadAllText(settingsFile));
            cookieFile=settings?.CookiesFile??"";useFirefoxCookies=settings?.UseFirefoxCookies??false;
        }
        client=new(Path.Combine(AppContext.BaseDirectory,"YouTubeTools"),Path.Combine(stateDirectory,"youtube-music"),()=>cookieFile,()=>useFirefoxCookies);
        if(File.Exists(queueFile))queue.AddRange((JsonSerializer.Deserialize<YouTubeVideo[]>(File.ReadAllText(queueFile))??[])
            .Where(video=>YouTubeMusicClient.VideoId(video.Id)==video.Id));
        playback.CommandOverride=Command;
        playback.LocalMediaRequested+=()=> { active=false; };
        playback.PreviewFrameChanged+=PreviewChanged;
    }
    public Canvas Create(string? query=null,bool loadDefault=true)
    {
        var canvas=new Canvas { Width=1280,Height=800,ClipToBounds=true,
            Background=new ImageBrush(new BitmapImage(new Uri(Path.Combine(root,"main_bg.jpg")))) { Stretch=Stretch.UniformToFill } };
        Put(canvas,new Image { Width=43,Height=30,Source=new BitmapImage(new Uri(Path.Combine(root,"icon_youtube.png"))) },40,98);
        var categories=new StackPanel { Orientation=Orientation.Horizontal };
        foreach(var name in new[]{"Karaoke","All","REMIX","Vinahouse","DJ"})
        {
            var tab=Button(name,()=> { category=name;_=Search(); });
            tab.Background=Brushes.Transparent;tab.FontSize=20;categories.Children.Add(tab);
        }
        Put(canvas,categories,95,87);
        var back=Button("‹",()=>HomeRequested?.Invoke());Put(canvas,back,700,587);
        // PGLayoutManager(2,3,HORIZONTAL) fills each column top to bottom.
        results=new WrapPanel { Width=740,Height=440,Orientation=Orientation.Vertical,ClipToBounds=true };
        Put(canvas,results,25,148);
        var pages=new StackPanel { Orientation=Orientation.Horizontal };
        pages.Children.Add(Button("‹",()=>ChangePage(-1)));
        pageLabel=Label("1 / 1",20);pageLabel.Width=100;pageLabel.TextAlignment=TextAlignment.Center;pages.Children.Add(pageLabel);
        pages.Children.Add(Button("›",()=>ChangePage(1)));Put(canvas,pages,280,550);
        Put(canvas,new Border { Width=440,Height=249,Background=Brushes.Black,Child=playback.CreatePanelPreview() },820,95);
        CreateKeyboard(canvas,query??"");
        status=Label(message,20);status.TextWrapping=TextWrapping.Wrap;status.Width=750;status.Height=52;Put(canvas,status,38,603);
        RefreshQueue();
        if(loadDefault || !string.IsNullOrWhiteSpace(query))_=Search();
        return canvas;
    }
    private async Task Search()
    {
        if(input is null || results is null)return;
        searching?.Cancel();var cancellation=new CancellationTokenSource();searching=cancellation;
        var target=results;var query=input.Text.Trim();
        if(YouTubeMusicClient.VideoId(query) is null && category!="All")query=(query+" "+category).Trim();
        SetStatus("Đang tìm trên YouTube…");
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
    private void PreviewChanged(BitmapSource frame) { if(preview is not null)preview.Source=frame; }
    private void CreateKeyboard(Canvas canvas,string text)
    {
        var contract=JsonSerializer.Deserialize<SongBrowserContract>(File.ReadAllText(Path.Combine(root,"song-browser.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("Missing original keyboard");
        input=new TextBox { Width=410,Height=42,FontSize=24,FontFamily=OriginalFont.Family,Text=text,
            Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new(0),Padding=new(0,5,0,0) };
        input.KeyDown+=async (_,e)=> { if(e.Key==System.Windows.Input.Key.Enter) { e.Handled=true;await Search(); } };
        var hint=Label(contract.Hint,24);hint.Foreground=new SolidColorBrush(Color.FromArgb(80,255,255,255));hint.IsHitTestVisible=false;
        hint.Visibility=string.IsNullOrEmpty(text)?Visibility.Visible:Visibility.Collapsed;
        input.TextChanged+=(_,_)=>hint.Visibility=input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        Put(canvas,input,805,353);Put(canvas,hint,805,353);Put(canvas,Button(contract.ClearText,()=>input.Clear()),1220,353);
        var keys=new Canvas { Width=480,Height=240 };Put(canvas,keys,800,400);var alphabetic=true;
        void Edit(string value,bool back=false)
        {
            var start=input.SelectionStart;var length=input.SelectionLength;
            if(back && length==0 && start>0) { start--;length=1; }
            input.Text=input.Text.Remove(start,length).Insert(start,value);input.Select(start+value.Length,0);
        }
        void Key(string label,double width,double x,int row,Action action)
        {
            var button=Button(label,action);button.Width=width;button.Height=contract.KeyRowHeight-contract.KeyGap;
            button.FontSize=contract.KeyTextSize;button.Padding=new(0);button.Margin=new(0);
            button.Background=new SolidColorBrush(Color.FromArgb(51,22,14,35));
            Put(keys,button,x,row*contract.KeyRowHeight);
        }
        void Build()
        {
            keys.Children.Clear();var letters=alphabetic?contract.AlphabetLetters:contract.SymbolLetters;
            for(int i=0;i<10;i++) { var value=letters[i];Key(value,43,i*(43+contract.KeyGap),0,()=>Edit(value)); }
            for(int i=10;i<19;i++) { var value=letters[i];Key(value,43,21+(i-9)*contract.KeyGap+(i-10)*43,1,()=>Edit(value)); }
            Key(alphabetic?".#+=":"ABC",67,0,2,()=> { alphabetic=!alphabetic;Build(); });
            for(int i=19;i<26;i++) { var value=letters[i];Key(value,43,67+(i-18)*contract.KeyGap+(i-19)*43,2,()=>Edit(value)); }
            Key("⌫",62,408,2,()=>Edit("",true));Key("Firefox",100,0,3,ShowLoginMenu);
            Key("__________",250,105,3,()=>Edit(" "));Key("Tìm",110,360,3,()=>_=Search());
        }
        Build();
    }
    private void ShowLoginMenu()
    {
        var menu=new ContextMenu();
        void Add(string label,Action action) { var item=new MenuItem { Header=label };item.Click+=(_,_)=>action();menu.Items.Add(item); }
        Add("Dùng phiên YouTube từ Firefox",()=> { cookieFile="";useFirefoxCookies=true;SaveSettings();_=Search(); });
        Add("Chọn file cookies…",ChooseCookies);
        Add("Bỏ đăng nhập",()=> { cookieFile="";useFirefoxCookies=false;SaveSettings(); });
        Add("Thử lại bài đang tải",()=>_=PlayFirst());menu.IsOpen=true;
        Add("Chữ chạy trên TV…",EditMarquee);
    }
    private void EditMarquee()
    {
        var path=Path.Combine(Path.GetDirectoryName(queueFile)!,"tv-marquee.txt");
        var text=new TextBox { Text=File.Exists(path)?File.ReadAllText(path):"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=150 };
        var area=new StackPanel { Margin=new(15) };area.Children.Add(text);
        var window=new Window { Title="VietK — Chữ chạy trên TV",Width=500,Height=270,Content=area,Owner=Application.Current.MainWindow };
        area.Children.Add(Button("Lưu",()=> { File.WriteAllText(path,text.Text);UpdateMarquee();window.Close(); }));window.ShowDialog();
    }
    private void UpdateMarquee()=>playback.Television.Overlay.SetSong(active?queue.FirstOrDefault()?.Title??"":"",
        active?queue.Skip(1).FirstOrDefault()?.Title??"":"");
    private void ShowQueue()
    {
        queueView=new StackPanel();var content=new StackPanel { Margin=new(20) };content.Children.Add(Label("Đã chọn",26));
        content.Children.Add(new ScrollViewer { Content=queueView,Height=340 });
        content.Children.Add(Button("Thử lại",()=>_=PlayFirst()));content.Children.Add(Button("Xóa hàng chờ",Clear));
        var dialog=new Window { Title="VietK — Đã chọn",Width=560,Height=520,Content=content,Owner=Application.Current.MainWindow,
            Background=new ImageBrush(new BitmapImage(new Uri(Path.Combine(root,"main_bg.jpg")))) };
        RefreshQueue();dialog.ShowDialog();queueView=null;
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
        UpdateMarquee();
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
        playback.Player.Stop();active=false;liveTransfer?.Dispose();liveTransfer=null;
        if(queue.Count==0) { playback.StartIdleDemo();return; }
        var video=queue[0];var cancellation=new CancellationTokenSource();downloading=cancellation;
        SetStatus("Đang tải: "+video.Title);
        try
        {
            var file=client.CompletedVideo(video);
            if(file is null)
            {
            liveTransfer=client.StartProgressive(video,progress=>Application.Current.Dispatcher.BeginInvoke(()=>
            {
                if(stamp==generation)SetStatus("Đang tải: "+video.Title+" — "+(progress.Total>0?
                    (100*progress.Received/progress.Total)+"%":(progress.Received/1048576)+" MiB"));
            }),cancellation.Token);
            await liveTransfer.WaitUntilReady(cancellation.Token);file=liveTransfer.Url;
            }
            if(stamp!=generation || disposed)return;
            if(!playback.PlayMedia(file,preserveStereo:true))throw new IOException("Không phát được video đã tải.");
            active=true;SetStatus("Đang phát: "+video.Title);RefreshQueue();
            playback.Television.Overlay.SetSong(video.Title,queue.Skip(1).FirstOrDefault()?.Title??"");
            if(liveTransfer is not null)
            {
                await liveTransfer.Completion;
                if(stamp==generation && !disposed)SetStatus("Đang phát: "+video.Title+" — đã tải xong");
            }
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
            playback.Television.Overlay.ShowControl("replay");
            return true;
        }
        if(command=="decoder_completed") { if(!active)return false;Next();return true; }
        if(command=="cut_song_imv") { Next();return true; }
        if(command is "ori_imv" or "accp_imv")
        { SetStatus("Video YouTube không có thông tin kênh nguyên xướng / nhạc đệm của VietK.");return true; }
        if(command=="orderlist_imv") { ShowQueue();return true; }
        return false;
    }
    private void Next()
    { downloading?.Cancel();liveTransfer?.Dispose();liveTransfer=null;++generation;active=false;playback.Player.Stop();if(queue.Count>0)queue.RemoveAt(0);Save();RefreshQueue();_=PlayFirst(); }
    private void Clear()
    { downloading?.Cancel();liveTransfer?.Dispose();liveTransfer=null;++generation;active=false;queue.Clear();Save();RefreshQueue();playback.StartIdleDemo();SetStatus("Hàng chờ trống."); }
    private void Save()
    { File.WriteAllText(queueFile+".tmp",JsonSerializer.Serialize(queue));File.Move(queueFile+".tmp",queueFile,true); }
    private void ChooseCookies()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog { Title="Chọn file cookies YouTube của chính bạn (định dạng Netscape)",
            Filter="Cookie text files|*.txt|All files|*.*",CheckFileExists=true };
        if(dialog.ShowDialog()!=true)return;
        cookieFile=dialog.FileName;useFirefoxCookies=false;SaveSettings();
        SetStatus("Đã chọn cookies của bạn. yt-dlp sẽ dùng phiên này cho yêu cầu YouTube; file ở lại trên máy này.");
    }
    private void SaveSettings()=>File.WriteAllText(settingsFile,JsonSerializer.Serialize(new YouTubeSettings(cookieFile,useFirefoxCookies)));
    private sealed record YouTubeSettings(string CookiesFile,bool UseFirefoxCookies=false);
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
    public void Dispose() { disposed=true;++generation;searching?.Cancel();downloading?.Cancel();liveTransfer?.Dispose();playback.CommandOverride=null;playback.PreviewFrameChanged-=PreviewChanged; }
}
