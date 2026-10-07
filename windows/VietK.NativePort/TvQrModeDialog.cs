using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

// SettingTvQrcodeModeDialog: staged selection, apply only on confirmation.
public sealed class TvQrModeDialog
{
    public Canvas Overlay { get; }=new() { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    public int Pending { get; private set; }
    public int PendingSize { get; private set; }
    private readonly Canvas host;
    private readonly TelevisionQr qr;
    private readonly TextBlock[] labels=new TextBlock[3];
    private readonly FrameworkElement[] markers=new FrameworkElement[3];
    public TvQrModeDialog(Canvas host,TelevisionQr qr)
    {
        this.host=host;this.qr=qr;Pending=qr.State.Mode;PendingSize=qr.SizePercent;
        var content=new Canvas { Width=418,Height=480 };
        var box=new Border { Width=418,Height=480,CornerRadius=new(10),Background=new SolidColorBrush(Color.FromRgb(72,23,64)),Child=content };
        Put(Overlay,box,431,156);
        Put(content,Label("Chế độ hiển thị mã QR lên TV",418,60,24,true),0,0);
        var close=new Border { Width=60,Height=60,Background=Brushes.Transparent,Tag="qr-mode:close" };
        var closePath=Path.Combine(OriginalSupplement.Root,"ambience","preview","dialog_common_close_n.png");
        close.Child=File.Exists(closePath)?new Image { Source=new BitmapImage(new Uri(Path.GetFullPath(closePath))),Stretch=Stretch.None }:Label("×",60,60,30,true);
        close.MouseLeftButtonUp+=(_,e)=> { Close();e.Handled=true; };Put(content,close,358,0);
        Put(content,new Border { Width=418,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,60);
        var choices=new[]{"Luôn hiển thị","Ẩn sau 20 giây","Không hiển thị"};
        for(var i=0;i<choices.Length;i++)
        {
            var index=i;var option=new Canvas { Width=418,Height=60,Background=Brushes.Transparent,Tag="qr-mode:option:"+i };
            labels[i]=Label(choices[i],255,60,24);Put(option,labels[i],28,0);
            var marker=new Canvas { Width=104,Height=60,IsHitTestVisible=false,Tag="qr-mode:selected:"+i };
            var selected=Label("Đã chọn",70,60,16);selected.Foreground=new SolidColorBrush(Color.FromRgb(238,156,63));Put(marker,selected,0,0);
            var iconPath=Path.Combine(OriginalSupplement.Root,"ambience","settings","setting_general_language_selected.png");
            UIElement check=File.Exists(iconPath)?new Image { Width=24,Height=24,Source=new BitmapImage(new Uri(Path.GetFullPath(iconPath))),Stretch=Stretch.None }:
                new System.Windows.Shapes.Path { Width=24,Height=24,Data=Geometry.Parse("M 3,12 L 9,18 L 21,5"),Stroke=selected.Foreground,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round };
            Put(marker,check,80,18);markers[i]=marker;Put(option,marker,284,0);
            option.MouseLeftButtonUp+=(_,e)=> { Select(index);e.Handled=true; };Put(content,option,0,60+i*62);
            if(i<2)Put(content,new Border { Width=418,Height=2,Background=new SolidColorBrush(Color.FromArgb(48,255,255,255)) },0,120+i*62);
        }
        var sizeLabel=Label($"Kích thước QR: {PendingSize}%",362,40,22);
        Put(content,sizeLabel,28,252);
        var size=new Slider { Minimum=25,Maximum=200,Value=PendingSize,TickFrequency=5,IsSnapToTickEnabled=true,
            Width=362,Height=42,Tag="qr-mode:size" };
        size.ValueChanged+=(_,_)=> { PendingSize=(int)Math.Round(size.Value);sizeLabel.Text=$"Kích thước QR: {PendingSize}%"; };
        Put(content,size,28,297);
        Put(content,Label("50% mặc định • 100% kích thước trước",362,32,16),28,341);
        var background=new LinearGradientBrush { StartPoint=new(0,1),EndPoint=new(0,0) };
        background.GradientStops.Add(new(Color.FromRgb(4,160,227),0));background.GradientStops.Add(new(Color.FromRgb(0,250,246),1));
        var confirm=new Border { Width=140,Height=46,CornerRadius=new(26),Background=background,Child=Label("Xác nhận",140,46,24,true),Tag="qr-mode:confirm" };
        OriginalPressFeedback.Bind(confirm,.9);confirm.MouseLeftButtonUp+=(_,e)=> { Confirm();e.Handled=true; };Put(content,confirm,139,398);
        Select(Pending);
        Overlay.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource==Overlay)Close(); };
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        Panel.SetZIndex(Overlay,1000);host.Children.Add(Overlay);
    }
    public void Select(int mode)
    {
        if(mode is <0 or >2)throw new ArgumentOutOfRangeException(nameof(mode));Pending=mode;
        for(var i=0;i<labels.Length;i++) { labels[i].Foreground=i==mode?new SolidColorBrush(Color.FromRgb(238,156,63)):Brushes.White;markers[i].Visibility=i==mode?Visibility.Visible:Visibility.Hidden; }
    }
    public void Confirm(bool persist=true) { qr.SetMode(Pending,persist);qr.SetSizePercent(PendingSize,persist);Close(); }
    public void Close()=>host.Children.Remove(Overlay);
    private static TextBlock Label(string text,double width,double height,double size,bool centered=false)=>new() {
        Text=text,Width=width,Height=height,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
        TextAlignment=centered?TextAlignment.Center:TextAlignment.Left,Padding=new(0,Math.Max(0,(height-size*1.3)/2),0,0),TextWrapping=TextWrapping.Wrap };
    private static void Put(Canvas canvas,UIElement child,double x,double y) { canvas.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
