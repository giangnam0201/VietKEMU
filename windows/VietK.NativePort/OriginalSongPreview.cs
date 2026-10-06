using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// PreviewDialog: a separate muted loop, never the current TV decoder.
public sealed class OriginalSongPreview : IDisposable
{
    private readonly Func<Canvas?> panel;
    private readonly Func<int,string?> localPath;
    private readonly Func<int,bool> queued;
    private readonly Action<int> order,collect;
    private Canvas? host,overlay;
    private WindowsVideoDecoder? decoder;
    private DispatcherTimer? reveal;
    private TextBlock? title,singer,tip;
    private Border? cover;
    private int songId,generation;
    internal int Frames { get; private set; }
    internal int LoopCount { get; private set; }
    internal bool IsOpen=>overlay is not null;
    internal bool VideoVisible=>cover?.Visibility==Visibility.Collapsed;
    internal WindowsVideoDecoder? Decoder=>decoder;
    internal bool OriginalArtworkAvailable=>File.Exists(Asset("preview_dialog_button_addsong.png"))&&File.Exists(Asset("dialog_common_close_n.png"));
    public OriginalSongPreview(Func<Canvas?> panel,Func<int,string?> localPath,Func<int,bool> queued,Action<int> order,Action<int> collect)
    { this.panel=panel;this.localPath=localPath;this.queued=queued;this.order=order;this.collect=collect; }
    public void Show(LocalSong song)
    {
        Close();host=panel();if(host is null)return;
        songId=song.Id;Frames=0;LoopCount=0;var current=++generation;
        overlay=new Canvas { Width=1280,Height=800,Background=Brushes.Transparent,Focusable=true,Tag="song-preview" };
        var content=new Canvas { Width=650,Height=530 };
        Put(overlay,new Border { Width=650,Height=530,CornerRadius=new(10),Background=Brush("#ff481740"),Child=content },315,135);
        overlay.MouseLeftButtonUp+=(_,e)=> { if(ReferenceEquals(e.OriginalSource,overlay))Close(); };
        content.MouseLeftButtonUp+=(_,e)=>e.Handled=true;
        overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        var heading=new StackPanel { Orientation=Orientation.Horizontal,Height=60,MaxWidth=550,HorizontalAlignment=HorizontalAlignment.Center };
        title=Text(song.Name,24,375);singer=Text(song.Singer,19,150);singer.Margin=new(15,0,0,0);
        heading.Children.Add(title);heading.Children.Add(singer);
        heading.Measure(new Size(550,60));Put(content,heading,(650-heading.DesiredSize.Width)/2,0);
        var close=Button("×",50,50);close.Tag="preview-close";close.FontSize=30;close.Background=Brushes.Transparent;
        if(File.Exists(Asset("dialog_common_close_n.png")))close.Content=new Image { Source=Bitmap(Asset("dialog_common_close_n.png")),Stretch=Stretch.None };
        close.PreviewMouseLeftButtonDown+=(_,_)=> { if(File.Exists(Asset("dialog_common_close_h.png")))close.Content=new Image { Source=Bitmap(Asset("dialog_common_close_h.png")),Stretch=Stretch.None }; };
        close.Click+=(_,_)=>Close();Put(content,close,600,5);
        var media=new Canvas { Width=450,Height=350 };
        var surface=new Image { Width=450,Height=253,Stretch=Stretch.Uniform };
        Put(media,new Border { Width=450,Height=253,Background=Brushes.Black },0,0);Put(media,surface,0,0);
        cover=new Border { Width=450,Height=253,Background=Brushes.Black };tip=Text("Đang tải",24,450);tip.Foreground=Brush("#ff414245");cover.Child=tip;Put(media,cover,0,0);
        var add=Button("Chọn bài",160,50);add.Tag="preview-order";
        add.Click+=(_,_)=> { order(songId);RefreshQueue();Feedback?.Invoke("phát theo yêu cầu thành công"); };Put(media,add,40,280);
        var favorite=Button("Bộ sưu tập",160,50);favorite.Tag="preview-collect";favorite.Click+=(_,_)=>collect(songId);Put(media,favorite,260,280);
        Put(content,media,100,75);host.Children.Add(overlay);overlay.Focus();RefreshQueue(true);
        var path=localPath(song.Id);
        if(string.IsNullOrEmpty(path)||!File.Exists(path)) { tip.Text="Tải video không thành công";return; }
        try
        {
            // A preview has no audio path. Disabling it at decoder creation also
            // prevents a startup burst before an audio output accepts mute.
            decoder=new WindowsVideoDecoder(host.Dispatcher,disableAudio:true);var player=decoder;
            player.SetMuted(true);player.Native.Mute=true;surface.Source=player.VideoSurface;
            player.VideoFrameChanged+=()=> { if(current==generation)Frames++; };
            player.Started=()=>
            {
                player.Native.Mute=true;
                if(reveal is not null)return;
                reveal=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(800) };
                reveal.Tick+=(_,_)=> { reveal?.Stop();if(current==generation&&cover is not null)cover.Visibility=Visibility.Collapsed; };
                reveal.Start();
            };
            player.Native.EndReached+=(_,_)=>host?.Dispatcher.BeginInvoke(new Action(()=>
            { if(current==generation&&decoder==player) { LoopCount++;player.SetSource(path);player.PrepareAsync(); } }));
            player.Native.EncounteredError+=(_,_)=>host?.Dispatcher.BeginInvoke(new Action(()=>
            { if(current==generation&&tip is not null&&cover is not null) { reveal?.Stop();cover.Visibility=Visibility.Visible;tip.Text="Tải video không thành công"; } }));
            player.SetSource(path);player.PrepareAsync();
        }
        catch(Exception error) when(error is IOException or InvalidOperationException or ArgumentException)
        { decoder?.Dispose();decoder=null;tip.Text="Tải video không thành công"; }
    }
    public event Action<string>? Feedback;
    public void RefreshQueue(bool initial=false)
    {
        if(title is null||singer is null)return;
        var selected=queued(songId);
        title.Foreground=Brush(selected?"#ffffdd1e":"#ffffffff");
        singer.Foreground=Brush(selected?(initial?"#ffffde00":"#fff9e51d"):"#ffb4b3b4");
    }
    public void Close()
    {
        ++generation;reveal?.Stop();reveal=null;
        if(overlay is not null)host?.Children.Remove(overlay);
        overlay=null;decoder?.Dispose();decoder=null;cover=null;title=null;singer=null;tip=null;host=null;
    }
    public void Dispose()=>Close();
    private static string Asset(string name)=>Path.Combine(OriginalSupplement.Root,"ambience","preview",name);
    private static BitmapImage Bitmap(string path)=>new(new Uri(Path.GetFullPath(path)));
    private static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
    private static TextBlock Text(string value,double size,double max)=>new() { Text=value,FontSize=size,MaxWidth=max,FontFamily=OriginalFont.Family,
        Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center };
    private static Button Button(string label,double width,double height)
    {
        var button=new Button { Content=label,Width=width,Height=height,FontSize=20,FontFamily=OriginalFont.Family,Foreground=Brush("#ffb4b3b4"),BorderThickness=new(0),Background=Brush("#ff481740") };
        if(width==160&&File.Exists(Asset("preview_dialog_button_addsong.png")))button.Background=new ImageBrush(Bitmap(Asset("preview_dialog_button_addsong.png")));
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        var labelView=new FrameworkElementFactory(typeof(ContentPresenter));labelView.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);labelView.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(labelView);
        button.Template=new ControlTemplate(typeof(Button)) { VisualTree=border };button.RenderTransformOrigin=new(.5,.5);button.RenderTransform=new ScaleTransform();
        button.PreviewMouseLeftButtonDown+=(_,_)=>((ScaleTransform)button.RenderTransform).ScaleX=((ScaleTransform)button.RenderTransform).ScaleY=.95;
        button.LostMouseCapture+=(_,_)=>((ScaleTransform)button.RenderTransform).ScaleX=((ScaleTransform)button.RenderTransform).ScaleY=1;
        return button;
    }
    private static void Put(Canvas parent,UIElement child,double x,double y) { parent.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
