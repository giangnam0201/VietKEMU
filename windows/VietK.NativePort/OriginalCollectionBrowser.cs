using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Ellipse=System.Windows.Shapes.Ellipse;
using Rectangle=System.Windows.Shapes.Rectangle;
using VietK.Core;

namespace VietK.NativePort;

// CollectFragment's active LinearLayoutManager/SongRecyclerAdapter path.
// Unused two-column/pager constants do not describe its rendered list.
public sealed class OriginalCollectionBrowser : IDisposable
{
    private readonly string root;
    private readonly OriginalCollectionProfiles profiles;
    private readonly Func<int,LocalSong?> lookup;
    private readonly Func<SongQueryContext> context;
    private readonly Func<IReadOnlyList<SelectedPlaylistItem>> queue;
    private readonly NativeCollectionControls controls;
    private readonly SongGridContract icons;
    private Canvas? screen,listArea;
    private TextBlock? account,empty;
    private FrameworkElement? userHint,passwordHint;
    private ScrollViewer? scroll;
    private Canvas? track;
    private Rectangle? thumb;
    private bool loggingIn;
    private int generation;
    private long lastPreviewClick;
    private string loadedUser="";
    internal TextBox? Username { get; private set; }
    internal PasswordBox? Password { get; private set; }
    internal Button? LoginButton { get; private set; }
    internal FrameworkElement? LoginProgress { get; private set; }
    internal IReadOnlyList<LocalSong> Rows { get; private set; }=[];
    internal bool LoginArtworkAvailable { get; private set; }
    public event Action? HomeRequested;
    public event Action<LocalSong,string>? ActionRequested;
    public OriginalCollectionBrowser(string root,OriginalCollectionProfiles profiles,Func<int,LocalSong?> lookup,
        Func<SongQueryContext> context,Func<IReadOnlyList<SelectedPlaylistItem>> queue,NativeCollectionControls controls,SongGridContract icons)
    {
        this.root=root;this.profiles=profiles;this.lookup=lookup;this.context=context;this.queue=queue;this.controls=controls;this.icons=icons;
        profiles.Changed+=ProfileChanged;
    }
    public Canvas Create()
    {
        ++generation;loggingIn=false;
        screen=new Canvas { Width=1280,Height=800,ClipToBounds=true,Background=new ImageBrush(Bitmap("main_bg.jpg")) { Stretch=Stretch.UniformToFill } };
        // fragment paddingLeft 60 + sidebar marginLeft 22, marginTop 40.
        var sidebar=new Canvas { Width=355,Height=490 };
        Put(screen,new Border { Width=355,Height=490,CornerRadius=new(10),Child=sidebar,
            Background=new LinearGradientBrush(Color.FromRgb(36,90,247),Color.FromRgb(144,23,255),new Point(0,.5),new Point(1,.5)) },82,40);
        var art=Path.Combine(OriginalSupplement.Root,"ambience","collection","icon_favorite_for_login.png");
        LoginArtworkAvailable=File.Exists(art);
        if(LoginArtworkAvailable)Put(sidebar,new Image { Width=152,Height=128,Source=new BitmapImage(new Uri(Path.GetFullPath(art))) },101.5,48);
        account=Text("chưa đăng nhập",28,355);account.TextAlignment=TextAlignment.Center;account.Measure(new Size(355,double.PositiveInfinity));Put(sidebar,account,0,200);
        var nameY=200+account.DesiredSize.Height+15;var passwordY=nameY+45+10+5;
        Username=new TextBox { Tag="collection-username",Width=299,Height=45,MaxLength=12,FontSize=18,FontFamily=OriginalFont.Family,
            Foreground=Brushes.White,Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(10,0,0,0),VerticalContentAlignment=VerticalAlignment.Center };
        Password=new PasswordBox { Tag="collection-password",Width=299,Height=45,MaxLength=12,FontSize=18,FontFamily=OriginalFont.Family,
            Foreground=Brushes.White,Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(10,0,0,0),VerticalContentAlignment=VerticalAlignment.Center };
        var nameBackground=new Border { Width=299,Height=45,CornerRadius=new(10),Background=Brush("#3302191b"),Child=Username };
        var passwordBackground=new Border { Width=299,Height=45,CornerRadius=new(10),Background=Brush("#3302191b"),Child=Password };
        // 28sp default-font line height, followed by 15dp/5dp field margins.
        Put(sidebar,nameBackground,28,nameY);Put(sidebar,passwordBackground,28,passwordY);
        var nameLabel=Text("Vui lòng nhập tên người dùng",18,289);nameLabel.Foreground=Brush("#ccffffff");nameLabel.IsHitTestVisible=false;Put(sidebar,nameLabel,38,nameY+12);
        var passwordLabel=Text("Mật khẩu",18,289);passwordLabel.Foreground=Brush("#ccffffff");passwordLabel.IsHitTestVisible=false;Put(sidebar,passwordLabel,38,passwordY+12);
        userHint=nameBackground;passwordHint=passwordBackground;
        Username.TextChanged+=(_,_)=>nameLabel.Visibility=Username.Text.Length==0&&profiles.CurrentUser.Length==0?Visibility.Visible:Visibility.Hidden;
        Password.PasswordChanged+=(_,_)=>passwordLabel.Visibility=Password.Password.Length==0&&profiles.CurrentUser.Length==0?Visibility.Visible:Visibility.Hidden;
        LoginButton=NativeCollectionButton.Create("đăng nhập",18);LoginButton.Tag="collection-login";Put(sidebar,LoginButton,107.5,passwordY+45+10+35);
        LoginButton.Click+=async (_,_)=>
        {
            if(profiles.CurrentUser.Length>0) { controls.Logout();return; }
            if(loggingIn)return;
            var user=Username.Text;var password=Password.Password;
            if(user.Length<4||password.Length<4)
            {
                controls.Feedback(user.Length==0||password.Length==0?"Tên người dùng và mật khẩu không thể để trống":"Tên người dùng và mật khẩu không thể ít hơn 4 chữ số");
                Refresh();return;
            }
            var attempt=generation;loggingIn=true;LoginButton.Visibility=Visibility.Hidden;LoginProgress!.Visibility=Visibility.Visible;
            // Yield so the native progress state paints before the tiny local
            // file transaction. Model observers stay on the WPF dispatcher.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            if(attempt!=generation)return;
            var result=profiles.Login(user,password,calibrate:false);loggingIn=false;
            if(result!=CollectionLoginResult.Success)controls.Feedback(result==CollectionLoginResult.WrongPassword?"Sai mật mã, vui lòng nhập lại!":"Tạo người dùng thất bại");
            Refresh();
        };
        var progress=new Ellipse { Width=32,Height=32,Stroke=Brushes.White,StrokeThickness=3,StrokeDashArray=new DoubleCollection { 5,2 },RenderTransformOrigin=new(.5,.5),RenderTransform=new RotateTransform(),Visibility=Visibility.Hidden };
        ((RotateTransform)progress.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(1)) { RepeatBehavior=RepeatBehavior.Forever });
        LoginProgress=progress;Put(sidebar,progress,161.5,passwordY+45+10+20);
        listArea=new Canvas { Width=744,Height=490,ClipToBounds=true,RenderTransform=new TranslateTransform() };
        var listBackground=new Border { Width=744,Height=490,CornerRadius=new(5),BorderThickness=new(2),BorderBrush=Brush("#195375be"),
            Background=new LinearGradientBrush(new GradientStopCollection { new((Color)ColorConverter.ConvertFromString("#33fb00cb"),0),new((Color)ColorConverter.ConvertFromString("#33490ba6"),.5),new((Color)ColorConverter.ConvertFromString("#33c01be2"),1) },new Point(0,1),new Point(1,0)) };
        var container=new Canvas { Width=744,Height=490,RenderTransform=new TranslateTransform() };Put(container,listBackground,0,0);Put(container,listArea,0,0);Put(screen,container,477,40);
        var category=new StackPanel { Orientation=Orientation.Horizontal,Height=30,Background=Brushes.Transparent };
        var title=Text("Bộ sưu tập",22,double.NaN);title.FontWeight=FontWeights.Bold;title.Margin=new(10,0,0,0);category.Children.Add(title);
        category.Children.Add(new Border { Width=2,Height=16,Margin=new(12,0,15,0),Background=Brush("#33ffffff"),VerticalAlignment=VerticalAlignment.Center });
        category.MouseLeftButtonUp+=(_,e)=> { HomeRequested?.Invoke();e.Handled=true; };Put(listArea,category,0,20);
        empty=Text("Danh sách rỗng",24,744);empty.TextAlignment=TextAlignment.Center;Put(listArea,empty,0,230);
        scroll=new ScrollViewer { Width=744,Height=440,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            CanContentScroll=false,PanningMode=PanningMode.VerticalOnly,ClipToBounds=true,Tag="collection-scroll" };Put(listArea,scroll,0,50);
        track=new Canvas { Width=3,Height=440,Background=Brush("#33000000"),IsHitTestVisible=false };thumb=new Rectangle { Width=3,Fill=Brush("#66ffffff") };track.Children.Add(thumb);Put(listArea,track,741,50);
        scroll.ScrollChanged+=(_,_)=>UpdateThumb();
        var back=new Border { Tag="collection-back",Width=90,Height=38,CornerRadius=new(30),Background=new LinearGradientBrush(Color.FromRgb(192,55,208),Color.FromRgb(116,55,233),0),
            Child=new Image { Width=27,Height=20,Source=Bitmap("icon_back.png"),Stretch=Stretch.Fill } };
        Wire(back,()=>HomeRequested?.Invoke());Put(screen,back,82,544);
        Reload();container.Loaded+=(_,_)=>((TranslateTransform)container.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(744,0,TimeSpan.FromMilliseconds(250)) { EasingFunction=new QuadraticEase { EasingMode=EasingMode.EaseIn },FillBehavior=FillBehavior.Stop });
        return screen;
    }
    private void ProfileChanged()
    { if(loadedUser!=profiles.CurrentUser)Reload();else Refresh(); }
    private void Reload()
    { loadedUser=profiles.CurrentUser;Rows=profiles.Visible(lookup,new(0,192),context());Refresh(); }
    public void Refresh()
    {
        if(screen is null||listArea is null||scroll is null)return;
        var logged=profiles.CurrentUser.Length>0;account!.Text=logged?profiles.CurrentUser:"chưa đăng nhập";
        userHint!.Visibility=passwordHint!.Visibility=logged?Visibility.Hidden:Visibility.Visible;
        if(!logged) { Username!.Clear();Password!.Clear(); }
        foreach(var label in ((Canvas)userHint.Parent).Children.OfType<TextBlock>().Where(text=>text.Text is "Vui lòng nhập tên người dùng" or "Mật khẩu"))label.Visibility=logged?Visibility.Hidden:Visibility.Visible;
        LoginButton!.Content=logged?"Thoát ra":"đăng nhập";LoginButton.Visibility=loggingIn?Visibility.Hidden:Visibility.Visible;LoginProgress!.Visibility=loggingIn?Visibility.Visible:Visibility.Hidden;
        var offset=scroll.VerticalOffset;var body=new StackPanel { Width=744 };
        for(var i=0;i<Rows.Count;i++)body.Children.Add(Row(Rows[i],i));scroll.Content=body;
        empty!.Visibility=Rows.Count==0?Visibility.Visible:Visibility.Hidden;scroll.Visibility=Rows.Count==0?Visibility.Hidden:Visibility.Visible;
        scroll.ScrollToVerticalOffset(Math.Min(offset,Math.Max(0,Rows.Count*74-440)));UpdateThumb();
    }
    private Canvas Row(LocalSong song,int index)
    {
        var row=new Canvas { Tag="collection-row:"+song.Id,Width=744,Height=74,ClipToBounds=true,Background=index%2==0?Brush("#195375be"):Brushes.Transparent };
        var orders=queue();var position=orders.ToList().FindIndex(item=>item.SongMetadata.Id==song.Id);
        if(orders.Count>0&&orders[0].LocalFlag==0&&position>=0&&song.LocalFlag==0)position++;
        var tip=position==0?"[Đang phát]":position>0?$"[Đặt trước {position}]":"";
        var marker=Text(tip,18,double.NaN);marker.Foreground=Brush("#ffffe761");marker.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var tipWidth=new FormattedText(tip,System.Globalization.CultureInfo.GetCultureInfo("vi-VN"),FlowDirection.LeftToRight,new Typeface(OriginalFont.Family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),18,Brushes.White,1).Width;
        var title=Text(song.Name,28,double.NaN);title.MaxWidth=Math.Max(0,400-tipWidth);title.TextWrapping=TextWrapping.Wrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.MaxHeight=68;title.Foreground=tip.Length==0?Brushes.White:Brush("#ffffe761");title.Measure(new Size(title.MaxWidth,68));
        Put(row,title,15,(74-title.DesiredSize.Height)/2);Put(row,marker,15+title.DesiredSize.Width+6,27);
        var singer=Text(song.Singer.Replace(",",", "),24,100);singer.MaxHeight=68;singer.TextWrapping=TextWrapping.Wrap;singer.TextTrimming=TextTrimming.CharacterEllipsis;singer.Measure(new Size(100,68));
        singer.MouseLeftButtonUp+=(_,e)=> { ActionRequested?.Invoke(song,"singer");e.Handled=true; };Put(row,singer,477,(74-singer.DesiredSize.Height)/2);
        var collected=profiles.Contains(song.Id);var local=song.LocalFlag is >=1 and <=2;var midi=song.Id is >=100000000 and <=100999999;
        var preview=Icon("preview_dialog_button",34);preview.Visibility=local&&!midi?Visibility.Visible:Visibility.Hidden;preview.Tag="collection-preview:"+song.Id;
        Wire(preview,()=> { var now=Environment.TickCount64;if(now-lastPreviewClick>500) { lastPreviewClick=now;ActionRequested?.Invoke(song,"preview"); } });Put(row,preview,592,20);
        var top=Icon("ic_top_song",34);top.Tag="collection-top:"+song.Id;Wire(top,()=>Order(song,row,index,"top"));Put(row,top,641,20);
        var favorite=Icon(collected?"button_add_song_item_collected_normal":"button_add_song_item_collect",34);favorite.Tag="collection-favorite:"+song.Id;
        Wire(favorite,()=>controls.Collect(song.Id),collected?"button_add_song_item_collected_select":"button_add_song_item_collect_selected");Put(row,favorite,690,20);
        if(!local)Put(row,Icon("icon_online_bg",30),447,27);
        row.MouseLeftButtonUp+=(_,e)=> { e.Handled=true;Order(song,row,index,"order"); };return row;
    }
    private void Order(LocalSong song,Canvas source,int index,string action)
    {
        if(screen is not { } target)return;
        var position=source.TranslatePoint(new Point(),target);var clone=Row(song,index);clone.Tag="collection-order-animation";clone.IsHitTestVisible=false;clone.RenderTransformOrigin=new(.5,.5);
        var scale=new ScaleTransform(1,1);var move=new TranslateTransform();var transforms=new TransformGroup();transforms.Children.Add(scale);transforms.Children.Add(move);clone.RenderTransform=transforms;Put(target,clone,0,0);
        DoubleAnimation Animation(double from,double to,bool accelerated=false)=>new(from,to,TimeSpan.FromMilliseconds(500))
        { EasingFunction=accelerated?new QuadraticEase { EasingMode=EasingMode.EaseIn }:new SineEase { EasingMode=EasingMode.EaseInOut } };
        // SongItemAnim keeps its base-middle offsets even though this overlay
        // is attached to view_main_anim; preserve the original coordinates.
        move.BeginAnimation(TranslateTransform.XProperty,Animation(position.X-30,1000,true));
        move.BeginAnimation(TranslateTransform.YProperty,Animation(position.Y-56,550));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,Animation(1,.2));scale.BeginAnimation(ScaleTransform.ScaleYProperty,Animation(1,.2));
        var alpha=Animation(1,.4);alpha.Completed+=(_,_)=>target.Children.Remove(clone);clone.BeginAnimation(UIElement.OpacityProperty,alpha);
        ActionRequested?.Invoke(song,action);
    }
    private void UpdateThumb()
    {
        if(track is null||thumb is null||scroll is null)return;track.Visibility=Rows.Count>6?Visibility.Visible:Visibility.Hidden;
        var max=Math.Max(1,Rows.Count-6);var height=Math.Clamp(6*(136d-3)/Math.Sqrt(max),68,136);thumb.Height=height;
        Canvas.SetTop(thumb,(int)(440-height)*(int)(scroll.VerticalOffset/74)/max);
    }
    private Image Icon(string name,double size)=>new() { Width=size,Height=size,Source=Bitmap(icons.Icons[name].File),Stretch=Stretch.Uniform };
    private void Wire(FrameworkElement target,Action action,string? pressed=null)
    {
        target.RenderTransformOrigin=new(.5,.5);target.RenderTransform=new ScaleTransform(1,1);
        var saved=(target as Image)?.Source;
        void Scale(double value)
        { var transform=(ScaleTransform)target.RenderTransform;var animation=new DoubleAnimation(value,TimeSpan.FromMilliseconds(25)) { EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } };transform.BeginAnimation(ScaleTransform.ScaleXProperty,animation);transform.BeginAnimation(ScaleTransform.ScaleYProperty,animation); }
        target.MouseLeftButtonDown+=(_,e)=> { target.CaptureMouse();Scale(.9);if(pressed is not null&&target is Image image)image.Source=Bitmap(icons.Icons[pressed].File);e.Handled=true; };
        target.LostMouseCapture+=(_,_)=> { Scale(1);if(target is Image image)image.Source=saved; };
        target.MouseLeftButtonUp+=(_,e)=> { var inside=new Rect(0,0,target.ActualWidth,target.ActualHeight).Contains(e.GetPosition(target));e.Handled=true;target.ReleaseMouseCapture();Scale(1);if(inside)action(); };
    }
    public void RefreshMedia()
    { Rows=Rows.Select(song=>lookup(song.Id)??song).ToArray();Refresh(); }
    public void Dispose() { ++generation;profiles.Changed-=ProfileChanged; }
    private BitmapImage Bitmap(string file)=>new(new Uri(Path.GetFullPath(Path.Combine(root,file))));
    private static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
    private static TextBlock Text(string value,double size,double width)=>new() { Text=value,FontSize=size,Width=width,Foreground=Brushes.White,FontFamily=OriginalFont.Family };
    private static void Put(Canvas parent,UIElement child,double x,double y) { parent.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}

internal static class NativeCollectionButton
{
    internal static Button Create(string label,double size)
    {
        var button=new Button { Content=label,Width=140,Height=46,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
            Background=new LinearGradientBrush(Color.FromRgb(4,160,227),Color.FromRgb(0,250,246),new Point(0,1),new Point(0,0)),BorderThickness=new(0) };
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(26));
        border.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(presenter);button.Template=new ControlTemplate(typeof(Button)) { VisualTree=border };return button;
    }
}
