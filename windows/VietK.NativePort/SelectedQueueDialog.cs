using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// PlayListDialog / SelectedListTabView / play_selected_list_item. This overlay
// stays inside the panel, leaving the independent TV window untouched.
public sealed class SelectedQueueDialog
{
    private readonly string resources;
    private readonly Action<YouTubeVideo> remove,top;
    private readonly Action clear,shuffle,retry;
    private readonly Canvas panel;
    public Canvas Overlay { get; }=new() { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    public StackPanel Rows { get; }=new();
    public SelectedQueueDialog(Canvas panel,Action<YouTubeVideo> remove,Action<YouTubeVideo> top,Action clear,Action shuffle,Action retry,string? resources=null)
    {
        this.panel=panel;this.remove=remove;this.top=top;this.clear=clear;this.shuffle=shuffle;this.retry=retry;
        this.resources=resources??Path.Combine(OriginalSupplement.Root,"ambience","playlist");
        var content=new Canvas { Width=563,Height=596,ClipToBounds=true };
        var border=new Border { Width=563,Height=596,CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Color.FromRgb(0x48,0x17,0x40)),Child=content };
        Put(Overlay,border,1280-563,84);Overlay.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource==Overlay)Close(); };
        var title=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        var selected=Icon("play_list_select_light",40,40);selected.Margin=new Thickness(0,0,5,0);title.Children.Add(selected);
        title.Children.Add(Text("Đã đặt bài",20));Put(content,new Border { Width=281.5,Height=62,Child=title,
            Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,0);
        var toolbar=new Grid { Width=585,Height=65 };toolbar.ColumnDefinitions.Add(new());toolbar.ColumnDefinitions.Add(new());
        var clearControl=ActionIcon("selected_list_clear_all","Xóa tất cả",()=>ConfirmClear());toolbar.Children.Add(clearControl);
        var shuffleControl=ActionIcon("selected_list_shuffle","Xáo Trộn",shuffle);Grid.SetColumn(shuffleControl,1);toolbar.Children.Add(shuffleControl);
        Put(content,toolbar,0,62);
        Put(content,new ScrollViewer { Width=585,Height=449,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=Rows },0,130);
        panel.Children.Add(Overlay);
    }
    public void Refresh(IReadOnlyList<YouTubeVideo> queue,bool playing)
    {
        Rows.Children.Clear();
        for(var index=0;index<queue.Count;index++)
        {
            var position=index;var video=queue[index];var row=new Canvas { Width=585,Height=65,Background=Brushes.Transparent };
            if(index==0 && playing)Put(row,Icon("selected_song_playing",22,15),30,25);
            else
            {
                var number=Text((playing?index:index+1).ToString("D2"),18);
                number.Foreground=new SolidColorBrush(Color.FromArgb(51,255,255,255));Put(row,number,30,21);
            }
            var name=Text(video.Title,index==0 && playing?20:18);name.MaxWidth=250;name.TextTrimming=TextTrimming.CharacterEllipsis;
            if(index==0 && playing)name.Foreground=new SolidColorBrush(Color.FromRgb(255,207,17));
            name.Measure(new Size(250,65));Put(row,name,70,(65-name.DesiredSize.Height)/2);
            Put(row,Icon("icon_youtube",33,30),70+name.DesiredSize.Width+15,17.5);
            if(index>1)Put(row,ClickIcon("ic_top_song",()=>top(video)),448,0);
            if(index==0)Put(row,ClickIcon("ic_cut_song",()=>remove(video)),448,0);
            Put(row,ClickIcon("ic_delete",()=>remove(video)),499,0);
            var retryHit=new Border { Width=440,Height=65,Background=Brushes.Transparent };
            retryHit.MouseLeftButtonUp+=(_,e)=> { if(position==0 && !playing)retry();e.Handled=true; };Put(row,retryHit,0,0);
            Rows.Children.Add(row);
        }
    }
    private void ConfirmClear()
    {
        if(Rows.Children.Count==0)return;
        var confirmation=new Canvas { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
        var area=new StackPanel { Width=506,Margin=new Thickness(0,40,0,50) };
        var prompt=Text("Bạn có cần phải làm trống danh sách bài hát?",24);prompt.TextWrapping=TextWrapping.Wrap;
        prompt.TextAlignment=TextAlignment.Center;prompt.Margin=new Thickness(20,0,20,45);area.Children.Add(prompt);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center };
        actions.Children.Add(ConfirmButton("Hủy",false,()=>panel.Children.Remove(confirmation)));
        var ok=ConfirmButton("Xác nhận",true,()=> { clear();panel.Children.Remove(confirmation); });ok.Margin=new Thickness(20,0,0,0);actions.Children.Add(ok);
        area.Children.Add(actions);
        var background=new Border { Width=506,CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Color.FromRgb(0x48,0x17,0x40)),Child=area };
        background.Measure(new Size(506,800));Put(confirmation,background,387,(800-background.DesiredSize.Height)/2);
        confirmation.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource==confirmation)panel.Children.Remove(confirmation); };
        panel.Children.Add(confirmation);
    }
    private static Border ConfirmButton(string label,bool ok,Action action)
    {
        var gradient=new LinearGradientBrush { StartPoint=new Point(0,1),EndPoint=new Point(0,0) };
        gradient.GradientStops.Add(new GradientStop(ok?Color.FromRgb(4,160,227):Color.FromRgb(216,216,254),0));
        if(!ok)gradient.GradientStops.Add(new GradientStop(Color.FromRgb(236,237,242),.5));
        gradient.GradientStops.Add(new GradientStop(ok?Color.FromRgb(0,250,246):Colors.White,1));
        var text=Text(label,20);text.TextAlignment=TextAlignment.Center;text.Foreground=ok?Brushes.White:Brushes.Black;
        var button=new Border { Width=140,Height=46,CornerRadius=new CornerRadius(26),Background=gradient,Child=text };
        button.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };return button;
    }
    public void Close()=>panel.Children.Remove(Overlay);
    private FrameworkElement ActionIcon(string icon,string label,Action action)
    {
        var area=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Background=Brushes.Transparent };
        var image=Icon(icon,36,36);image.Margin=new Thickness(0,0,5,0);area.Children.Add(image);area.Children.Add(Text(label,20));
        area.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };return area;
    }
    private FrameworkElement ClickIcon(string icon,Action action)
    {
        var target=new Border { Width=51,Height=65,Padding=new Thickness(icon=="ic_top_song"?0:12,0,0,0),Background=Brushes.Transparent,Child=Icon(icon,34,34),Tag=icon };
        target.MouseLeftButtonDown+=(_,e)=> { target.Child=Icon(icon+"_press",icon=="ic_top_song"?34:33,icon=="ic_top_song"?34:33);e.Handled=true; };
        target.MouseLeave+=(_,_)=>target.Child=Icon(icon,34,34);
        target.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };return target;
    }
    private Image Icon(string name,double width,double height)
    {
        var path=Path.Combine(resources,name+".png");return new Image { Width=width,Height=height,Stretch=Stretch.Uniform,
            Source=File.Exists(path)?new BitmapImage(new Uri(Path.GetFullPath(path))):null };
    }
    private static TextBlock Text(string text,double size)=>new() { Text=text,FontSize=size,Foreground=Brushes.White,FontFamily=OriginalFont.Family,
        VerticalAlignment=VerticalAlignment.Center };
    private static void Put(Canvas canvas,UIElement child,double x,double y)
    { Canvas.SetLeft(child,x);Canvas.SetTop(child,y);canvas.Children.Add(child); }
}
