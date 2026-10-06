using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

public sealed record GridIcon(string File, double Width, double Height, string OriginalResource,
    string OriginalSha256, string PngSha256);
public sealed record SongGridContract(Dictionary<string,GridIcon> Icons,string Provenance);

// Original grid cell, default stage. Events request the remaining original
// handlers; they do not mark a song queued, downloaded, previewed or collected.
public sealed class SongGrid(string root,SongGridContract contract)
{
    public event Action<CatalogueSong,string>? ActionRequested;
    public event Action<string>? SingerRequested;
    private long lastPreviewClick;

    public ScrollViewer Create(IReadOnlyList<CatalogueSong> songs,
        IReadOnlySet<int>? queued=null,IReadOnlySet<int>? collected=null)
    {
        var body=new Canvas { Width=738,Height=((songs.Count+2)/3)*147,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left };
        for(int index=0;index<songs.Count;index++)
        {
            var song=songs[index];
            if(song.LocalState is null)throw new InvalidDataException("Song grid requires original local-state flags");
            var cell=new Canvas { Width=246,Height=147,ClipToBounds=true };
            var tile=new Canvas { Width=248,Height=142,ClipToBounds=true };
            Put(cell,tile,5,0);Put(body,cell,index%3*246,index/3*147);
            var defaultImage=contract.Icons["icon_song_default"];
            Put(tile,new Image { Source=Bitmap(defaultImage.File),Width=248,Height=146,Stretch=Stretch.UniformToFill },0,0);
            Put(tile,new OutlinedSongName(song.Name,queued?.Contains(song.Id)==true),0,0);
            var bar=new Grid { Width=248,Height=33,Background=Brush("#b3000000") };
            Put(tile,bar,0,109);
            var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
            double actionWidth=0;
            void Icon(string name,string? pressed,string action,double rightMargin)
            {
                var data=contract.Icons[name];
                var image=new Image { Source=Bitmap(data.File),Width=data.Width,Height=data.Height,Stretch=Stretch.Fill,Tag=action+":"+song.Id,
                    VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,rightMargin,0),
                    RenderTransformOrigin=new(.5,.5),RenderTransform=new ScaleTransform(1,1) };
                actionWidth+=data.Width+rightMargin;actions.Children.Add(image);
                if(action=="online")return; // Original online indicator has no click listener.
                void Scale(double target)
                {
                    var transform=(ScaleTransform)image.RenderTransform;
                    var anim=new DoubleAnimation(target,TimeSpan.FromMilliseconds(25))
                        { EasingFunction=new SineEase { EasingMode=EasingMode.EaseInOut } };
                    transform.BeginAnimation(ScaleTransform.ScaleXProperty,anim);
                    transform.BeginAnimation(ScaleTransform.ScaleYProperty,anim);
                }
                image.MouseLeftButtonDown+=(_,e)=> { image.CaptureMouse();Scale(.9);
                    if(pressed is not null)image.Source=Bitmap(contract.Icons[pressed].File);e.Handled=true; };
                image.MouseLeftButtonUp+=(_,e)=> { var inside=new Rect(0,0,image.ActualWidth,image.ActualHeight).Contains(e.GetPosition(image));
                    image.ReleaseMouseCapture();
                    if(inside && action=="preview")
                    {
                        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if(now-lastPreviewClick>500) { ActionRequested?.Invoke(song,action);lastPreviewClick=now; }
                    }
                    else if(inside)ActionRequested?.Invoke(song,action);
                    e.Handled=true; };
                image.LostMouseCapture+=(_,_)=> { Scale(1);image.Source=Bitmap(data.File); };
            }
            if(song.LocalState is >=1 and <=2)Icon("preview_dialog_button",null,"preview",5);
            else Icon("icon_online_bg",null,"online",5);
            var isCollected=collected?.Contains(song.Id)==true;
            Icon(isCollected?"button_add_song_item_collected_normal":"button_add_song_item_collect",
                isCollected?"button_add_song_item_collected_select":"button_add_song_item_collect_selected","collect",5);
            Icon("ic_top_song",null,"top",10);
            var singer=OriginalSingerText.Create(song.Singer,16,name=>SingerRequested?.Invoke(name),"song-singers:"+song.Id);
            singer.TextWrapping=TextWrapping.NoWrap;singer.Margin=new(10,0,actionWidth,0);
            bar.Children.Add(singer);bar.Children.Add(actions);
            tile.MouseLeftButtonUp+=(_,e)=> { ActionRequested?.Invoke(song,"order");e.Handled=true; };
        }
        return new ScrollViewer { Width=738,Height=440,Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false,ClipToBounds=true,
            VerticalContentAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Left };
    }

    private BitmapImage Bitmap(string file)=>new(new Uri(Path.Combine(root,file)));
    private static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
    private static void Put(Canvas canvas,UIElement child,double x,double y)
    { Canvas.SetLeft(child,x);Canvas.SetTop(child,y);canvas.Children.Add(child); }
}

// StrokeTextView draws a 4px white stroke, then a bold fill, with centered
// wrapping and a one-pixel font reduction until the 109px height fits.
public sealed class OutlinedSongName(string name,bool queued) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawing)
    {
        var size=32d;
        FormattedText Build()=>new(name,CultureInfo.GetCultureInfo("vi-VN"),FlowDirection.LeftToRight,
            new Typeface(OriginalFont.Family,FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),
            size,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth=228,TextAlignment=TextAlignment.Center };
        var text=Build();
        while(text.Height>109 && size>1) { size--;text=Build(); }
        var geometry=text.BuildGeometry(new Point(10,(109-text.Height)/2));
        drawing.DrawGeometry(null,new Pen(Brushes.White,4),geometry);
        drawing.DrawGeometry(new SolidColorBrush((Color)ColorConverter.ConvertFromString(queued?"#ffff9d02":"#ff74c044")),null,geometry);
    }
    protected override Size MeasureOverride(Size availableSize)=>new(248,109);
}
