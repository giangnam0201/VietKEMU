using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VietK.NativePort;

public sealed record TvOsdContract(double ControlWidth,double ControlHeight,double ControlY,
    double NumberY,double NumberSize,int TimeoutMs);

// KmOSDMessageView control resources and lifetime; MarqueeManager song messages.
public sealed class TelevisionOverlay
{
    public Canvas Canvas { get; }=new() { Width=1280,Height=720,IsHitTestVisible=false };
    private readonly string root;
    private readonly TvOsdContract contract;
    private readonly Image control,pause;
    private readonly TextBlock number,marquee;
    private readonly DispatcherTimer timeout;
    public string LastControl { get; private set; }="";
    public bool Paused => pause.Visibility==Visibility.Visible;
    public bool ControlVisible => control.Visibility==Visibility.Visible;
    public TelevisionOverlay(string root)
    {
        this.root=root;
        contract=JsonSerializer.Deserialize<TvOsdContract>(File.ReadAllText(Path.Combine(root,"player","osd.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("Missing original TV OSD contract");
        var logo=new Image { Source=Bitmap("top-logo.png"),Width=135,Height=64,Stretch=Stretch.Uniform };
        Put(logo,20,10);
        var strip=new Canvas { Width=1060,Height=54,ClipToBounds=true };
        marquee=Text("",26);strip.Children.Add(marquee);System.Windows.Controls.Canvas.SetTop(marquee,10);Put(strip,200,10);
        control=new Image { Width=contract.ControlWidth,Height=contract.ControlHeight,Visibility=Visibility.Collapsed };
        pause=new Image { Width=contract.ControlWidth,Height=contract.ControlHeight,Source=Bitmap("player/pause.png"),Visibility=Visibility.Collapsed };
        Put(control,(1280-contract.ControlWidth)/2,contract.ControlY);
        Put(pause,(1280-contract.ControlWidth)/2,contract.ControlY);
        number=Text("",contract.NumberSize);number.Width=300;number.TextAlignment=TextAlignment.Center;
        number.Visibility=Visibility.Collapsed;Put(number,490,contract.NumberY);
        timeout=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(contract.TimeoutMs) };
        timeout.Tick+=(_,_)=> { timeout.Stop();control.Visibility=Visibility.Collapsed;number.Visibility=Visibility.Collapsed; };
    }
    public void SetSong(string current,string next="",string advertisement="")
    {
        marquee.Text=(string.IsNullOrWhiteSpace(current)?"": "Đang phát: "+current)+
            (string.IsNullOrWhiteSpace(next)?"":"     Tiếp theo: "+next)+
            (string.IsNullOrWhiteSpace(advertisement)?"":"     "+advertisement);
        marquee.Measure(new Size(double.PositiveInfinity,54));
        marquee.BeginAnimation(System.Windows.Controls.Canvas.LeftProperty,new DoubleAnimation(1060,-marquee.DesiredSize.Width,
            TimeSpan.FromSeconds(Math.Max(15,(1060+marquee.DesiredSize.Width)/70))) { RepeatBehavior=RepeatBehavior.Forever });
    }
    public void SetPaused(bool value)
    {
        pause.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        if(value)pause.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.4,1,TimeSpan.FromMilliseconds(300)));
    }
    public void ShowControl(string name,int? value=null)
    {
        LastControl=name;control.Source=Bitmap("player/"+name+".png");control.Visibility=Visibility.Visible;
        number.Text=value?.ToString()??"";number.Visibility=value.HasValue?Visibility.Visible:Visibility.Collapsed;
        control.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.3,1,TimeSpan.FromMilliseconds(150)));
        timeout.Stop();timeout.Start();
    }
    public void Stop()=>timeout.Stop();
    private BitmapImage Bitmap(string path)=>new(new Uri(Path.Combine(root,path)));
    private static TextBlock Text(string value,double size)=>new() { Text=value,FontSize=size,
        FontFamily=OriginalFont.Family,Foreground=Brushes.White,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=3,ShadowDepth=1 } };
    private void Put(UIElement child,double x,double y)
    { Canvas.Children.Add(child);System.Windows.Controls.Canvas.SetLeft(child,x);System.Windows.Controls.Canvas.SetTop(child,y); }
}
