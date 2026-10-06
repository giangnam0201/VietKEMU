using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// Original ItemOnLongClickListener, DragViewManager and SelectedPullListView.
// A Windows mouse hold invokes the original reorder path inside the panel.
internal sealed class SelectedQueueDrag
{
    private readonly Canvas overlay;
    private readonly ScrollViewer list;
    private readonly StackPanel rows;
    private readonly Action<YouTubeVideo,int> move;
    private readonly DispatcherTimer hold=new() { Interval=TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer scroll=new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly Image marker;
    private OriginalQueueDragPosition position=new();
    private Canvas? heldRow;
    private YouTubeVideo? video;
    private int source,scrollDirection;
    private Point pressed;
    private double pointerY;
    public Border? Ghost { get; private set; }
    public bool IsDragging=>Ghost is not null;
    internal int Target=>position.Target;
    internal double MarkerTop=>Canvas.GetTop(marker);
    internal double ScrollOffset=>list.VerticalOffset;

    public SelectedQueueDrag(Canvas overlay,ScrollViewer list,StackPanel rows,Action<YouTubeVideo,int> move,string resources)
    {
        this.overlay=overlay;this.list=list;this.rows=rows;this.move=move;
        overlay.Focusable=true;
        var path=Path.Combine(resources,"selected_order_into.png");
        var image=File.Exists(path)?new BitmapImage(new Uri(Path.GetFullPath(path))):null;
        marker=new Image { Width=image?.PixelWidth??514,Height=image?.PixelHeight??12,Source=image,IsHitTestVisible=false,Visibility=Visibility.Hidden };
        hold.Tick+=(_,_)=> { hold.Stop();if(Mouse.LeftButton==MouseButtonState.Pressed && heldRow is not null && video is not null)Begin(heldRow,video,source,true);else Cancel(); };
        scroll.Tick+=(_,_)=>ScrollAtEdge();
        overlay.MouseMove+=(_,e)=>
        {
            if(IsDragging) { Update(e.GetPosition(list).Y);e.Handled=true; }
            else if(hold.IsEnabled && (e.GetPosition(overlay)-pressed).Length>8)Cancel();
        };
        overlay.MouseLeftButtonUp+=(_,e)=> { if(IsDragging) { Finish(true);e.Handled=true; }else Cancel(); };
        overlay.LostMouseCapture+=(_,_)=> { if(IsDragging)Cancel(); };
        overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape && IsDragging) { Cancel();e.Handled=true; } };
        overlay.Unloaded+=(_,_)=>Cancel();
    }
    public void Attach(Border target,Canvas row,YouTubeVideo item,int index)
    {
        if(index==0)return;
        row.Tag=item;
        target.MouseLeftButtonDown+=(_,e)=>
        {
            Cancel();heldRow=row;video=item;source=index;pressed=e.GetPosition(overlay);hold.Start();e.Handled=true;
        };
        target.MouseLeave+=(_,_)=> { if(!IsDragging)Cancel(); };
        target.AddHandler(UIElement.MouseLeftButtonUpEvent,new MouseButtonEventHandler((_,_)=> { if(!IsDragging)Cancel(); }),true);
    }
    private bool Begin(Canvas row,YouTubeVideo item,int index,bool capture)
    {
        hold.Stop();if(index<1 || index>=rows.Children.Count || row.Parent!=rows)return false;
        if(capture && !overlay.CaptureMouse()) { Cancel();return false; }
        if(capture)overlay.Focus();
        heldRow=row;video=item;source=index;position=new();
        Ghost=new Border { Width=643,Height=66,Opacity=.8,IsHitTestVisible=false,
            Background=new SolidColorBrush(Color.FromArgb(242,72,74,77)),
            Child=new Border { Width=585,Height=65,HorizontalAlignment=HorizontalAlignment.Left,Background=new VisualBrush(row) { Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top } } };
        var location=row.TranslatePoint(new Point(),overlay);
        Canvas.SetLeft(Ghost,location.X);Canvas.SetTop(Ghost,location.Y);overlay.Children.Add(Ghost);overlay.Children.Add(marker);
        scroll.Start();return true;
    }
    internal bool BeginForVerification(int index)
    {
        if(index<0 || index>=rows.Children.Count || rows.Children[index] is not Canvas row || row.Tag is not YouTubeVideo item)return false;
        rows.UpdateLayout();
        return Begin(row,item,index,false);
    }
    private void ScrollAtEdge()
    {
        if(!IsDragging || scrollDirection==0)return;
        list.ScrollToVerticalOffset(list.VerticalOffset+20*scrollDirection);list.UpdateLayout();Update(pointerY);
    }
    internal void ScrollForVerification()=>ScrollAtEdge();
    internal void Update(double viewportY)
    {
        if(Ghost is null)return;
        pointerY=viewportY;
        var height=list.ActualHeight>0?list.ActualHeight:list.Height;
        scrollDirection=viewportY<=0?-1:viewportY>=height?1:0;
        var clamped=Math.Clamp(viewportY,0,height);
        var first=(int)(list.VerticalOffset/65);var firstTop=-(int)(list.VerticalOffset-first*65);
        position.Update((int)clamped,first,firstTop,rows.Children.Count);
        var origin=list.TranslatePoint(new Point(),overlay);
        Canvas.SetTop(Ghost,origin.Y+clamped);
        if(position.HasMarker)
        {
            Canvas.SetLeft(marker,origin.X+25);Canvas.SetTop(marker,origin.Y+position.MarkerY);marker.Visibility=Visibility.Visible;
        }
    }
    private void Finish(bool commit)
    {
        var item=video;var target=position.Target;var hadMarker=position.HasMarker;
        Cancel();if(commit && hadMarker && item is not null)move(item,target);
    }
    public void Cancel()
    {
        hold.Stop();scroll.Stop();heldRow=null;video=null;scrollDirection=0;
        if(Ghost is not null)overlay.Children.Remove(Ghost);Ghost=null;
        overlay.Children.Remove(marker);marker.Visibility=Visibility.Hidden;
        if(Mouse.Captured==overlay)overlay.ReleaseMouseCapture();
    }
}
