using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// BroadcastListSettingDialog / AddBroadcastListDialog: local draft editing.
public sealed class OriginalBroadcastPlaylistDialog
{
    public Canvas Overlay { get; }=BroadcastDialogUi.Overlay();
    internal IReadOnlyList<LocalSong> Draft=>songs;
    internal OriginalBroadcastAddDialog? AddDialog { get; private set; }
    private readonly Canvas host,content=new() { Width=680,Height=538 };
    private readonly NativePlayback playback;
    private readonly Func<int,LocalSong?> lookup;
    private readonly Func<string,int,IReadOnlyList<LocalSong>> search;
    private readonly Func<int,bool> isLocal;
    private readonly Action<int> order;
    private readonly List<LocalSong> songs=[];
    private readonly ScrollViewer list=new() { Width=620,Height=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    private readonly StackPanel rows=new();
    private readonly Canvas headers=new() { Width=620,Height=50 };
    private readonly Border hint=new() { Width=620,Height=280,Background=BroadcastDialogUi.EditorBrush,CornerRadius=new(10) };
    private readonly RadioButton local,usb;
    private readonly FrameworkElement add;
    private bool usbMode,closed;
    public OriginalBroadcastPlaylistDialog(Canvas host,NativePlayback playback,Func<int,LocalSong?> lookup,
        Func<string,int,IReadOnlyList<LocalSong>> search,Func<int,bool> isLocal,Action<int> order,bool allowRemote=true)
    {
        this.host=host;this.playback=playback;this.lookup=lookup;this.search=search;this.isLocal=isLocal;this.order=order;
        songs.AddRange(playback.IdlePlaylist.Entries.Select(e=>lookup(e.SongId)).OfType<LocalSong>().Where(s=>allowRemote||s.LocalFlag>0));
        BroadcastDialogUi.Body(Overlay,content,300,131);
        BroadcastDialogUi.Put(content,BroadcastDialogUi.Label("Video màn hình chờ",680,60,24,true),0,0);
        BroadcastDialogUi.Put(content,new Border { Width=680,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,60);
        // XML is wrap_content with minWidth=80, not a fixed 80-pixel label.
        add=BroadcastDialogUi.Action("＋ Tạo mới",104,60,18,OpenAdd,"broadcast:add");BroadcastDialogUi.Put(content,add,546,0);
        BroadcastDialogUi.Put(content,BroadcastDialogUi.Label("Chế độ màn hình chờ",310,40,20),30,70);
        usb=new RadioButton { Content="USB",FontSize=20,FontFamily=OriginalFont.Family,Foreground=Brushes.White,Height=40,VerticalContentAlignment=VerticalAlignment.Center,Tag="broadcast:usb" };
        local=new RadioButton { Content="Đầu máy",FontSize=20,FontFamily=OriginalFont.Family,Foreground=Brushes.White,Height=40,VerticalContentAlignment=VerticalAlignment.Center,Tag="broadcast:local" };
        BroadcastDialogUi.Put(content,usb,450,70);BroadcastDialogUi.Put(content,local,550,70);
        usb.Click+=(_,_)=> { usbMode=true;Refresh(); };
        local.Click+=(_,_)=> { if(usbMode) { songs.Clear();songs.AddRange(playback.IdlePlaylist.Entries.Select(e=>lookup(e.SongId)).OfType<LocalSong>().Where(s=>allowRemote||s.LocalFlag>0)); }usbMode=false;Refresh(); };
        foreach(var (text,x,width) in new[]{("Tên bài hát",10d,185d),("Ca sĩ",195d,137d),("Thể loại",332d,137d),("Hệ điều hành",469d,131d)})
        { var heading=BroadcastDialogUi.Label(text,width,50,18);heading.Foreground=new SolidColorBrush(Color.FromArgb(204,255,255,255));BroadcastDialogUi.Put(headers,heading,x,0); }
        BroadcastDialogUi.Put(content,headers,30,110);
        list.Content=rows;list.Background=BroadcastDialogUi.EditorBrush;BroadcastDialogUi.ScrollStyle(list);BroadcastDialogUi.Put(content,list,30,160);
        var empty=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new(30,0,30,0) };
        var hintArt=Path.Combine(OriginalSupplement.Root,"ambience","settings","dialog_public_play_hint_icon.png");
        empty.Children.Add(File.Exists(hintArt)?new Image { Width=90,Height=120,Source=new BitmapImage(new Uri(Path.GetFullPath(hintArt))),Stretch=Stretch.Fill }:
            BroadcastDialogUi.Label("♫",90,120,80,true));
        empty.Children.Add(new TextBlock { Text="Vui lòng nhấn vào nút Tạo Mới ở góc trên bên phải để thêm bài hát vào màn hình chờ",FontFamily=OriginalFont.Family,FontSize=24,Foreground=new SolidColorBrush(Color.FromArgb(51,255,255,255)),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new(0,10,0,0) });hint.Child=empty;
        BroadcastDialogUi.Put(content,hint,30,120);
        var setup=BroadcastDialogUi.Action("Thiết lập từ USB",250,46,24,ChooseVideo,"broadcast:choose-video",true);BroadcastDialogUi.Put(content,setup,215,260);
        setup.Visibility=Visibility.Collapsed;setupVideo=setup;
        BroadcastDialogUi.Put(content,BroadcastDialogUi.Action("Hủy",140,46,24,Close,"broadcast:cancel",true,true),162.5,470);
        BroadcastDialogUi.Put(content,BroadcastDialogUi.Action("Xác nhận",140,46,24,Confirm,"broadcast:confirm",true),377.5,470);
        Overlay.MouseLeftButtonDown+=(_,e)=> { if(ReferenceEquals(e.OriginalSource,Overlay)) { Close();e.Handled=true; } };
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        usbMode=playback.UsbIdleVideo is not null;Refresh();Panel.SetZIndex(Overlay,1000);host.Children.Add(Overlay);Keyboard.Focus(Overlay);
    }
    private readonly FrameworkElement setupVideo;
    private void Refresh()
    {
        usb.IsChecked=usbMode;local.IsChecked=!usbMode;add.Visibility=usbMode?Visibility.Collapsed:Visibility.Visible;
        rows.Children.Clear();headers.Visibility=list.Visibility=usbMode||songs.Count==0?Visibility.Collapsed:Visibility.Visible;
        hint.Visibility=!usbMode&&songs.Count==0?Visibility.Visible:Visibility.Collapsed;
        setupVideo.Visibility=usbMode?Visibility.Visible:Visibility.Collapsed;
        if(usbMode)return;
        for(var index=0;index<songs.Count;index++)
        {
            var position=index;var song=songs[index];var row=new Canvas { Width=600,Height=60,Background=Brushes.Transparent,Tag="broadcast:row:"+index };
            var title=BroadcastDialogUi.Label(song.Name,isLocal(song.Id)?175:150,60,18);BroadcastDialogUi.Put(row,title,10,0);
            if(!isLocal(song.Id))BroadcastDialogUi.Put(row,BroadcastDialogUi.OnlineBadge(),165,20);
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Label(song.Singer,127,60,18),195,0);
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Label("thư viện nhạc",137,60,18),332,0);
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Icon("ic_top_song",50,60,()=> { var item=songs[position];songs.RemoveAt(position);songs.Insert(0,item);Refresh(); },"broadcast:top:"+index),469,0);
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Icon("ic_delete",50,60,()=> { songs.RemoveAt(position);Refresh(); },"broadcast:delete:"+index),519,0);
            row.MouseLeftButtonUp+=(_,e)=> { if(!isLocal(song.Id))order(song.Id);e.Handled=true; };
            rows.Children.Add(row);
        }
    }
    private void ChooseVideo()
    {
        var file=new Microsoft.Win32.OpenFileDialog { Title="Chọn Demo.mp4",Filter="MP4 video|*.mp4",CheckFileExists=true };
        if(file.ShowDialog()!=true)return;
        playback.SetIdleVideo(file.FileName);Refresh();
    }
    internal void OpenAdd()
    {
        host.Children.Remove(Overlay);
        AddDialog=new(host,search,isLocal,(added,confirmed)=>
        {
            if(confirmed)songs.AddRange(added);usbMode=false;Refresh();host.Children.Add(Overlay);Keyboard.Focus(Overlay);
        },Close);
    }
    public void Confirm()
    {
        if(!usbMode)
        {
            var json=JsonSerializer.Serialize(new { play_list=songs.Select(s=>new { song_id=s.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),type="1" }),is_need_reply="1" });
            foreach(var song in songs)if(!isLocal(song.Id))order(song.Id);
            playback.ImportIdlePlaylist(json,restartIdle:false);
        }
        Close();
    }
    public void Close() { if(closed)return;closed=true;host.Children.Remove(Overlay); }
}

