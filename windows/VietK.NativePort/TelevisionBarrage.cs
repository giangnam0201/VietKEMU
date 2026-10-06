using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// BarrageManager's local-user bitmap, original delayed R2L motion, no duplicate
// merging. Retention follows the APK bytecode; Android font metrics still need
// visual comparison before the bitmap line height is fully verified.
public sealed class TelevisionBarrage : IDisposable
{
    public Canvas Canvas { get; }=new() { Width=1280,Height=720,ClipToBounds=true,IsHitTestVisible=false };
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(15) };
    private readonly List<Message> messages=[];
    private readonly OriginalBarrageRetainer retainer=new(720);
    private int bitmapWidth=1280;
    public int PendingCount=>messages.Count(message=>message.Start>clock.ElapsedMilliseconds);
    public int VisibleCount=>messages.Count(message=>message.Placed);
    public long Duration=>OriginalBarrageMotion.ScrollDuration(1280,4f/3);
    public double? FirstVisibleLeft=>messages.FirstOrDefault(message=>message.Placed)?.Transform.X;
    public TelevisionBarrage() { timer.Tick+=(_,_)=>Update(); }
    public bool Send(string text,string? resourceRoot=null)
    {
        if(string.IsNullOrEmpty(text))return false;
        var root=Path.GetFullPath(Path.Combine(resourceRoot??OriginalSupplement.Root,"ambience"));
        var ellipse=Path.Combine(root,"barrage_ellipse.png");var rocket=Path.Combine(root,"barrage_rocket.png");
        if(!File.Exists(ellipse) || !File.Exists(rocket))return false;
        var formatted=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(OriginalFont.Family,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),30,Brushes.Yellow,1);
        if(formatted.Width>bitmapWidth)bitmapWidth=(int)(100+formatted.Width);
        var width=bitmapWidth;
        var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())
        {
            var background=new BitmapImage(new Uri(ellipse));var launch=new BitmapImage(new Uri(rocket));
            context.DrawImage(background,new Rect(55,0,background.PixelWidth,background.PixelHeight));
            context.DrawImage(launch,new Rect(0,10,launch.PixelWidth,launch.PixelHeight));
            context.DrawText(formatted,new Point(80,45-formatted.Baseline));
        }
        var bitmap=new RenderTargetBitmap(width,64,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();
        var transform=new TranslateTransform(1280,0);
        var image=new Image { Width=width,Height=64,Source=bitmap,RenderTransform=transform,Visibility=Visibility.Collapsed };
        Canvas.Children.Add(image);
        messages.Add(new(clock.ElapsedMilliseconds+1200,new(1280,width+13,Duration),image,transform));
        timer.Start();return true;
    }
    private void Update()
    {
        var now=clock.ElapsedMilliseconds;
        foreach(var message in messages.ToArray())
        {
            if(now<message.Start)continue;
            if(now-message.Start>=message.Motion.Duration) { Remove(message);continue; }
            if(!message.Placed)
            {
                if(now==message.Start)continue; // BaseDanmaku.isOutside at its exact start.
                var placement=retainer.Place(message.Layout,now);
                if(!placement.Accepted) { Remove(message);continue; }
                message.Placed=true;message.Transform.Y=placement.Top+5;message.Image.Visibility=Visibility.Visible;
            }
            message.Transform.X=message.Motion.Left(now-message.Start)+5;
        }
        if(messages.Count==0)timer.Stop();
    }
    private void Remove(Message message) { Canvas.Children.Remove(message.Image);messages.Remove(message); }
    public void Clear() { timer.Stop();Canvas.Children.Clear();messages.Clear();retainer.Clear(); }
    public void Dispose()=>Clear();
    private sealed class Message(long start,OriginalBarrageMotion motion,Image image,TranslateTransform transform)
    {
        public long Start { get; }=start;
        public OriginalBarrageMotion Motion { get; }=motion;
        public Image Image { get; }=image;
        public TranslateTransform Transform { get; }=transform;
        public bool Placed { get; set; }
        public OriginalBarrageRetainer.Item Layout { get; }=new(motion,start,74);
    }
}
