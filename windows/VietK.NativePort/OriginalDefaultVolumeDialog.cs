using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// DefaultVolumeSettingDialog / dialog_setting_default_volume_view.
public sealed class OriginalDefaultVolumeDialog
{
    public Canvas Overlay { get; }=new() { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    public int Pending { get; private set; }
    internal OriginalDefaultVolumeSeekBar SeekBar { get; }
    private readonly Canvas host;
    private readonly OriginalDefaultVolumeSettings settings;
    public OriginalDefaultVolumeDialog(Canvas host,OriginalDefaultVolumeSettings settings)
    {
        this.host=host;this.settings=settings;Pending=settings.Volume;
        var content=new Canvas { Width=518,Height=338 };
        Put(Overlay,new Border { Width=518,Height=338,CornerRadius=new(10),Background=new SolidColorBrush(Color.FromRgb(72,23,64)),Child=content },381,166);
        Put(content,Label("Âm lượng mặc định",518,60,24),0,0);
        Put(content,new Border { Width=518,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,60);
        var hint=Label("Sau khi hệ thống khởi động và đóng, phòng sẽ sử dụng mức âm lượng này,\nkhuyến nghị từ 15, tối đa là 20.",488,30,12);
        hint.Foreground=new SolidColorBrush(Color.FromArgb(204,255,255,255));Put(content,hint,15,60);
        SeekBar=new OriginalDefaultVolumeSeekBar(Pending);SeekBar.Released+=value=>Pending=value;Put(content,SeekBar,131,125.5);
        Icon("volume_decrease","−",83,()=>Change(-1));Icon("volume_increase","+",387,()=>Change(1));
        Button("Hủy",81.5,Close,"cancel");Button("Xác nhận",296.5,Confirm,"confirm");
        Overlay.MouseLeftButtonDown+=(_,e)=> { if(ReferenceEquals(e.OriginalSource,Overlay))Close(); };
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        Panel.SetZIndex(Overlay,1000);host.Children.Add(Overlay);
        void Icon(string name,string fallback,double x,Action action)
        {
            var image=Artwork(name);var element=(FrameworkElement?)image??Label(fallback,48,48,36);
            element.Width=element.Height=48;element.Tag="default-volume:"+name;
            OriginalPressFeedback.Bind(element,.9);element.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };Put(content,element,x,131);
        }
        void Button(string title,double x,Action action,string tag)
        {
            var element=new Border { Width=140,Height=46,CornerRadius=new(23),Background=new SolidColorBrush(Color.FromRgb(105,45,123)),Child=Label(title,140,46,24),Tag="default-volume:"+tag };
            OriginalPressFeedback.Bind(element,.9);element.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };Put(content,element,x,270);
        }
    }
    public void Change(int delta) { Pending=Math.Clamp(Pending+delta,0,20);SeekBar.Progress=Pending; }
    public void Confirm() { settings.Save(Pending);Close(); }
    public void Close()=>host.Children.Remove(Overlay);
    internal static Image? Artwork(string name)
    {
        var path=Path.Combine(OriginalSupplement.Root,"ambience","volume",name+".png");
        return File.Exists(path)?new Image { Source=new BitmapImage(new Uri(Path.GetFullPath(path))),Stretch=Stretch.Fill }:null;
    }
    private static TextBlock Label(string text,double width,double height,double size)=>new() {
        Text=text,Width=width,Height=height,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
        TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Padding=new Thickness(0,Math.Max(0,(height-size*1.3)/2),0,0) };
    private static void Put(Canvas canvas,UIElement child,double x,double y) { canvas.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}

// Draws the value on the thumb as DefaultVolumeSeekBar.onDraw does. Dragging
// changes progress; only release updates the dialog's staged mCurrentVolume.
internal sealed class OriginalDefaultVolumeSeekBar : FrameworkElement
{
    private int progress;
    private bool dragging;
    private readonly ImageSource? thumb=OriginalDefaultVolumeDialog.Artwork("volume_seekbar_thumb")?.Source;
    internal event Action<int>? Released;
    internal int Progress { get=>progress;set { progress=Math.Clamp(value,0,20);InvalidateVisual(); } }
    internal OriginalDefaultVolumeSeekBar(int value)
    {
        Width=256;Height=59;Focusable=true;Progress=value;
        MouseLeftButtonDown+=(_,e)=> { Focus();dragging=true;CaptureMouse();Update(e.GetPosition(this).X);e.Handled=true; };
        MouseMove+=(_,e)=> { if(dragging)Update(e.GetPosition(this).X); };
        MouseLeftButtonUp+=(_,e)=> { if(dragging) { Update(e.GetPosition(this).X);dragging=false;ReleaseMouseCapture();Released?.Invoke(Progress); }e.Handled=true; };
        LostMouseCapture+=(_,_)=>dragging=false;
        KeyDown+=(_,e)=>
        {
            var next=e.Key switch { Key.Left or Key.Down=>Progress-1,Key.Right or Key.Up=>Progress+1,Key.Home=>0,Key.End=>20,_=>Progress };
            if(e.Key is Key.Left or Key.Down or Key.Right or Key.Up or Key.Home or Key.End) { Progress=next;Released?.Invoke(Progress);e.Handled=true; }
        };
        Unloaded+=(_,_)=> { dragging=false;ReleaseMouseCapture(); };
    }
    private void Update(double x)=>Progress=(int)Math.Round(Math.Clamp((x-29)/(ActualWidth-59),0,1)*20,MidpointRounding.AwayFromZero);
    protected override void OnRender(DrawingContext drawing)
    {
        var center=29+Progress/20d*(ActualWidth-59);
        drawing.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(74,53,115)),null,new Rect(20,25,ActualWidth-40,9),5,5);
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(251,185,19)),null,new Rect(20,25,Math.Max(0,center-20),9),5,5);
        if(thumb is not null)drawing.DrawImage(thumb,new Rect(center-29.5,0,59,59));
        else drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(105,45,123)),null,new Point(center,29.5),29.5,29.5);
        var text=new FormattedText(Progress.ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(OriginalFont.Family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),18,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(text,new Point(center-text.Width/2,ActualHeight/2+7-text.Baseline));
    }
}
