using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

public sealed record TvOsdContract(double ControlWidth,double ControlHeight,double ControlY,
    double NumberY,double NumberSize,int TimeoutMs);

// KmOSDMessageView control resources and lifetime; MarqueeManager song messages.
public sealed class TelevisionOverlay
{
    public Canvas Canvas { get; }=new() { Width=1280,Height=720,IsHitTestVisible=false };
    public TelevisionBarrage Barrage { get; }=new();
    public TelevisionQr Qr { get; }=new();
    private readonly string root;
    private readonly TvOsdContract contract;
    private readonly Image control,pause,mute;
    private readonly DispatcherTimer muteRepeat;
    private int muteCount;
    public bool Muted { get; private set; }
    public bool MuteVisible=>mute.Visibility==Visibility.Visible;
    internal string? MuteResourceDirectory { get; set; }
    private readonly Canvas expression=new() { Width=330,Height=330,Visibility=Visibility.Collapsed };
    private readonly Image expressionImage=new() { Width=318,Height=318,Stretch=Stretch.Uniform };
    public bool ExpressionVisible=>expression.Visibility==Visibility.Visible;
    private readonly TextBlock number;
    private readonly Canvas marqueeTrain=new();
    private readonly TranslateTransform marqueeShift=new();
    private string marqueeValue="";
    public double MarqueeOffset=>marqueeShift.X;
    public int MarqueeCopyCount=>marqueeTrain.Children.Count;
    private readonly DispatcherTimer timeout;
    private readonly DispatcherTimer pauseRepeat;
    private int pauseCount;
    private readonly Dictionary<string,string> messages;
    private readonly OriginalMarqueeSettings marqueeSettings;
    private string currentSong="",nextSong="";
    public string MarqueeText=>marqueeValue;
    internal string CurrentSong=>currentSong;
    internal string NextSong=>nextSong;
    public string LastControl { get; private set; }="";
    public bool Paused { get; private set; }
    public bool PauseVisible=>pause.Visibility==Visibility.Visible;
    public bool ControlVisible => control.Visibility==Visibility.Visible;
    public TelevisionOverlay(string root,OriginalMarqueeSettings? marqueeSettings=null)
    {
        this.root=root;
        messages=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"player","marquee.json")))!;
        this.marqueeSettings=marqueeSettings??new OriginalMarqueeSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VietKNativePort"));
        contract=JsonSerializer.Deserialize<TvOsdContract>(File.ReadAllText(Path.Combine(root,"player","osd.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("Missing original TV OSD contract");
        var logo=new Image { Source=Bitmap("top-logo.png"),Width=135,Height=64,Stretch=Stretch.Uniform };
        Put(logo,20,10);
        var strip=new Canvas { Width=1060,Height=54,ClipToBounds=true };
        marqueeTrain.RenderTransform=marqueeShift;strip.Children.Add(marqueeTrain);Put(strip,200,10);
        control=new Image { Width=contract.ControlWidth,Height=contract.ControlHeight,Visibility=Visibility.Collapsed };
        Put(Qr.Canvas,49*4d/3,55*4d/3);
        Put(Barrage.Canvas,0,0);
        pause=new Image { Width=contract.ControlWidth,Height=contract.ControlHeight,Source=Bitmap("player/pause.png"),Visibility=Visibility.Collapsed };
        Put(control,(1280-contract.ControlWidth)/2,contract.ControlY);
        Put(pause,(1280-contract.ControlWidth)/2,contract.ControlY);
        mute=new Image { Width=contract.ControlWidth,Height=contract.ControlHeight,Visibility=Visibility.Collapsed };
        Put(mute,(1280-contract.ControlWidth)/2,contract.ControlY);
        number=Text("",contract.NumberSize);number.Width=300;number.TextAlignment=TextAlignment.Center;
        number.Visibility=Visibility.Collapsed;Put(number,490,contract.NumberY);
        timeout=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(contract.TimeoutMs) };
        Put(expression,(1280-330)/2,65);
        expression.Children.Add(expressionImage);System.Windows.Controls.Canvas.SetLeft(expressionImage,6);System.Windows.Controls.Canvas.SetTop(expressionImage,6);
        timeout.Tick+=(_,_)=> { timeout.Stop();control.Visibility=Visibility.Collapsed;number.Visibility=Visibility.Collapsed; };
        pauseRepeat=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(contract.TimeoutMs/6) };
        pauseRepeat.Tick+=(_,_)=>RepeatPause();
        muteRepeat=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(contract.TimeoutMs/6) };
        muteRepeat.Tick+=(_,_)=>RepeatMute();
    }
    public void SetSong(string current,string next="",string advertisement="")
    {
        currentSong=current;nextSong=next;
        if(string.IsNullOrWhiteSpace(advertisement))advertisement=marqueeSettings.LocalText;
        var key=string.IsNullOrWhiteSpace(current)?"marquee_not_demand_tip":
            string.IsNullOrWhiteSpace(next)?"marquee_current_playing_tip":"marquee_current_playing_and_next_play_tip";
        var value=messages[key].Replace("%1$s",current).Replace("%2$s",next)+
            (string.IsNullOrWhiteSpace(advertisement)?"":"     "+advertisement);
        if(value==marqueeValue)return; // Queue count refresh must not restart scrolling.
        marqueeValue=value;
        var text=value+new string('\u3000',10); // MarqueeTextView.END_TEXT.
        var measured=Text(text,26);measured.Measure(new Size(double.PositiveInfinity,54));
        var pitch=Math.Max(1,measured.DesiredSize.Width);var phase=marqueeShift.X%pitch;
        marqueeTrain.Children.Clear();
        for(var i=0;i<(int)Math.Ceiling(1060/pitch)+2;i++)
        {
            var copy=Text(text,26);marqueeTrain.Children.Add(copy);
            System.Windows.Controls.Canvas.SetLeft(copy,i*pitch);System.Windows.Controls.Canvas.SetTop(copy,10);
        }
        // Original view advances 2px per 30ms. Repeated copies make the wrap
        // continuous rather than teleporting the only text block to the left.
        marqueeShift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(phase,phase-pitch,
            TimeSpan.FromSeconds(pitch/(2.0/.030))) { RepeatBehavior=RepeatBehavior.Forever });
    }
    public void RefreshAdvertisement()=>SetSong(currentSong,nextSong);
    public void SetPaused(bool value)
    {
        if(Paused==value)return;
        Paused=value;pauseCount=0;pauseRepeat.Stop();pause.Visibility=Visibility.Collapsed;
        if(value)mute.Visibility=Visibility.Collapsed;
        if(value)
        {
            // KmOSDMessageView.ShowPauseTime: 7 visible ticks, 2 hidden;
            // another control holds the pause image hidden until it expires.
            timeout.Stop();control.Visibility=Visibility.Collapsed;number.Visibility=Visibility.Collapsed;
            RepeatPause();pauseRepeat.Start();
        }
    }
    private void RepeatPause()
    {
        if(control.Visibility==Visibility.Visible) { pause.Visibility=Visibility.Collapsed;pauseCount=7; }
        if(pauseCount>6)pause.Visibility=Visibility.Collapsed;
        else if(pause.Visibility!=Visibility.Visible)pause.Visibility=Visibility.Visible;
        pauseCount=(pauseCount+1)%9;
    }
    public void SetMuted(bool value)
    {
        if(Muted==value)return;
        Muted=value;muteCount=0;muteRepeat.Stop();mute.Visibility=Visibility.Collapsed;
        if(value)
        {
            mute.Source=MuteBitmap("mute");
            timeout.Stop();control.Visibility=Visibility.Collapsed;number.Visibility=Visibility.Collapsed;
            RepeatMute();muteRepeat.Start();
        }
    }
    private BitmapImage? MuteBitmap(string name)
    {
        var file=Path.Combine(MuteResourceDirectory??Path.Combine(OriginalSupplement.Root,"ambience","player"),name+".png");
        return File.Exists(file)?new BitmapImage(new Uri(Path.GetFullPath(file))):null;
    }
    private void RepeatMute()
    {
        if(ControlVisible) { mute.Visibility=Visibility.Collapsed;muteCount=7; }
        if(muteCount>6)mute.Visibility=Visibility.Collapsed;
        else if(!MuteVisible)
        {
            // ShowMuteTime returns without advancing the counter while paused.
            if(Paused)return;
            control.Visibility=Visibility.Collapsed;number.Visibility=Visibility.Collapsed;
            mute.Visibility=mute.Source is null?Visibility.Collapsed:Visibility.Visible;
        }
        muteCount=(muteCount+1)%9;
    }
    public void ShowMuteOff()
    {
        LastControl="unmute";control.Source=MuteBitmap("unmute");
        control.Visibility=control.Source is null?Visibility.Collapsed:Visibility.Visible;
        number.Visibility=Visibility.Collapsed;timeout.Stop();timeout.Start();
    }
    public void ShowControl(string name,int? value=null)
    {
        LastControl=name;control.Source=Bitmap("player/"+name+".png");control.Visibility=Visibility.Visible;
        if(Paused) { pause.Visibility=Visibility.Collapsed;pauseCount=7; }
        if(Muted) { mute.Visibility=Visibility.Collapsed;muteCount=7; }
        number.Text=value?.ToString()??"";number.Visibility=value.HasValue?Visibility.Visible:Visibility.Collapsed;
        control.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.3,1,TimeSpan.FromMilliseconds(150)));
        timeout.Stop();timeout.Start();
    }
    public void Stop() { timeout.Stop();pauseRepeat.Stop();muteRepeat.Stop();Barrage.Dispose();marqueeShift.BeginAnimation(TranslateTransform.XProperty,null); }
    public void ShowExpression(BitmapSource picture,string avatarPath)
    {
        expressionImage.Source=picture;
        while(expression.Children.Count>1)expression.Children.RemoveAt(1);
        if(File.Exists(avatarPath))
        {
            var avatar=new Image { Width=32,Height=32,Source=new BitmapImage(new Uri(avatarPath)),Stretch=Stretch.UniformToFill,
                Clip=new EllipseGeometry(new Point(16,16),16,16) };
            expression.Children.Add(avatar);System.Windows.Controls.Canvas.SetLeft(avatar,26);System.Windows.Controls.Canvas.SetTop(avatar,169);
        }
        expression.Visibility=Visibility.Visible;
    }
    public void HideExpression()=>expression.Visibility=Visibility.Collapsed;
    private BitmapImage Bitmap(string path)=>new(new Uri(Path.Combine(root,path)));
    private static TextBlock Text(string value,double size)=>new() { Text=value,FontSize=size,
        FontFamily=OriginalFont.Family,Foreground=Brushes.White,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=3,ShadowDepth=1 } };
    private void Put(UIElement child,double x,double y)
    { Canvas.Children.Add(child);System.Windows.Controls.Canvas.SetLeft(child,x);System.Windows.Controls.Canvas.SetTop(child,y); }
}
