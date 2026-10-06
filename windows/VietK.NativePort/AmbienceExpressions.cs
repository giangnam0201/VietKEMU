using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace VietK.NativePort;

// SendExpressionView -> KmOSDMessageView.showTftpPic. Factory media stays local;
// an independent sound output leaves the karaoke decoder and queue untouched.
public sealed class AmbienceExpressions : IDisposable
{
    public static readonly (string File,string Label)[] Items =
    [ ("memeda","Hôn"),("xianhua","Tặng hoa"),("zan","Thích"),("baodeng","Yêu"),
      ("wuyafeiguo","Vỗ tay"),("zajidan","Chê"),("birthday","Mừng sinh nhật"),("zaiyiqi","Bên nhau") ];
    private readonly TelevisionOverlay overlay;
    private readonly TelevisionWindow? television;
    public int CurrentTab { get; private set; }=10;
    private readonly LibVLC library;
    public LibVLCSharp.Shared.MediaPlayer Sound { get; }
    private Media? media;
    private readonly DispatcherTimer expiry=new() { Interval=TimeSpan.FromMilliseconds(6000) };
    public string ActiveExpression { get; private set; }="";
    public AmbienceExpressions(TelevisionOverlay overlay,TelevisionWindow? television=null)
    {
        this.television=television;
        this.overlay=overlay;library=new LibVLC("--no-video","--no-video-title-show");
        Sound=new LibVLCSharp.Shared.MediaPlayer(library) { Volume=50 };
        Sound.Playing+=(_,_)=>Sound.Volume=50;
        expiry.Tick+=(_,_)=>Hide();
    }
    public bool Show(string name,string? resourceRoot=null)
    {
        if(!Items.Any(item=>item.File==name))return false;
        var root=Path.Combine(resourceRoot??OriginalSupplement.Root,"ambience");
        var image=Path.Combine(root,name+".png");if(!File.Exists(image))return false;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.UriSource=new Uri(Path.GetFullPath(image));bitmap.EndInit();bitmap.Freeze();
        Hide();overlay.ShowExpression(bitmap,Path.Combine(root,"osd_local_defaultfig.png"));ActiveExpression=name;
        var wave=Path.Combine(root,name+".wav");
        if(File.Exists(wave))
        {
            media=new Media(library,new Uri(Path.GetFullPath(wave)));media.AddOption(":input-repeat=65535");
            Sound.Volume=50;Sound.Play(media);
        }
        expiry.Start();return true;
    }
    public void Hide()
    {
        expiry.Stop();Sound.Stop();media?.Dispose();media=null;overlay.HideExpression();ActiveExpression="";
    }
    public void ShowDialog(Window owner,string? resourceRoot=null)
    {
        if(owner.Content is not Viewbox { Child:Canvas panel })return;
        if(panel.Children.OfType<Canvas>().Any(child=>Equals(child.Tag,"original-expression-dialog")))return;
        var directory=Path.Combine(resourceRoot??OriginalSupplement.Root,"ambience");
        if(!Items.Any(item=>File.Exists(Path.Combine(directory,item.File+".png"))))
        { MessageBox.Show(owner,"Chưa tìm thấy tài nguyên biểu cảm gốc trên máy này.","Vui nhộn");return; }
        var dim=new Canvas { Width=1280,Height=800,Tag="original-expression-dialog",Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
        var content=new Canvas { Width=780,Height=450 };
        var border=new Border { Width=780,Height=450,CornerRadius=new CornerRadius(10),
            Background=new SolidColorBrush(Color.FromRgb(0x48,0x17,0x40)),Child=content };
        Put(dim,border,155,175);panel.Children.Add(dim);
        void Close()=>panel.Children.Remove(dim);
        dim.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource==dim)Close(); };
        var heading=new Border { CornerRadius=new CornerRadius(30),Padding=new Thickness(15,0,15,0),
            Background=new LinearGradientBrush(Color.FromRgb(0xc0,0x37,0xd0),Color.FromRgb(0x74,0x37,0xe9),0),
            Child=new TextBlock { Text="Biểu cảm",FontSize=22,Foreground=Brushes.White,FontFamily=OriginalFont.Family } };
        Put(content,heading,30,10);
        var televisionHeading=new Border { CornerRadius=new CornerRadius(30),Padding=new Thickness(15,0,15,0),
            Background=Brushes.Transparent,Child=new TextBlock { Text="TV",FontSize=22,Foreground=Brushes.White,FontFamily=OriginalFont.Family } };
        if(television is not null)Put(content,televisionHeading,315,10);
        var close=new Image { Width=30,Height=30,Source=LoadImage(Path.Combine(directory,"dc_overseas_popup_close.png")) };
        close.MouseLeftButtonUp+=(_,_)=>Close();Put(content,close,730,10);
        var grid=new Canvas { Width=704,Height=340 };
        var scroll=new ScrollViewer { Width=740,Height=358,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=grid };
        Put(content,scroll,40,60);
        Canvas? tvPage=null;
        if(television is not null)
        {
            tvPage=new Canvas { Width=780,Height=390,Visibility=Visibility.Collapsed };
            var text=new StackPanel();text.Children.Add(new TextBlock { Text="Tắt màn hình TV",FontSize=18,Foreground=Brushes.White,FontFamily=OriginalFont.Family });
            text.Children.Add(new TextBlock { Text="Có thể tắt màn hình TV, chỉ phát nhạc",FontSize=16,
                Foreground=new SolidColorBrush(Color.FromRgb(0x9b,0x8d,0xb0)),FontFamily=OriginalFont.Family });
            Put(tvPage,text,90,47);
            var toggle=new Image { Width=56,Height=35,Tag="original-tv-mask-toggle" };
            void UpdateToggle(bool value)=>toggle.Source=LoadImage(Path.Combine(directory,value?"dc_overseas_set_on.png":"dc_overseas_set_off.png"));
            UpdateToggle(television.IsScreenMasked);toggle.MouseLeftButtonUp+=(_,e)=>
            { television.SetScreenMask(!television.IsScreenMasked);e.Handled=true; };
            television.ScreenMaskChanged+=UpdateToggle;
            dim.Unloaded+=(_,_)=>television.ScreenMaskChanged-=UpdateToggle;
            Put(tvPage,toggle,554,50.5);Put(content,tvPage,0,50);
        }
        void SelectTab(int tab)
        {
            CurrentTab=tab;scroll.Visibility=tab==10?Visibility.Visible:Visibility.Collapsed;
            if(tvPage is not null)tvPage.Visibility=tab==12?Visibility.Visible:Visibility.Collapsed;
            var selected=new LinearGradientBrush(Color.FromRgb(0xc0,0x37,0xd0),Color.FromRgb(0x74,0x37,0xe9),0);
            heading.Background=tab==10?selected:Brushes.Transparent;televisionHeading.Background=tab==12?selected:Brushes.Transparent;
        }
        heading.MouseLeftButtonUp+=(_,e)=> { SelectTab(10);e.Handled=true; };
        televisionHeading.MouseLeftButtonUp+=(_,e)=> { SelectTab(12);e.Handled=true; };
        SelectTab(CurrentTab);
        for(var i=0;i<Items.Length;i++)
        {
            var item=Items[i];var imagePath=Path.Combine(directory,item.File+".png");if(!File.Exists(imagePath))continue;
            var tile=new StackPanel { Width=176,Height=150,Background=Brushes.Transparent,
                RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(1,1) };
            tile.Children.Add(new Image { Source=LoadImage(imagePath),Width=120,Height=120,Stretch=Stretch.Uniform });
            tile.Children.Add(new TextBlock { Text=item.Label,FontSize=20,Height=30,Foreground=Brushes.White,
                TextAlignment=TextAlignment.Center,FontFamily=OriginalFont.Family });
            tile.MouseLeftButtonUp+=(_,e)=>
            {
                var scale=(ScaleTransform)tile.RenderTransform;
                // AnimCommonUtils.scaleAnim: 500ms, no repeat, reset on finish.
                var animation=new DoubleAnimation(1,1.15,TimeSpan.FromMilliseconds(500)) { FillBehavior=FillBehavior.Stop };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
                Show(item.File,resourceRoot);e.Handled=true;
            };
            Put(grid,tile,(i/2)*176,20+(i%2)*170);
        }
    }
    private static BitmapImage LoadImage(string path)=>new(new Uri(Path.GetFullPath(path)));
    private static void Put(Canvas canvas,UIElement child,double x,double y)
    { Canvas.SetLeft(child,x);Canvas.SetTop(child,y);canvas.Children.Add(child); }
    public void Dispose() { Hide();Sound.Dispose();library.Dispose(); }
}
