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
    private readonly Image image=new() { Width=91*4d/3,Height=91*4d/3,Stretch=Stretch.Fill };
    private readonly TextBlock code=new() { Width=91*4d/3,Height=50,FontSize=36,FontWeight=FontWeights.Bold,
        TextAlignment=TextAlignment.Center,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(224,21,21,21)) };
    private readonly TranslateTransform slide=new();
    private readonly string modeFile;
    public TelevisionQr()
    {
        Canvas.RenderTransform=slide;Canvas.Children.Add(image);Canvas.Children.Add(code);
        System.Windows.Controls.Canvas.SetTop(code,image.Height);
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VietKNativePort");
        modeFile=Path.Combine(directory,"tv-qr-mode.json");
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
        var pixels=OriginalMobileQr.Render(OriginalMobileQr.TelevisionPayload(binding),true);
        image.Width=image.Height=91*4d/3;
        image.Source=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Bgra32,null,pixels.Pixels,pixels.Width*4);
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        code.Text=binding.TvBindCode;
        code.Visibility=string.IsNullOrEmpty(binding.TvBindCode)?Visibility.Collapsed:Visibility.Visible;
        Canvas.Visibility=Visibility.Visible;Refresh();
    }
    public void ConfigureLocalRemote(string url)
    {
        // The LAN pairing token is longer than the original binding code.
        // Use a larger matrix so it remains readable on a scaled Windows TV.
        image.Width=image.Height=180;
        var pixels=OriginalMobileQr.Render(url,true);
        image.Source=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Bgra32,null,pixels.Pixels,pixels.Width*4);
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        code.Text="";code.Visibility=Visibility.Collapsed;Canvas.Visibility=Visibility.Visible;Refresh();
    }
    public void SetMode(int mode,bool persist=true)
    {
        State.ChangeMode(mode);Refresh();
        if(persist) { Directory.CreateDirectory(Path.GetDirectoryName(modeFile)!);File.WriteAllText(modeFile,JsonSerializer.Serialize(mode)); }
    }
    private void Refresh()
    {
        slide.X=State.Tick(Environment.TickCount64,Canvas.Width);
        image.Visibility=State.ImageVisible?Visibility.Visible:Visibility.Hidden;
    }
}
