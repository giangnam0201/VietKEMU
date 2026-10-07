using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// OuterCommonPresentation xhdpi dimensions, mapped to the native 1280 canvas.
// Binding data must come from an authorized device; rendering does not authenticate it.
public sealed class TelevisionQr
{
    public Canvas Canvas { get; }=new() { Width=1280-49*4d/3,Height=620,Visibility=Visibility.Collapsed };
    public OriginalTvQrState State { get; }=new();
    public MobileQrBinding Binding { get; private set; }=new();
    private const double OriginalQrSize=91*4d/3*.25;
    internal const double DisplayScale=1.25;
    public int SizePercent { get; private set; }=50;
    private double SizeScale=>2.5*SizePercent/100d;
    private readonly Image image=new() { Width=OriginalQrSize*DisplayScale,Height=OriginalQrSize*DisplayScale,Stretch=Stretch.Fill };
    private readonly TextBlock code=new() { Width=OriginalQrSize*DisplayScale,Height=12.5*DisplayScale,FontSize=9*DisplayScale,FontWeight=FontWeights.Bold,
        TextAlignment=TextAlignment.Center,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(224,21,21,21)) };
    private readonly TranslateTransform slide=new();
    private readonly string modeFile;
    private readonly string sizeFile;
    public TelevisionQr(string? stateDirectory=null)
    {
        Canvas.RenderTransform=slide;Canvas.Children.Add(image);Canvas.Children.Add(code);
        System.Windows.Controls.Canvas.SetTop(code,image.Height);
        var directory=stateDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VietKNativePort");
        modeFile=Path.Combine(directory,"tv-qr-mode.json");
        sizeFile=Path.Combine(directory,"tv-qr-size.json");
        try { if(File.Exists(sizeFile))SetSizePercent(JsonSerializer.Deserialize<int>(File.ReadAllText(sizeFile)),false); }
        catch(Exception error) when(error is IOException or JsonException or ArgumentOutOfRangeException) { }
        try { if(File.Exists(modeFile))State.ChangeMode(JsonSerializer.Deserialize<int>(File.ReadAllText(modeFile))); }
        catch(Exception error) when(error is IOException or JsonException or ArgumentOutOfRangeException) { }
        try
        {
            var path=Path.Combine(directory,"mobile-binding.json");
            if(File.Exists(path))Configure(JsonSerializer.Deserialize<MobileQrBinding>(File.ReadAllText(path),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??new());
        }
        catch(Exception error) when(error is IOException or JsonException or ArgumentException) { }
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(30) };
        timer.Tick+=(_,_)=>Refresh();timer.Start();
    }
    public void Configure(MobileQrBinding binding)
    {
        Binding=binding;
        if(!binding.CanPresent) { Canvas.Visibility=Visibility.Collapsed;image.Source=null;return; }
        // Keep at least two whole pixels per module when the requested
        // quarter-size is too small. Never resample a larger QR into lost modules.
        var pixels=OriginalMobileQr.RenderCompact(OriginalMobileQr.TelevisionPayload(binding),true,(int)Math.Ceiling(OriginalQrSize));
        image.Source=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Bgra32,null,pixels.Pixels,pixels.Width*4);
        ApplySize();
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        code.Text=binding.TvBindCode;
        code.Visibility=string.IsNullOrEmpty(binding.TvBindCode)?Visibility.Collapsed:Visibility.Visible;
        Canvas.Visibility=Visibility.Visible;Refresh();
    }
    public void ConfigureLocalRemote(string url)
    {
        // Retain the complete matrix; resize only its displayed dimensions.
        var pixels=OriginalMobileQr.RenderCompact(url,true,45);
        image.Source=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Bgra32,null,pixels.Pixels,pixels.Width*4);
        ApplySize();
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        code.Text="";code.Visibility=Visibility.Collapsed;Canvas.Visibility=Visibility.Visible;Refresh();
    }
    public void SetMode(int mode,bool persist=true)
    {
        State.ChangeMode(mode);Refresh();
        if(persist) { Directory.CreateDirectory(Path.GetDirectoryName(modeFile)!);File.WriteAllText(modeFile,JsonSerializer.Serialize(mode)); }
    }
    public void SetSizePercent(int percent,bool persist=true)
    {
        if(percent is <25 or >200)throw new ArgumentOutOfRangeException(nameof(percent));
        SizePercent=percent;ApplySize();
        if(persist) { Directory.CreateDirectory(Path.GetDirectoryName(sizeFile)!);File.WriteAllText(sizeFile,JsonSerializer.Serialize(percent)); }
    }
    private void ApplySize()
    {
        var width=image.Source is BitmapSource bitmap?bitmap.PixelWidth:OriginalQrSize;
        image.Width=image.Height=width*SizeScale;code.Width=image.Width;
        code.Height=12.5*SizeScale;code.FontSize=9*SizeScale;
        System.Windows.Controls.Canvas.SetTop(code,image.Height);
    }
    private void Refresh()
    {
        slide.X=State.Tick(Environment.TickCount64,Canvas.Width);
        image.Visibility=State.ImageVisible?Visibility.Visible:Visibility.Hidden;
    }
}
