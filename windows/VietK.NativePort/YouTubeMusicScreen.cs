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
    private readonly Dictionary<string,QueueTransferDisplay> queueTransfers=[];
    private CancellationTokenSource? searching,downloading;
    private ProgressiveVideo? liveTransfer;
    private WrapPanel? results;
    private IReadOnlyList<YouTubeVideo> videos=[];
    private readonly Dictionary<string,TextBlock> visibleTitles=[];
    private TextBlock? pageLabel;
    private int page;
    private SelectedQueueDialog? queueDialog;
    private TextBlock? status;
    private TextBox? input;
    private string category="Karaoke";
    private int generation;
    private bool active,disposed;
    private string message="Tìm bài hát hoặc dán liên kết YouTube. Bấm bài để thêm vào hàng chờ.";
    public event Action? HomeRequested;
    public NativeSearchOptions? SearchOptions { get; set; }
    internal Func<string,CancellationToken,Task<IReadOnlyList<YouTubeVideo>>>? SearchFixture { get; set; }
    internal Func<string>? MobileConnectionInfo { get; set; }
    internal Action? RePairMobile { get; set; }
    internal Action? OpenMobilePairing { get; set; }
    internal Action? OpenCollectionLogin { get; set; }
    internal Action? LogoutCollection { get; set; }
    internal Action? OpenBroadcastPlaylist { get; set; }
    internal OriginalQueueRemote? OriginalQueue { get; set; }
    internal object RemoteState()=>new { queue=queue.ToArray(),active,paused=playback.Player.State==OriginalVideoState.Pause,
        volume=playback.Decoder.OutputVolumeStep,muted=playback.Decoder.Muted,
        canAdjustVolume=playback.VolumeUnavailableReason.Length==0,volumeUnavailableReason=playback.VolumeUnavailableReason,
        canSwitchVocal=playback.CanSwitchVocal,originalVocal=playback.ConfirmedOriginalVocal,vocalUnavailableReason=playback.VocalUnavailableReason,
        source=playback.Source.ToString(),original=OriginalQueue?.State(),
        status=message,transfers=queueTransfers.ToDictionary(pair=>pair.Key,pair=>pair.Value) };
    internal Task<IReadOnlyList<YouTubeVideo>> RemoteSearch(string query,CancellationToken cancellation)=>client.Search(query,cancellation);
    internal void RemoteAdd(YouTubeVideo video,bool first)=>Add(video,first);
    internal void SeedRemoteFixture()
    {
        queue.Clear();queue.Add(new("fixture0001","Playing fixture","",""));
        queue.Add(new("fixture0002","Waiting fixture","",""));active=true;Save();RefreshQueue();
    }
    internal void RemoteQueue(string action,string id,int target)
    {
        var video=queue.FirstOrDefault(item=>item.Id==id);
        switch(action)
        {
            case "clear":Clear();break;
            case "shuffle":Shuffle();break;
            case "retry":_=PlayFirst();break;
            case "remove" when video is not null:Remove(video);break;
            case "top" when video is not null:TopNext(video);break;
            case "move" when video is not null:MoveQueue(video,target);break;
            default:throw new ArgumentException("Unknown queue action or song");
        }
    }
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
        playback.SourceChanged+=SourceChanged;
    }
    private void SourceChanged(PlaybackSource source)
    {
        if(source==PlaybackSource.LocalKaraoke)
        {
            ++generation;active=false;
            var previous=downloading;downloading=null;previous?.Cancel();
            liveTransfer?.Dispose();liveTransfer=null;
            SetStatus("Đang phát bài VietK từ hàng chờ cục bộ.");
        }
        else if(source==PlaybackSource.Idle)
        { active=false;SetStatus("Đang phát video chờ."); }
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
        var searchInput=input!;
        SearchOptions?.Register(canvas,()=>searchInput.Text,()=> { searchInput.Clear();if(ReferenceEquals(input,searchInput))_=Search(); });
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
            var found=await (SearchFixture?.Invoke(query,cancellation.Token)??client.Search(query,cancellation.Token));
            if(!ReferenceEquals(searching,cancellation))return;
            videos=found;page=0;RenderPage(target);
            SetStatus(found.Count==0?"Không tìm thấy video.":"Bấm bài để thêm vào hàng chờ. Bấm chuột phải để hát ngay.");
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { if(!disposed)SetStatus(ex.Message); }
        finally { if(ReferenceEquals(searching,cancellation))searching=null;cancellation.Dispose(); }
    }
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
        if(SearchOptions is { } options)
        {
            var clear=new MenuItem { Header="Khi phát bài hát, tự động xóa điều kiện tìm kiếm",IsCheckable=true,IsChecked=options.ClearAfterOrder,Tag=OriginalSearchSettings.ClearKey };
            clear.Click+=(_,_)=>options.SetClearAfterOrder(clear.IsChecked);menu.Items.Add(clear);
        }
        Add("Đăng nhập bộ sưu tập VietK",()=>OpenCollectionLogin?.Invoke());
        Add("Thoát bộ sưu tập VietK",()=>LogoutCollection?.Invoke());
        Add("Thử lại bài đang tải",()=>_=PlayFirst());
        Add("Tắt / Bật tiếng",()=>playback.Command("mute"));
        Add("Âm lượng mặc định…",()=>
        {
            if(Application.Current.MainWindow?.Content is Viewbox { Child:Canvas panel })new OriginalDefaultVolumeDialog(panel,playback.DefaultVolumeSettings);
        });
        Add("Âm lượng màn hình chờ…",()=>
        {
            if(Application.Current.MainWindow?.Content is Viewbox { Child:Canvas panel })new OriginalBroadcastVolumeDialog(panel,playback.BroadcastVolumeSettings);
        });
        Add("Chữ chạy trên TV…",EditMarquee);
        Add("Kết nối điều khiển bằng điện thoại",()=> { SetStatus(MobileConnectionInfo?.Invoke()??"Điều khiển điện thoại chưa khởi động.");OpenMobilePairing?.Invoke(); });
        Add("Ngắt điện thoại cũ / tạo QR mới",()=> { RePairMobile?.Invoke();SetStatus("Đã đổi mã kết nối. Quét lại QR trên TV."); });
        Add("Chế độ hiển thị mã QR lên TV…",()=>
        {
            if(Application.Current.MainWindow?.Content is Viewbox { Child:Canvas panel })new TvQrModeDialog(panel,playback.Television.Overlay.Qr);
        });
        Add("Chọn video chờ (Demo.mp4)…",ChooseIdleVideo);
        Add("Video màn hình chờ…",()=>OpenBroadcastPlaylist?.Invoke());
        Add("Nhập danh sách video chờ VietK…",()=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog { Title="Danh sách video chờ VietK",Filter="VietK playlist|*.init;*.json|All files|*.*",CheckFileExists=true };
            if(dialog.ShowDialog()!=true)return;
            try { playback.ImportIdlePlaylist(File.ReadAllText(dialog.FileName));SetStatus("Đã lưu danh sách video chờ. Chỉ bài có video trên máy mới phát được."); }
            catch(Exception error) { SetStatus(error.Message); }
        });
        Add("Dùng video chờ gốc",()=> { playback.UseFactoryIdleVideo();SetStatus("Đã khôi phục video chờ mặc định."); });menu.IsOpen=true;
    }
    private void ChooseIdleVideo()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog { Title="Chọn video chờ VietK",Filter="MP4 video|*.mp4",CheckFileExists=true };
        if(dialog.ShowDialog()!=true)return;
        try { playback.SetIdleVideo(dialog.FileName);SetStatus("Đã lưu video chờ. Video sẽ phát khi hàng chờ trống."); }
        catch(Exception error) { SetStatus(error.Message); }
    }
    private void EditMarquee()
    {
        if(Application.Current.MainWindow?.Content is Viewbox { Child:Canvas panel })
            new OriginalMarqueeDialog(panel,playback.MarqueeSettings,playback.SetLocalMarquee);
    }
    private void UpdateMarquee()=>playback.Television.Overlay.SetSong(active?queue.FirstOrDefault()?.Title??"":"",
        active?queue.Skip(1).FirstOrDefault()?.Title??"":"");
    private void ShowQueue()
    {
        if(Application.Current.MainWindow?.Content is not Viewbox { Child:Canvas panel })return;
        queueDialog?.Close();
        queueDialog=new SelectedQueueDialog(panel,Remove,TopNext,Clear,Shuffle,()=>_=PlayFirst(),move:MoveQueue);
        RefreshQueue();
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
    internal void VerifyQueueControls(Canvas panel,string captureDirectory)
    {
        var saved=queue.ToArray();var wasActive=active;var stamp=generation;
        queue.Clear();queue.AddRange(Enumerable.Range(0,4).Select(i=>new YouTubeVideo("queue-fixture-"+i,"Queue fixture "+i,"","")));
        active=true;
        var dragResources=Path.Combine(captureDirectory,"queue-drag-fixture");Directory.CreateDirectory(dragResources);
        // Synthetic white insertion line tests actual bitmap rendering without
        // publishing the owner's recovered original PNG in verification files.
        var markerPixels=new byte[514*12*4];
        for(var y=5;y<7;y++)for(var x=0;x<514;x++)for(var channel=0;channel<4;channel++)markerPixels[(y*514+x)*4+channel]=255;
        var markerEncoder=new PngBitmapEncoder();markerEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(514,12,96,96,PixelFormats.Pbgra32,null,markerPixels,514*4)));
        using(var markerFile=File.Create(Path.Combine(dragResources,"selected_order_into.png")))markerEncoder.Save(markerFile);
        queueDialog=new SelectedQueueDialog(panel,Remove,TopNext,Clear,Shuffle,()=>_=PlayFirst(),resources:dragResources,move:MoveQueue);RefreshQueue();
        void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
        void Click(UIElement element)=>element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
        Border IconAt(int row,string name)=>((Canvas)queueDialog.Rows.Children[row]).Children.OfType<Border>().Single(b=>Equals(b.Tag,name));
        try
        {
            panel.Measure(new Size(1280,800));panel.Arrange(new Rect(0,0,1280,800));panel.UpdateLayout();
            Require(((Canvas)queueDialog.Rows.Children[0]).Height==65 && panel.Children.Contains(queueDialog.Overlay),"Selected queue layout differs");
            var image=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);image.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using(var file=File.Create(Path.Combine(captureDirectory,"native-selected-queue.png")))encoder.Save(file);
            var content=(Canvas)((Border)queueDialog.Overlay.Children[0]).Child;
            Click(content.Children.OfType<Border>().Single(b=>Equals(b.Tag,"Lịch sử")));
            Require(queueDialog.ShowingHistory && queue.Count==4 && queueDialog.Rows.Parent is null,"History switch changed the queue or fabricated sung YouTube rows");
            Click(content.Children.OfType<Border>().Single(b=>Equals(b.Tag,"Đã đặt bài")));
            Require(!queueDialog.ShowingHistory && queueDialog.Rows.Parent is not null,"Selected tab did not restore queue rows");
            Canvas TransferArea()=>((Canvas)queueDialog.Rows.Children[0]).Children.OfType<Canvas>().Single();
            SetQueueTransfer(queue[0].Id,new(1048576));
            Require(TransferArea().Children.OfType<TextBlock>().Single().Text.EndsWith(" MiB") && TransferArea().Children.OfType<Image>().Count()==0,"Streaming bytes invented percentage/bar coverage");
            SetQueueTransfer(queue[0].Id,new(45,100));
            Require(TransferArea().Children.OfType<TextBlock>().Single().Text=="45%" && ((RectangleGeometry)TransferArea().Children.OfType<Image>().Last().Clip).Rect.Width==18,"Known-size download bar differs");
            SetQueueTransfer(queue[0].Id,new(Error:"Verification failure"));
            Require(TransferArea().Children.OfType<TextBlock>().Single().Text=="Lỗi tải" && TransferArea().Children.Count==1,"Failed transfer still displays a success bar");
            SetQueueTransfer(queue[0].Id,null);Require(TransferArea().Children.Count==0,"Completed transfer decoration remains visible");
            Require(!queueDialog.Drag.BeginForVerification(0),"Drag selected the playing head");
            Require(queueDialog.Drag.BeginForVerification(1),"Waiting song could not start a drag");
            Require(queueDialog.Drag.Ghost is { Width:643,Height:66,Opacity:.8 },"Original drag preview geometry differs");
            queueDialog.Drag.Update(195);Require(queueDialog.Drag.Target==3 && queueDialog.Drag.MarkerTop==398,"Drag insertion position differs");
            panel.Measure(new Size(1280,800));panel.Arrange(new Rect(0,0,1280,800));panel.UpdateLayout();
            var dragImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);dragImage.Render(panel);
            var markerPixel=new byte[4];dragImage.CopyPixels(new Int32Rect(1242,403,1,1),markerPixel,4,0);
            var backgroundPixel=new byte[4];image.CopyPixels(new Int32Rect(1242,403,1,1),backgroundPixel,4,0);
            Require(markerPixel.Take(3).All(value=>value>=254) && markerPixel[3]==255 &&
                markerPixel.Take(3).Sum(value=>(int)value)-backgroundPixel.Take(3).Sum(value=>(int)value)>300,
                "Insertion bitmap did not render above the drag preview: BGRA="+string.Join(",",markerPixel));
            var dragEncoder=new PngBitmapEncoder();dragEncoder.Frames.Add(BitmapFrame.Create(dragImage));
            using(var dragFile=File.Create(Path.Combine(captureDirectory,"native-queue-drag-fixture.png")))dragEncoder.Save(dragFile);
            Click(queueDialog.Overlay);Require(queue.Select(v=>v.Id).SequenceEqual(new[]{"queue-fixture-0","queue-fixture-2","queue-fixture-3","queue-fixture-1"}) && !queueDialog.Drag.IsDragging,"Drag release failed to reorder or remove its ghost");
            Require(queueDialog.Drag.BeginForVerification(3),"Reverse drag could not start");queueDialog.Drag.Update(65);Click(queueDialog.Overlay);
            Require(queue.Select(v=>v.Id).SequenceEqual(new[]{"queue-fixture-0","queue-fixture-1","queue-fixture-2","queue-fixture-3"}),"Reverse drag changed the playing head");
            Require(queueDialog.Drag.BeginForVerification(2),"End-boundary drag could not start");queueDialog.Drag.Update(260);Click(queueDialog.Overlay);
            Require(queue[2].Id=="queue-fixture-2" && queue.Count==4,"One-past-last marker reordered a song");
            Require(queueDialog.Drag.BeginForVerification(2),"Cancel drag could not start");queueDialog.Drag.Update(195);
            Click(content.Children.OfType<Border>().Single(b=>Equals(b.Tag,"Lịch sử")));
            Require(!queueDialog.Drag.IsDragging && queue[2].Id=="queue-fixture-2","Tab switch committed a cancelled drag");
            Click(content.Children.OfType<Border>().Single(b=>Equals(b.Tag,"Đã đặt bài")));
            queue.AddRange(Enumerable.Range(4,16).Select(i=>new YouTubeVideo("queue-fixture-"+i,"Scroll fixture "+i,"","")));RefreshQueue();
            panel.Measure(new Size(1280,800));panel.Arrange(new Rect(0,0,1280,800));panel.UpdateLayout();
            Require(queueDialog.Drag.BeginForVerification(1),"Scrollable drag could not start");queueDialog.Drag.Update(450);queueDialog.Drag.ScrollForVerification();
            Require(queueDialog.Drag.ScrollOffset==20,"Original edge scrolling did not advance 20px");queueDialog.Drag.Cancel();
            queue.RemoveRange(4,16);Save();RefreshQueue();panel.Measure(new Size(1280,800));panel.Arrange(new Rect(0,0,1280,800));panel.UpdateLayout();
            Require(active && generation==stamp,"Reordering restarted current playback");
            Click(IconAt(3,"ic_top_song"));Require(queue.Select(v=>v.Id).SequenceEqual(new[]{"queue-fixture-0","queue-fixture-3","queue-fixture-1","queue-fixture-2"}),"Queue top callback differs");
            Click(IconAt(2,"ic_delete"));Require(queue.Count==3 && queue.All(v=>v.Id!="queue-fixture-1"),"Pending-song delete callback failed");
            var toolbar=((Canvas)((Border)queueDialog.Overlay.Children[0]).Child).Children.OfType<Grid>().Single();
            Click(toolbar.Children[1]);Require(queue[0].Id=="queue-fixture-0" && queue.Count==3,"Shuffle restarted or lost the playing song");
            StackPanel ConfirmationButtons()=>((StackPanel)((Border)((Canvas)panel.Children[panel.Children.Count-1]).Children[0]).Child).Children.OfType<StackPanel>().Single();
            Click(toolbar.Children[0]);Click(ConfirmationButtons().Children[0]);
            Require(queue.Count==3 && panel.Children[panel.Children.Count-1]==queueDialog.Overlay,"Cancel cleared the queue or left confirmation open");
            Click(toolbar.Children[0]);Click(ConfirmationButtons().Children[1]);
            Require(queue.Count==1 && queue[0].Id=="queue-fixture-0" && active && generation==stamp,"Clear interrupted current playback");
            Require(JsonSerializer.Deserialize<List<YouTubeVideo>>(File.ReadAllText(queueFile))?.Count==1,"Queue changes were not saved");
        }
        finally
        {
            queueDialog.Close();queueDialog=null;queue.Clear();queue.AddRange(saved);active=wasActive;Save();RefreshQueue();
        }
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
        SearchOptions?.Ordered();
        if(first || (wasEmpty&&playback.Source!=PlaybackSource.LocalKaraoke))_=PlayFirst();
    }
    private void RefreshQueue()
    {
        foreach(var id in queueTransfers.Keys.Where(id=>queue.All(item=>item.Id!=id)).ToArray())queueTransfers.Remove(id);
        if(OriginalQueue?.ShouldPresent!=true)bottom.SetConfirmedQueueCount(queue.Count);
        queueDialog?.Refresh(queue,active,queueTransfers);
        if(playback.Source!=PlaybackSource.LocalKaraoke)UpdateMarquee();
        foreach(var (id,title) in visibleTitles)
            title.Foreground=queue.Any(item=>item.Id==id)?new SolidColorBrush(Color.FromRgb(255,231,97)):Brushes.White;
    }
    private void Remove(YouTubeVideo video)
    {
        if(!active&&downloading is null&&playback.Source==PlaybackSource.LocalKaraoke)
        { queue.RemoveAll(item=>item.Id==video.Id);Save();RefreshQueue();return; }
        if(queue.FirstOrDefault()?.Id==video.Id) { Next();return; }
        queue.RemoveAll(item=>item.Id==video.Id);Save();RefreshQueue();
    }
    private async Task PlayFirst()
    {
        downloading?.Cancel();var stamp=++generation;
        playback.Player.Stop();active=false;liveTransfer?.Dispose();liveTransfer=null;
        if(queue.Count==0) { playback.StartIdleDemo();return; }
        var video=queue[0];var cancellation=new CancellationTokenSource();downloading=cancellation;
        SetStatus("Đang tải: "+video.Title);SetQueueTransfer(video.Id,new(Waiting:true));RefreshQueue();
        try
        {
            var file=await client.VerifiedCachedVideo(video,cancellation.Token);
            if(file is null)
            {
            liveTransfer=client.StartProgressive(video,progress=>Application.Current.Dispatcher.BeginInvoke(()=>
            {
                if(stamp==generation) { SetQueueTransfer(video.Id,new(progress.Received,progress.Total));SetStatus("Đang tải: "+video.Title+" — "+queueTransfers[video.Id].Caption); }
            }),cancellation.Token);
            try { await liveTransfer.WaitUntilReady(cancellation.Token);file=liveTransfer.Url; }
            catch(YouTubeIncompleteAudioException)
            {
                liveTransfer.Dispose();liveTransfer=null;
                SetStatus("Âm thanh tải chưa đủ — đang tải lại bài bằng chế độ đầy đủ…");
                file=await client.Download(video,progress=>Application.Current.Dispatcher.BeginInvoke(()=>
                {
                    if(stamp==generation) { SetQueueTransfer(video.Id,new(progress.Received,progress.Total));SetStatus("Đang tải lại: "+video.Title+" — "+queueTransfers[video.Id].Caption); }
                }),cancellation.Token);
            }
            }
            if(stamp!=generation || disposed)return;
            if(liveTransfer is null)SetQueueTransfer(video.Id,null);
            if(!playback.PlayMedia(file,preserveStereo:true,source:PlaybackSource.YouTube))throw new IOException("Không phát được video đã tải.");
            active=true;SetStatus("Đang phát: "+video.Title);RefreshQueue();
            playback.Television.Overlay.SetSong(video.Title,queue.Skip(1).FirstOrDefault()?.Title??"");
            if(liveTransfer is not null)
            {
                try { await liveTransfer.Completion; }
                catch(YouTubeIncompleteAudioException)
                {
                    if(stamp!=generation || disposed)return;
                    var resumeAt=playback.Decoder.Position;
                    active=false;liveTransfer.Dispose();liveTransfer=null;playback.Player.Stop();
                    SetQueueTransfer(video.Id,new(Waiting:true));RefreshQueue();
                    SetStatus("Âm thanh tải chưa đủ — đang tải lại bài bằng chế độ đầy đủ…");
                    var repaired=await client.Download(video,progress=>Application.Current.Dispatcher.BeginInvoke(()=>
                    {
                        if(stamp==generation) { SetQueueTransfer(video.Id,new(progress.Received,progress.Total));SetStatus("Đang tải lại âm thanh: "+video.Title+" — "+queueTransfers[video.Id].Caption); }
                    }),cancellation.Token);
                    if(stamp!=generation || disposed)return;
                    if(!playback.PlayMedia(repaired,preserveStereo:true,source:PlaybackSource.YouTube))throw new IOException("Không phát được video đã tải lại.");
                    active=true;RefreshQueue();
                    var deadline=DateTime.UtcNow.AddSeconds(15);
                    while(playback.Player.State==OriginalVideoState.Preparing && DateTime.UtcNow<deadline)
                        await Task.Delay(50,cancellation.Token);
                    if(stamp!=generation || disposed)return;
                    if(playback.Player.State==OriginalVideoState.Play && resumeAt>0 && resumeAt<playback.Decoder.Duration)playback.Player.Seek(resumeAt);
                }
                if(stamp==generation && !disposed) { SetQueueTransfer(video.Id,null);SetStatus("Đang phát: "+video.Title+" — đã tải xong"); }
            }
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(stamp==generation && !disposed)
            {
                active=false;liveTransfer?.Dispose();liveTransfer=null;playback.StartIdleDemo();
                SetQueueTransfer(video.Id,new(Error:ex.Message));RefreshQueue();
                SetStatus(ex.Message+" — bấm Thử lại hoặc chọn bài khác.");
            }
        }
        finally { if(ReferenceEquals(downloading,cancellation))downloading=null;cancellation.Dispose(); }
    }
    private void SetQueueTransfer(string id,QueueTransferDisplay? state)
    {
        if(state is null)queueTransfers.Remove(id);else queueTransfers[id]=state;
        queueDialog?.SetTransfer(id,state);
    }
    private bool Command(string command)
    {
        if(command=="decoder_failed")
        {
            if(!active)return false;
            ++generation;active=false;downloading?.Cancel();liveTransfer?.Dispose();liveTransfer=null;
            playback.StartIdleDemo();UpdateMarquee();
            SetStatus("Không giải mã được video — bấm Thử lại hoặc chọn bài khác.");return true;
        }
        if(command=="replay_imv" && active && playback.Player.Source is string source)
        {
            active=playback.PlayMedia(source,preserveStereo:true,source:PlaybackSource.YouTube);
            playback.Television.Overlay.ShowControl("replay");
            return true;
        }
        if(command=="decoder_completed") { if(!active)return false;Next();return true; }
        if(command=="cut_song_imv")
        {
            if(playback.Source==PlaybackSource.LocalKaraoke)return false;
            if(!active&&downloading is null&&playback.Source!=PlaybackSource.YouTube)return false;
            Next();return true;
        }
        if(command is "ori_imv" or "accp_imv")
        {
            if(playback.CanSwitchVocal)return false;
            SetStatus(playback.Decoder.PreserveStereo&&playback.CurrentMedia is null?
                "Video YouTube không có thông tin kênh nguyên xướng / nhạc đệm của VietK.":playback.VocalUnavailableReason);
            return true;
        }
        if((command is "volinc" or "voldec")&&playback.Source==PlaybackSource.Idle&&playback.BroadcastSessionMuted)
        { SetStatus(playback.VolumeUnavailableReason);return true; }
        if(command is "order_bg" or "orderlist_imv")
        { if(OriginalQueue?.ShouldPresent==true)OriginalQueue.ShowDialog();else ShowQueue();return true; }
        return false;
    }
    private void Next()
    { downloading?.Cancel();liveTransfer?.Dispose();liveTransfer=null;++generation;active=false;playback.Player.Stop();if(queue.Count>0)queue.RemoveAt(0);Save();RefreshQueue();_=PlayFirst(); }
    private void Clear()
    {
        if(!active&&downloading is null&&playback.Source==PlaybackSource.LocalKaraoke)
        { queue.Clear();queueTransfers.Clear();Save();RefreshQueue();return; }
        if(active && queue.Count>0)
        {
            OriginalQueueOrder.ClearExceptPlaying(queue,idle:false);Save();RefreshQueue();
            SetStatus("Đã xóa các bài đang chờ.");return;
        }
        downloading?.Cancel();liveTransfer?.Dispose();liveTransfer=null;++generation;active=false;
        OriginalQueueOrder.ClearExceptPlaying(queue,idle:true);Save();RefreshQueue();playback.StartIdleDemo();SetStatus("Hàng chờ trống.");
    }
    private void TopNext(YouTubeVideo video)
    { if(OriginalQueueOrder.Top(queue,queue.FindIndex(item=>item.Id==video.Id))) { Save();RefreshQueue(); } }
    private void Shuffle()
    { if(OriginalQueueOrder.Shuffle(queue,Random.Shared.Next)) { Save();RefreshQueue(); } }
    private void MoveQueue(YouTubeVideo video,int target)
    { if(OriginalQueueOrder.Move(queue,queue.FindIndex(item=>item.Id==video.Id),target)) { Save();RefreshQueue(); } }
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
    public void Dispose() { disposed=true;++generation;searching?.Cancel();downloading?.Cancel();liveTransfer?.Dispose();playback.SourceChanged-=SourceChanged;playback.CommandOverride=null; }
}
