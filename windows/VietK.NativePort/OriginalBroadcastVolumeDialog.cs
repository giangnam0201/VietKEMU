using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// BroadcastVolumeSettingDialog saves on every dismiss; there is no confirm or
// cancel branch. Muting zeros the staged slider, and unmuting leaves it at zero.
public sealed class OriginalBroadcastVolumeDialog
{
    public Canvas Overlay { get; }=new() { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    public int Pending { get; private set; }
    public bool Muted { get; private set; }
    internal OriginalDefaultVolumeSeekBar SeekBar { get; }
    private readonly Canvas host;
    private readonly OriginalBroadcastVolumeSettings settings;
    private readonly Border toggle;
    private bool closed;
    public OriginalBroadcastVolumeDialog(Canvas host,OriginalBroadcastVolumeSettings settings)
    {
        this.host=host;this.settings=settings;Pending=settings.Volume;Muted=settings.Muted;
        var content=new Canvas { Width=500,Height=324 };
        Put(Overlay,new Border { Width=500,Height=324,CornerRadius=new(10),Background=new SolidColorBrush(Color.FromRgb(72,23,64)),Child=content },390,238);
        Put(content,Label("Âm lượng màn hình chờ",500,60,24,true),0,0);
        Put(content,new Border { Width=500,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,60);
        Put(content,Label("Tắt tiếng",250,35,24),74,92);
        toggle=new Border { Width=56,Height=35,Tag="broadcast-volume:mute",Background=Brushes.Transparent };
        toggle.MouseLeftButtonUp+=(_,e)=> { ToggleMute();e.Handled=true; };Put(content,toggle,370,92);
        SeekBar=new OriginalDefaultVolumeSeekBar(Pending);SeekBar.ProgressChanged+=value=>Pending=value;Put(content,SeekBar,122,147);
        Icon("volume_decrease",79,()=>Change(-1));Icon("volume_increase",378,()=>Change(1));
        Put(content,Label("⚠   "+OriginalBroadcastVolumeSettings.RestartTip,500,20,14,true),0,236);
        UpdateMute();Panel.SetZIndex(Overlay,1000);host.Children.Add(Overlay);
        Overlay.MouseLeftButtonDown+=(_,e)=> { if(ReferenceEquals(e.OriginalSource,Overlay))Close(); };
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        void Icon(string name,double x,Action action)
        {
            var element=(FrameworkElement?)OriginalDefaultVolumeDialog.Artwork(name)??Label(name=="volume_decrease"?"−":"+",43,42,36,true);
            element.Width=43;element.Height=42;element.Tag="broadcast-volume:"+name;OriginalPressFeedback.Bind(element,.9);
            element.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };Put(content,element,x,155.5);
        }
    }
    public void Change(int delta) { if(!Muted)SeekBar.Progress=Math.Clamp(Pending+delta,0,20); }
    public void ToggleMute() { Muted=!Muted;UpdateMute(); }
    private void UpdateMute()
    {
        if(Muted)SeekBar.Progress=0;SeekBar.IsEnabled=!Muted;SeekBar.Focusable=!Muted;
        var path=Path.Combine(OriginalSupplement.Root,"ambience",Muted?"dc_overseas_set_on.png":"dc_overseas_set_off.png");
        toggle.Child=File.Exists(path)?new Image { Source=new BitmapImage(new Uri(Path.GetFullPath(path))),Stretch=Stretch.Fill }:
            new Border { CornerRadius=new(18),Background=Muted?Brushes.DarkTurquoise:Brushes.Gray,
                Child=new Border { Width=23,Height=23,CornerRadius=new(12),Background=Brushes.White,Margin=new(5),HorizontalAlignment=Muted?HorizontalAlignment.Right:HorizontalAlignment.Left } };
    }
    public void Close() { if(closed)return;settings.Save(Pending,Muted);closed=true;host.Children.Remove(Overlay); }
    private static TextBlock Label(string text,double width,double height,double size,bool centered=false)=>new() {
        Text=text,Width=width,Height=height,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
        TextAlignment=centered?TextAlignment.Center:TextAlignment.Left,Padding=new(0,Math.Max(0,(height-size*1.3)/2),0,0) };
    private static void Put(Canvas canvas,UIElement child,double x,double y) { canvas.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
