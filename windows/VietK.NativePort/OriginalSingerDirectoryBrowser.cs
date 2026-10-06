using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// SingerNameForRecyclerFragment, view_singer_recycler and its 4 x 2 adapter.
public sealed class OriginalSingerDirectoryBrowser(string root,SongBrowserContract contract,MoreContract more,
    LocalSongDatabase database,SongGridContract grid)
{
    public NativePlayback? Playback { get; set; }
    public NativeSingerPictures? Pictures { get; set; }
    public NativeSearchOptions? SearchOptions { get; set; }
    internal bool OriginalPopupArtworkAvailable=>File.Exists(SingerAsset("dialog_category_background.9.png"))&&File.Exists(SingerAsset("dialog_category_selected.9.png"));
    public event Action? HomeRequested;
    public event Action<OriginalSinger>? SingerRequested;
    public VietnameseSearchInput? Input=>keyboard?.Input;
    public int Country { get; private set; }
    public int Sex { get; private set; }
    public int CurrentPage { get; private set; }=1;
    public int TotalPages { get; private set; }
    public IReadOnlyList<OriginalSinger> LoadedSingers=>singers;
    internal Popup? TypePopup=>popup;
    private SongBrowser? keyboard;
    private readonly List<OriginalSinger> singers=[];
    private Canvas? tiles;
    private StackPanel? categories;
    private Popup? popup;
    private TextBlock? current,total;
    private FrameworkElement? previous,next;
    private TextBlock? empty;
    private string spell="";
    private int batch;
    private Point? drag;
    public Canvas Create()
    {
        Country=Sex=0;CurrentPage=1;spell="";
        keyboard=new SongBrowser(root,contract,more,database,grid) { Playback=Playback,SearchOptions=SearchOptions };
        var canvas=keyboard.CreateSearchShell();canvas.Tag="original-singer-directory";
        var area=new Canvas { Width=765,Height=500,Tag="singer-directory-area" };
        area.Children.Add(new Border { Width=765,Height=500,CornerRadius=new(5),BorderThickness=new(2),BorderBrush=Brush("#195375be"),
            Background=new LinearGradientBrush(new GradientStopCollection { new(Brush("#33fb00cb").Color,0),new(Brush("#33490ba6").Color,.5),new(Brush("#33c01be2").Color,1) },new Point(0,1),new Point(1,0)),IsHitTestVisible=false });
        Put(canvas,area,32,21);
        categories=new StackPanel { Orientation=Orientation.Horizontal,Height=30 };
        Put(area,categories,15,10);
        tiles=new Canvas { Width=760,Height=460,ClipToBounds=true,Tag="singer-directory-tiles" };
        var viewport=new Canvas { Width=760,Height=460,ClipToBounds=true };
        Put(area,viewport,5,40);Put(viewport,tiles,0,15);
        empty=Label("Không có kết quả phù hợp",28);empty.Width=760;empty.TextAlignment=TextAlignment.Center;
        Put(area,empty,5,250);
        var pager=new StackPanel { Width=114,Orientation=Orientation.Horizontal,Tag="singer-directory-pager" };
        previous=PageButton("icon_pre_page.png","‹",()=>GoToPage(CurrentPage-1));pager.Children.Add(previous);
        current=Label("1",20);current.Width=28;current.TextAlignment=TextAlignment.Center;pager.Children.Add(current);
        pager.Children.Add(new TextBlock { Text="/",FontSize=16,Foreground=Brushes.White,Opacity=.2,VerticalAlignment=VerticalAlignment.Center });
        total=Label("0",16);total.Opacity=.2;total.Width=28;total.TextAlignment=TextAlignment.Center;pager.Children.Add(total);
        next=PageButton("icon_next_page.png","›",()=>GoToPage(CurrentPage+1));pager.Children.Add(next);
        Put(canvas,pager,300,529);
        var back=new Border { Width=more.BackWidth,Height=more.BackHeight,CornerRadius=new(more.BackCorner),
            Tag="singer-directory-back",Background=Gradient(more.BackStartColor,more.BackEndColor),
            Child=new Image { Source=new BitmapImage(new Uri(Path.Combine(root,"icon_back.png"))),Width=27,Height=20,Stretch=Stretch.Fill } };
        Click(back,()=>HomeRequested?.Invoke());Put(canvas,back,contract.BackX,530);
        popup=new Popup { Placement=PlacementMode.Bottom,PlacementTarget=categories,StaysOpen=false,AllowsTransparency=true };
        canvas.Unloaded+=(_,_)=>popup.IsOpen=false;
        drag=null;
        tiles.PreviewMouseLeftButtonDown+=(_,e)=>drag=e.GetPosition(tiles);
        tiles.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            if(drag is { } start&&Math.Abs(e.GetPosition(tiles).X-start.X)>30)
            { GoToPage(CurrentPage+(e.GetPosition(tiles).X<start.X?1:-1));e.Handled=true; }
            drag=null;
        };
        tiles.MouseWheel+=(_,e)=> { GoToPage(CurrentPage+(e.Delta<0?1:-1));e.Handled=true; };
        Input!.SpellRequested+=value=> { spell=value;Reload(); };
        RenderCategories();Reload();return canvas;
    }
    public void GoToPage(int page)
    {
        if(page<1||page>TotalPages)return;
        CurrentPage=page;
        // Original prefetch gate: two visible pages before the loaded boundary.
        var loadedPages=(singers.Count+7)/8;
        if(loadedPages<TotalPages&&CurrentPage>=loadedPages-2)
            singers.AddRange(database.SingerDirectory.BySpell(spell,Country,Sex,++batch));
        RenderPage();
    }
    private void Reload()
    {
        batch=0;CurrentPage=1;singers.Clear();
        singers.AddRange(database.SingerDirectory.BySpell(spell,Country,Sex));
        TotalPages=(database.SingerDirectory.CountBySpell(spell,Country,Sex)+7)/8;
        if(TotalPages==0)CurrentPage=0;
        RenderPage();
    }
    private void RenderCategories()
    {
        if(categories is null)return;categories.Children.Clear();
        var home=Label("Ca sĩ",22);home.FontWeight=FontWeights.Bold;home.Margin=new(10,0,0,0);
        home.Tag="singer-directory-home";home.MouseLeftButtonUp+=(_,e)=> { HomeRequested?.Invoke();e.Handled=true; };categories.Children.Add(home);
        categories.Children.Add(new Border { Width=2,Height=16,Background=Brush("#33ffffff"),Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center });
        for(var index=0;index<OriginalSingerDirectory.Countries.Count;index++)
        {
            var position=index;var label=Label(OriginalSingerDirectory.Countries[index],16);
            label.Foreground=Brush(index==Country?"#ffffe761":"#33ffffff");
            var stack=new StackPanel();stack.Children.Add(label);
            stack.Children.Add(new Border { Height=2,Margin=new(0,3,0,0),Background=index==Country?Brush("#ffffe761"):Brushes.Transparent });
            var item=new Border { Padding=new(15,0,15,0),Child=stack,Tag="singer-country:"+index,Background=Brushes.Transparent };
            Click(item,()=> { Country=position;spell="";Input?.Clear();Reload();RenderCategories();ShowSexPopup(); });categories.Children.Add(item);
        }
    }
    private void ShowSexPopup()
    {
        if(popup is null)return;
        var content=new StackPanel { Margin=new(20,30,20,30) };
        var title=Label("Phân loại",16);title.Margin=new(35,0,0,0);content.Children.Add(title);
        var choices=new UniformGrid { Columns=6 };
        for(var index=0;index<OriginalSingerDirectory.Sexes.Count;index++)
        {
            var position=index;var text=Label(OriginalSingerDirectory.Sexes[index],16);text.TextAlignment=TextAlignment.Center;text.TextWrapping=TextWrapping.Wrap;
            var padded=new Border { Padding=new(15,10,15,10),Child=text };
            var item=new Border { Margin=new(0,7,0,7),Tag="singer-sex:"+index,Background=Brushes.Transparent,
                Child=index==Sex?OriginalNinePatch.Wrap(padded,SingerAsset("dialog_category_selected.9.png"),Brush("#663d225b")):padded };
            Click(item,()=> { if(Sex==position)return;Sex=position;spell="";Input?.Clear();Reload();ShowSexPopup(); });choices.Children.Add(item);
        }
        content.Children.Add(choices);
        var background=OriginalNinePatch.Wrap(content,SingerAsset("dialog_category_background.9.png"),Brush("#ff251136"));
        background.Width=752;popup.Child=background;
        popup.IsOpen=true;
    }
    private void RenderPage()
    {
        if(tiles is null||empty is null||current is null||total is null||previous is null||next is null)return;
        tiles.Children.Clear();empty.Visibility=singers.Count==0?Visibility.Visible:Visibility.Collapsed;
        var portrait=Path.Combine(OriginalSupplement.Root,"ambience","singer","defaultsmall.png");
        var visible=singers.Skip((CurrentPage-1)*8).Take(8).ToArray();
        for(var index=0;index<visible.Length;index++)
        {
            var singer=visible[index];var column=new StackPanel();
            var image=new Image { Width=180,Height=180,Stretch=Stretch.Fill,
                Source=File.Exists(portrait)?new BitmapImage(new Uri(Path.GetFullPath(portrait))):null };
            column.Children.Add(image);Pictures?.Load(image,singer);
            var name=Label(singer.Name,20);name.Height=32;name.Width=180;name.TextAlignment=TextAlignment.Center;
            name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;
            name.Background=Brush("#59000000");column.Children.Add(name);
            var item=new Border { Width=180,Height=212,Background=Brushes.Transparent,Child=column,Tag="singer-card:"+singer.Id };
            OriginalPressFeedback.Bind(item,.97,true);
            // Horizontal GridLayoutManager has two spans: fill each column first.
            item.MouseLeftButtonUp+=(_,e)=> { if(drag is { } start&&Math.Abs(e.GetPosition(tiles).X-start.X)>30)return;
                if(popup is not null)popup.IsOpen=false;SingerRequested?.Invoke(singer);e.Handled=true; };
            Put(tiles,item,index/2*190+5,index%2*230+9);
        }
        current.Text=CurrentPage.ToString();total.Text=TotalPages.ToString();
        previous.Visibility=CurrentPage>1?Visibility.Visible:Visibility.Hidden;
        next.Visibility=CurrentPage<TotalPages?Visibility.Visible:Visibility.Hidden;
    }
    private FrameworkElement PageButton(string resource,string fallback,Action action)
    {
        var path=File.Exists(SingerAsset(resource))?SingerAsset(resource):Path.Combine(root,resource);var button=new Border { Width=24,Height=30,Background=Brushes.Transparent,Tag="singer-page:"+resource,
            Child=File.Exists(path)?new Image { Source=new BitmapImage(new Uri(path)),Stretch=Stretch.None }:Label(fallback,24) };
        OriginalPressFeedback.Bind(button,1.2);
        Click(button,action);return button;
    }
    private static TextBlock Label(string text,double size)=>new() { Text=text,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center };
    private static string SingerAsset(string name)=>Path.Combine(OriginalSupplement.Root,"ambience","singer",name);
    private static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    private static LinearGradientBrush Gradient(string start,string end)=>new(Brush(start).Color,Brush(end).Color,new Point(0,.5),new Point(1,.5));
    private static void Put(Canvas canvas,UIElement child,double x,double y) { Canvas.SetLeft(child,x);Canvas.SetTop(child,y);canvas.Children.Add(child); }
    private static void Click(Border item,Action action)
    { item.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; }; }
}
