using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

public sealed record SongBrowserContract(string Title, string EmptyMessage, string YoutubeText,
    string Hint, string ClearText, double ContainerX, double ContainerY, double ContainerWidth,
    double ContainerHeight, double CategoryHeight, double BackX, double BackY,
    double KeyboardWidth, double KeyboardY, double PhantomWidth, double PhantomHeight,
    double KeyboardHeight, double KeyRowHeight, double KeyGap, double KeyTextSize,
    string[] AlphabetLetters, string[] SymbolLetters, string Provenance);

// First local SongNameFragment path. Song tiles/actions, alternate input modes,
// The phantom shares the TV decoder's preview and overlay composition.
public sealed class SongBrowser(string root, SongBrowserContract contract,
    MoreContract more, LocalSongDatabase local, SongGridContract gridContract,
    Func<SongQueryContext>? queryContext=null,OriginalSinger? singer=null)
{
    public event Action? HomeRequested;
    public event Action? BackRequested;
    public event Action<string>? SingerRequested;
    public NativePlayback? Playback { get; set; }
    public event Action<string>? YoutubeRequested;
    public event Action<int>? InputModeRequested;
    public event Action<CatalogueSong,string>? SongActionRequested;
    public VietnameseSearchInput? Input { get; private set; }
    public IReadOnlyList<CatalogueSong> Results { get; private set; } = [];
    public bool Alphabetic { get; private set; } = true;
    public OriginalSinger? Singer=>singer;
    internal bool SingerPortraitAvailable { get; private set; }
    private IReadOnlySet<int> confirmedQueued=new HashSet<int>();
    private IReadOnlySet<int> confirmedCollected=new HashSet<int>();
    private Action? refreshSelection;
    private Action? refreshQuery;
    public void Refresh()=>refreshQuery?.Invoke();
    public void SetConfirmedQueuedSongs(IReadOnlySet<int> songIds)
    { confirmedQueued=songIds;refreshSelection?.Invoke(); }
    public void SetConfirmedCollectedSongs(IReadOnlySet<int> songIds)
    { confirmedCollected=songIds;refreshSelection?.Invoke(); }

    public Canvas Create()=>CreateCore(null);
    internal Canvas CreateVerificationFixture(IReadOnlyList<CatalogueSong> songs)=>CreateCore(songs);

    private Canvas CreateCore(IReadOnlyList<CatalogueSong>? fixtureSongs)
    {
        Alphabetic = true;
        var canvas = new Canvas { Width = 1280, Height = 800, ClipToBounds = true,
            Background = new ImageBrush(Bitmap("main_bg.jpg")) { Stretch = Stretch.UniformToFill } };
        var area = new Grid { Width = contract.ContainerWidth, Height = contract.ContainerHeight,
            Background = new LinearGradientBrush(new GradientStopCollection {
                new(Color("#33fb00cb"),0), new(Color("#33490ba6"),.5), new(Color("#33c01be2"),1) },
                new Point(0,1), new Point(1,0)) };
        // ThemeManager.getSongListBg: rounded gradient and 2px outline.
        var frame = new Border { CornerRadius=new(5), IsHitTestVisible=false,
            BorderThickness=new(2), BorderBrush=Brush("#195375be"), Background=area.Background };
        area.Background = Brushes.Transparent;
        area.Children.Add(frame);Put(canvas, area, contract.ContainerX, contract.ContainerY);
        var category = new StackPanel { Orientation=Orientation.Horizontal, Height=contract.CategoryHeight,
            HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top, Margin=new(15,10,0,0) };
        category.Tag=singer is null?"song-category-home":"singer-category-home";
        if(singer is not null)
        {
            var portrait=Path.Combine(OriginalSupplement.Root,"ambience","singer","defaultsmall.png");
            SingerPortraitAvailable=File.Exists(portrait);
            category.Children.Add(new Border { Width=27,Height=27,CornerRadius=new(10),ClipToBounds=true,Margin=new(10,0,0,0),
                Child=SingerPortraitAvailable?new Image { Source=new BitmapImage(new Uri(Path.GetFullPath(portrait))),Stretch=Stretch.Fill }:null });
        }
        var title = Text(singer?.Name??contract.Title,22); title.FontWeight=FontWeights.Bold;
        title.Margin=new(10,0,0,0); title.VerticalAlignment=VerticalAlignment.Center;
        category.Children.Add(title);
        category.Children.Add(new Border { Width=2,Height=16,Background=Brush("#33ffffff"),Margin=new(12,0,15,0),VerticalAlignment=VerticalAlignment.Center });
        category.MouseLeftButtonUp += (_,_) => HomeRequested?.Invoke(); area.Children.Add(category);
        var empty = new StackPanel { Width=contract.ContainerWidth-100,
            HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
        var message = Text(contract.EmptyMessage,26); message.TextWrapping=TextWrapping.Wrap; empty.Children.Add(message);
        var youtubeText=Text(contract.YoutubeText,24); youtubeText.FontWeight=FontWeights.Bold;
        youtubeText.Padding=new(5,0,5,0);
        var youtube=new Border { Background=Gradient("#ffc037d0","#ff7437e9"), CornerRadius=new(30),
            HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,15,0,0),Child=youtubeText };
        Click(youtube,()=>YoutubeRequested?.Invoke(Input?.Text ?? "")); empty.Children.Add(youtube); area.Children.Add(empty);
        var gridFactory=new SongGrid(root,gridContract);
        gridFactory.ActionRequested+=(song,action)=>SongActionRequested?.Invoke(song,action);
        gridFactory.SingerRequested+=name=>SingerRequested?.Invoke(name);
        var nextPageTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(150) };
        nextPageTimer.Tick+=(_,_)=> { nextPageTimer.Stop();LoadNextPage(); };
        var currentPage=0;var lastTotalSize=0;
        ScrollViewer? songGrid=null;
        void ShowResults(IReadOnlyList<CatalogueSong> songs,bool preserveOffset=false)
        {
            var offset=preserveOffset?songGrid?.VerticalOffset??0:0;
            Results=songs;empty.Visibility=songs.Count==0?Visibility.Visible:Visibility.Collapsed;
            if(songGrid is not null)area.Children.Remove(songGrid);
            songGrid=gridFactory.Create(songs,confirmedQueued,confirmedCollected);songGrid.HorizontalAlignment=HorizontalAlignment.Left;
            songGrid.VerticalAlignment=VerticalAlignment.Top;songGrid.Margin=new(0,50,6,0);
            songGrid.Visibility=songs.Count==0?Visibility.Collapsed:Visibility.Visible;area.Children.Add(songGrid);
            songGrid.ScrollToVerticalOffset(offset);
            if(singer is not null)songGrid.ScrollChanged+=(_,e)=>
            { if(e.VerticalChange!=0&&canvas.IsLoaded) { nextPageTimer.Stop();nextPageTimer.Start(); } };
        }
        refreshSelection=()=>ShowResults(Results,true);
        var back=new Border { Width=more.BackWidth,Height=more.BackHeight,CornerRadius=new(more.BackCorner),
            Background=Gradient(more.BackStartColor,more.BackEndColor),Child=Icon("icon_back.png",27,20) };
        back.Tag=singer is null?"song-browser-back":"singer-song-back";
        Click(back,()=> { if(singer is null)HomeRequested?.Invoke();else BackRequested?.Invoke(); }); Put(canvas,back,contract.BackX,contract.BackY);

        var shell=new Grid { Width=contract.KeyboardWidth };
        // Android 6 GradientDrawable: omitted endColor defaults to transparent,
        // and an explicit centerColor inserts a stop at 0.5.
        // aosp-mirror/platform_frameworks_base android-6.0.1_r1, updateGradientDrawableGradient.
        var shellGradient=new LinearGradientBrush(new GradientStopCollection {
            new(Color("#335a2e9d"),0), new(Color("#337339b1"),.5), new(Colors.Transparent,1) },
            new Point(0,.5),new Point(1,.5));
        shell.Children.Add(new Border { CornerRadius=new(5),BorderThickness=new(1),IsHitTestVisible=false,
            BorderBrush=Brush("#195375be"),Background=shellGradient });
        var column=new StackPanel(); shell.Children.Add(column);
        column.Children.Add(new Border { Width=contract.PhantomWidth,Height=contract.PhantomHeight,
            Margin=new(20,5,20,5),Padding=new(2),CornerRadius=new(5),Background=Brushes.Black,
            Child=Playback?.CreatePanelPreview() });
        var keyboard=new Canvas { Width=contract.KeyboardWidth,Height=contract.KeyboardHeight };
        column.Children.Add(keyboard); Put(canvas,shell,1280-contract.KeyboardWidth,contract.KeyboardY);
        var display=Text("",28); display.FontWeight=FontWeights.Bold; display.Width=380;display.Height=35;
        display.TextWrapping=TextWrapping.NoWrap;display.ClipToBounds=true;
        Put(keyboard,display,25,0);
        var keys=new Canvas { Width=contract.KeyboardWidth,Height=4*contract.KeyRowHeight };
        Put(keyboard,keys,3,35);
        var timers=new HashSet<DispatcherTimer>();
        var input=new VietnameseSearchInput((delay,callback)=>
        {
            var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(delay) };
            timer.Tick+=(_,_)=> { timer.Stop();timers.Remove(timer);callback(); };timers.Add(timer);timer.Start();
        });
        Input=input;
        var lastSpell="";
        input.TextChanged+=()=> { display.Text=input.Text.Length==0?contract.Hint:input.Text;
            display.Foreground=input.Text.Length==0?Brush("#33ffffff"):Brushes.White; };
        input.SpellRequested+=spell=>
        {
            lastSpell=spell;
            ResetQuery();
        };
        canvas.Unloaded+=(_,_)=> { nextPageTimer.Stop();foreach(var timer in timers) timer.Stop();timers.Clear(); };
        void BuildKeys()
        {
            keys.Children.Clear();
            var letters=Alphabetic?contract.AlphabetLetters:contract.SymbolLetters;
            if(letters.Length!=26)throw new InvalidDataException("Original Vietnamese key count differs");
            Border Key(string text,double width,double x,int row,Action action,string? icon=null)
            {
                var key=new Border { Width=width,Height=contract.KeyRowHeight-contract.KeyGap,
                    CornerRadius=new(7),Background=Brush("#33160e23") };
                if(icon is null) { var label=Text(text,contract.KeyTextSize);label.FontWeight=FontWeights.Bold;
                    label.Foreground=Brush("#ffc5d0fb");label.TextAlignment=TextAlignment.Center;
                    label.VerticalAlignment=VerticalAlignment.Center;key.Child=label; }
                else key.Child=new Image { Source=Bitmap(icon),Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
                Click(key,action,true);Put(keys,key,x,row*contract.KeyRowHeight);return key;
            }
            for(int i=0;i<10;i++) { var value=letters[i];Key(value,43,i*(43+contract.KeyGap),0,()=>input.Letter(value)); }
            for(int i=10;i<19;i++) { var value=letters[i];Key(value,43,21+(i-9)*contract.KeyGap+(i-10)*43,1,()=>input.Letter(value)); }
            Key(Alphabetic?".#+=":"ABC",67,0,2,()=> { Alphabetic=!Alphabetic;BuildKeys(); });
            for(int i=19;i<26;i++) { var value=letters[i];Key(value,43,67+(i-18)*contract.KeyGap+(i-19)*43,2,()=>input.Letter(value)); }
            Key("",62,67+7*43+8*contract.KeyGap,2,input.Back,"search_keyboard_back.png");
            Key(contract.ClearText,100,0,3,input.Clear);
            Key("__________",250,100+contract.KeyGap,3,input.Space);
            Key("",50,350+2*contract.KeyGap,3,()=>InputModeRequested?.Invoke(2),"icon_pen.png");
            Key("",50,400+3*contract.KeyGap,3,()=>InputModeRequested?.Invoke(10),"keyboard_earth.png");
        }
        IReadOnlyList<CatalogueSong> QueryPage(int page)=>singer is null?
            local.Search.BySpell(lastSpell,0,0,new(page),queryContext?.Invoke()??new()):
            local.Singers.BySpell(singer.Id,lastSpell,0,0,new(page),queryContext?.Invoke()??new());
        void ResetQuery() { currentPage=0;lastTotalSize=0;nextPageTimer.Stop();ShowResults(fixtureSongs??QueryPage(0)); }
        void LoadNextPage()
        {
            if(singer is null||fixtureSongs is not null||songGrid is null||Results.Count==0||lastTotalSize==Results.Count)return;
            var lastVisible=Math.Min(Results.Count-1,(int)((songGrid.VerticalOffset+songGrid.ViewportHeight-1)/147)*3+2);
            // Preserve the source's unusually early last-visible > count/3 gate.
            if(lastVisible<=Results.Count/3)return;
            lastTotalSize=Results.Count;ShowResults(Results.Concat(QueryPage(++currentPage)).ToArray(),true);
        }
        refreshQuery=ResetQuery;
        BuildKeys();input.Clear();refreshQuery();
        return canvas;
    }

    private BitmapImage Bitmap(string name)=>new(new Uri(Path.Combine(root,name)));
    private Image Icon(string name,double width,double height)=>new() { Source=Bitmap(name),Width=width,Height=height,Stretch=Stretch.Fill };
    private static Color Color(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
    private static SolidColorBrush Brush(string hex)=>new(Color(hex));
    private static LinearGradientBrush Gradient(string start,string end)=>new(Color(start),Color(end),new Point(0,.5),new Point(1,.5));
    private static TextBlock Text(string text,double size)=>new() { Text=text,FontSize=size,Foreground=Brushes.White,FontFamily=OriginalFont.Family };
    private static void Put(Canvas canvas,UIElement child,double x,double y)
    { Canvas.SetLeft(child,x);Canvas.SetTop(child,y);canvas.Children.Add(child); }
    private static void Click(Border button,Action action,bool outline=false)
    {
        button.MouseLeftButtonDown+=(_,e)=> { button.CaptureMouse();if(outline) { button.BorderBrush=Brushes.White;button.BorderThickness=new(2); }e.Handled=true; };
        button.MouseLeftButtonUp+=(_,e)=> { var inside=new Rect(0,0,button.ActualWidth,button.ActualHeight).Contains(e.GetPosition(button));
            button.ReleaseMouseCapture();if(inside)action();e.Handled=true; };
        button.LostMouseCapture+=(_,_)=> { if(outline)button.BorderThickness=new(0); };
    }
}