public sealed class OriginalBroadcastAddDialog
{
    public Canvas Overlay { get; }=BroadcastDialogUi.Overlay();
    internal TextBox Input { get; }
    internal IReadOnlyList<LocalSong> Draft=>added;
    private readonly Canvas host,content=new() { Width=680,Height=538 };
    private readonly Func<string,int,IReadOnlyList<LocalSong>> search;
    private readonly Func<int,bool> isLocal;
    private readonly Action<IReadOnlyList<LocalSong>,bool> finish;
    private readonly Action? dismiss;
    private readonly List<LocalSong> added=[];
    private readonly ScrollViewer searchList=new() { Width=620,Height=320,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    private readonly StackPanel results=new();
    private readonly Canvas draftArea=new() { Width=620,Height=290 };
    private readonly StackPanel draftRows=new();
    private readonly FrameworkElement back,confirm;
    private bool closed,loading,more;
    private int page;
    public OriginalBroadcastAddDialog(Canvas host,Func<string,int,IReadOnlyList<LocalSong>> search,Func<int,bool> isLocal,Action<IReadOnlyList<LocalSong>,bool> finish,Action? dismiss=null)
    {
        this.host=host;this.search=search;this.isLocal=isLocal;this.finish=finish;this.dismiss=dismiss;
        BroadcastDialogUi.Body(Overlay,content,300,86);
        // Root XML has 19-pixel top/bottom padding; add dialog is centered -45.
        BroadcastDialogUi.Put(content,BroadcastDialogUi.Label("Video màn hình chờ",680,60,24,true),0,19);
        BroadcastDialogUi.Put(content,new Border { Width=680,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,79);
        BroadcastDialogUi.Put(content,BroadcastDialogUi.Label("Vui lòng nhập tên bài hát",620,40,20),30,79);
        Input=new TextBox { Width=620,Height=45,FontFamily=OriginalFont.Family,FontSize=18,Foreground=Brushes.White,Background=BroadcastDialogUi.EditorBrush,BorderThickness=new(0),Padding=new(10,8,45,0),ContextMenu=null,Tag="broadcast-add:input" };
        BroadcastDialogUi.Put(content,Input,30,119);
        var placeholder=BroadcastDialogUi.Label("Vui lòng nhập tên bài hát vài tìm kiếm",540,45,18);placeholder.Foreground=new SolidColorBrush(Color.FromArgb(204,255,255,255));placeholder.IsHitTestVisible=false;BroadcastDialogUi.Put(content,placeholder,40,119);
        var inputAction=BroadcastDialogUi.Action("⌕",45,45,24,()=>Input.Clear(),"broadcast-add:clear");BroadcastDialogUi.Put(content,inputAction,605,119);
        searchList.Content=results;searchList.Background=BroadcastDialogUi.EditorBrush;BroadcastDialogUi.ScrollStyle(searchList);BroadcastDialogUi.Put(content,searchList,30,164);
        searchList.ScrollChanged+=(_,e)=> { if(!loading&&more&&e.ExtentHeightChange==0&&e.VerticalChange>0&&searchList.VerticalOffset+searchList.ViewportHeight>=searchList.ExtentHeight-1)LoadNext(); };
        BroadcastDialogUi.Put(content,draftArea,30,164);
        BroadcastDialogUi.Put(draftArea,BroadcastDialogUi.Label("Đã chọn",620,40,20),0,0);
        BroadcastDialogUi.Put(draftArea,BroadcastDialogUi.Label("Tên bài hát",259,44,18),10,40);
        BroadcastDialogUi.Put(draftArea,BroadcastDialogUi.Label("Ca sĩ",236,44,18),269,40);
        var operation=BroadcastDialogUi.Label("Hệ điều hành",95,44,18,true);operation.TextWrapping=TextWrapping.Wrap;operation.TextTrimming=TextTrimming.None;operation.Padding=new(0);
        BroadcastDialogUi.Put(draftArea,operation,505,40);
        var draftList=new ScrollViewer { Width=620,Height=200,Background=BroadcastDialogUi.EditorBrush,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=draftRows };
        BroadcastDialogUi.ScrollStyle(draftList);BroadcastDialogUi.Put(draftArea,draftList,0,89);
        back=BroadcastDialogUi.Action("Quay lại",140,46,24,()=>Close(false),"broadcast-add:back",true,true);
        confirm=BroadcastDialogUi.Action("Xác nhận tạo mới",140,46,16,()=>Close(true),"broadcast-add:confirm",true);
        BroadcastDialogUi.Put(content,back,162.5,461);BroadcastDialogUi.Put(content,confirm,377.5,461);
        Input.TextChanged+=(_,_)=> { placeholder.Visibility=Input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;if(inputAction is Border { Child:TextBlock symbol })symbol.Text=Input.Text.Length==0?"⌕":"×";Search(); };
        Input.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Enter) { Search();e.Handled=true; } };
        Input.GotKeyboardFocus+=(_,_)=> { if(Input.Text.Length>0)Search(); };
        Overlay.MouseLeftButtonDown+=(_,e)=> { if(ReferenceEquals(e.OriginalSource,Overlay)) { Close(false,returnToParent:false);e.Handled=true; } };
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close(false,returnToParent:false);e.Handled=true; } };
        ShowDraft();Panel.SetZIndex(Overlay,1000);host.Children.Add(Overlay);Keyboard.Focus(Overlay);
    }
    private void Search()
    {
        if(Input.Text.Length==0) { ShowDraft();return; }
        searchList.Visibility=Visibility.Visible;draftArea.Visibility=back.Visibility=confirm.Visibility=Visibility.Collapsed;
        page=0;results.Children.Clear();more=true;LoadNext();searchList.ScrollToTop();
    }
    private void LoadNext()
    {
        if(loading||!more)return;loading=true;
        try
        {
            var songs=search(Input.Text,page++);more=songs.Count==50;
            foreach(var song in songs)
            {
                var row=new Canvas { Width=600,Height=45,Background=Brushes.Transparent,Tag="broadcast-add:result:"+song.Id };
                var inline=new StackPanel { Orientation=Orientation.Horizontal,Height=45 };
                inline.Children.Add(BroadcastDialogUi.Label(song.Name[..Math.Min(10,song.Name.Length)],double.NaN,45,14,true));
                inline.Children.Add(BroadcastDialogUi.Label(" - ",double.NaN,45,14,true));
                inline.Children.Add(BroadcastDialogUi.Label(song.Singer[..Math.Min(10,song.Singer.Length)],double.NaN,45,14,true));
                if(!isLocal(song.Id)) { var badge=BroadcastDialogUi.OnlineBadge();badge.Margin=new(5,0,0,0);badge.VerticalAlignment=VerticalAlignment.Center;inline.Children.Add(badge); }
                BroadcastDialogUi.Put(row,inline,10,0);
                row.MouseLeftButtonUp+=(_,e)=> { added.Add(song);ShowDraft();e.Handled=true; };results.Children.Add(row);
            }
        }
        finally { loading=false; }
    }
    private void ShowDraft()
    {
        results.Children.Clear();searchList.Visibility=Visibility.Collapsed;draftArea.Visibility=back.Visibility=confirm.Visibility=Visibility.Visible;
        draftRows.Children.Clear();
        for(var index=0;index<added.Count;index++)
        {
            var position=index;var song=added[index];var row=new Canvas { Width=600,Height=50 };
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Label(song.Name,isLocal(song.Id)?239:214,50,18),10,0);
            if(!isLocal(song.Id))BroadcastDialogUi.Put(row,BroadcastDialogUi.OnlineBadge(),224,15);
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Label(song.Singer,226,50,18),269,0);
            BroadcastDialogUi.Put(row,BroadcastDialogUi.Icon("ic_delete",60,50,()=> { added.RemoveAt(position);ShowDraft(); },"broadcast-add:delete:"+index),505,0);
            draftRows.Children.Add(row);
        }
    }
    public void Close(bool commit,bool returnToParent=true) { if(closed)return;closed=true;host.Children.Remove(Overlay);if(returnToParent)finish(added.ToArray(),commit);else dismiss?.Invoke(); }
}

internal static class BroadcastDialogUi
{
    internal static void ScrollStyle(ScrollViewer viewer)
    {
        var template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ScrollBar}">
              <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" ViewportSize="{TemplateBinding ViewportSize}" Value="{Binding Value,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}">
                <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="{x:Type Thumb}"><Border CornerRadius="3" Background="#80FFFFFF" /></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              </Track>
            </ControlTemplate>
            """);
        var style=new Style(typeof(ScrollBar));style.Setters.Add(new Setter(FrameworkElement.WidthProperty,8d));style.Setters.Add(new Setter(Control.TemplateProperty,template));
        viewer.Resources[typeof(ScrollBar)]=style;
    }
    internal static Brush EditorBrush=>new SolidColorBrush(Color.FromArgb(76,0,0,0));
    internal static Canvas Overlay()=>new() { Width=1280,Height=800,Focusable=true,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    internal static void Body(Canvas overlay,Canvas content,double x,double y)=>Put(overlay,new Border { Width=content.Width,Height=content.Height,CornerRadius=new(10),Background=new SolidColorBrush(Color.FromRgb(72,23,64)),Child=content },x,y);
    internal static TextBlock Label(string text,double width,double height,double size,bool centered=false)=>new() {
        Text=text,Width=width,Height=height,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis,
        TextAlignment=centered?TextAlignment.Center:TextAlignment.Left,Padding=new(0,Math.Max(0,(height-size*1.3)/2),0,0) };
    internal static FrameworkElement OnlineBadge()=>new Border { Width=20,Height=20,CornerRadius=new(4),Background=Brushes.DeepSkyBlue,Child=Label("云",20,20,9,true) };
    internal static FrameworkElement Action(string text,double width,double height,double size,Action action,string tag,bool button=false,bool cancel=false)
    {
        var label=Label(text,width,height,size,true);
        if(cancel)label.Foreground=new SolidColorBrush(Color.FromRgb(38,41,100));
        var border=new Border { Width=width,Height=height,Background=button?(cancel?new LinearGradientBrush(Color.FromRgb(216,216,254),Colors.White,90):new LinearGradientBrush(Color.FromRgb(4,160,227),Color.FromRgb(0,250,246),90)):Brushes.Transparent,
            CornerRadius=new(26),Child=label,Tag=tag };
        OriginalPressFeedback.Bind(border,.9);border.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };return border;
    }
    internal static FrameworkElement Icon(string name,double width,double height,Action action,string tag)
    {
        FrameworkElement Art(bool pressed)
        {
            var path=Path.Combine(OriginalSupplement.Root,"ambience","playlist",name+(pressed?"_press":"")+".png");
            return File.Exists(path)?new Image { Width=34,Height=34,Source=new BitmapImage(new Uri(Path.GetFullPath(path))),Stretch=Stretch.Uniform }:
                Label(name=="ic_top_song"?"↑":"×",34,34,28,true);
        }
        var target=new Border { Width=width,Height=height,Background=Brushes.Transparent,Child=Art(false),Tag=tag };
        target.MouseLeftButtonDown+=(_,e)=> { target.Child=Art(true);e.Handled=true; };
        target.MouseLeave+=(_,_)=>target.Child=Art(false);
        target.MouseLeftButtonUp+=(_,e)=> { target.Child=Art(false);action();e.Handled=true; };return target;
    }
    internal static void Put(Canvas parent,UIElement child,double x,double y) { parent.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
