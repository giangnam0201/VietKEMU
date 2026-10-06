using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeSongGridAnimationVerification
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    internal static async Task Run(Window host,string root,string output)
    {
        var previous=host.Content;
        try
        {
            var contract=JsonSerializer.Deserialize<SongGridContract>(File.ReadAllText(Path.Combine(root,"song-grid.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var songs=Enumerable.Range(1,21).Select(index=>new CatalogueSong(70000000+index,"Order fixture "+index,"OF",2,"Fixture singer",8,0,1,"") { LocalState=1 }).ToArray();
            var panel=new Canvas { Width=1280,Height=800,ClipToBounds=true,Background=Brushes.Black };
            var actions=new List<(int Id,string Action)>();var grid=new SongGrid(root,contract) { OrderAnimationHost=panel };
            grid.ActionRequested+=(song,action)=>actions.Add((song.Id,action));
            var scroll=grid.Create(songs);Canvas.SetLeft(scroll,40);Canvas.SetTop(scroll,70);panel.Children.Add(scroll);host.Content=new Viewbox { Child=panel };
            await Until(()=>scroll.IsLoaded&&scroll.ViewportHeight>0,"Order fixture grid never loaded");scroll.ScrollToVerticalOffset(200);
            await Until(()=>scroll.VerticalOffset>100,"Order fixture did not scroll");host.UpdateLayout();
            var body=(Canvas)scroll.Content;var cell=(Canvas)body.Children[6];var tile=(Canvas)cell.Children[0];
            var press=Descendants<Image>(tile).Single(element=>Equals(element.Tag,"top:70000007"));
            press.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent });
            await Task.Delay(60);var pressScale=(ScaleTransform)press.RenderTransform;
            Require(Math.Abs(pressScale.ScaleX-.9)<.001&&Math.Abs(pressScale.ScaleY-.9)<.001,"Grid icon did not reach its original pressed scale");
            press.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount) { RoutedEvent=UIElement.MouseLeaveEvent });
            await Task.Delay(60);
            Require(Math.Abs(pressScale.ScaleX-1)<.001&&Math.Abs(pressScale.ScaleY-1)<.001&&actions.Count==0,"Canceled grid press retained its scale or ordered a song");
            var position=cell.TranslatePoint(new Point(),panel);await Click(tile);
            var first=panel.Children.OfType<Canvas>().Single(element=>Equals(element.Tag,"song-grid-order-animation:70000007"));
            Require(first.Width==246&&first.Height==147&&!first.IsHitTestVisible&&first.RenderTransform is TransformGroup,"Order did not inflate an independent original grid item");
            var transform=(TransformGroup)first.RenderTransform;var move=transform.Children.OfType<TranslateTransform>().Single();var scale=transform.Children.OfType<ScaleTransform>().Single();
            Require(Math.Abs(move.X-(position.X-30))<80&&Math.Abs(move.Y-(position.Y-56))<100,"Order animation ignored its scrolled source coordinates");
            var initial=move.X;await Task.Delay(80);
            Require(move.X>initial&&scale.ScaleX<1&&scale.ScaleY<1&&first.Opacity<1,"Order animation did not move, shrink and fade");
            var top=Descendants<Image>(tile).Single(element=>Equals(element.Tag,"top:70000007"));await Click(top);
            Require(actions.SequenceEqual(new[]{(70000007,"order"),(70000007,"top")}),"Order/priority animation duplicated or changed admission callbacks");
            Require(panel.Children.OfType<Canvas>().Count(element=>Equals(element.Tag,"song-grid-order-animation:70000007"))==2,"Concurrent order feedback replaced its previous animation");
            Require(ReferenceEquals(body.Children[6],cell)&&tile.Children.OfType<OutlinedSongName>().Single().IsConfirmedQueued==false,"Order moved the original source or simulated confirmed queue membership");
            await Until(()=>!panel.Children.OfType<Canvas>().Any(element=>element.Tag is string tag&&tag.StartsWith("song-grid-order-animation:")),"Completed order animations were not removed");
            var count=actions.Count;await Click(Descendants<Image>(tile).Single(element=>Equals(element.Tag,"collect:70000007")));
            Require(actions.Count==count+1&&actions[^1].Action=="collect"&&!panel.Children.OfType<Canvas>().Any(element=>element.Tag is string tag&&tag.StartsWith("song-grid-order-animation:")),"Favorite action unexpectedly played an order animation");
            File.WriteAllText(Path.Combine(output,"song-grid-order-animation-verification.json"),JsonSerializer.Serialize(new {
                originalGridItemClone=true,scrolledSourceCoordinates=true,movementScaleAndFade=true,orderAndPriorityCallbacks=true,
                concurrentFeedback=true,sourcePreserved=true,completionCleanup=true,favoriteDoesNotAnimateOrder=true,queueConfirmationNotSimulated=true,
                originalIconPressedScale=true,canceledIconPressRestoresScale=true
            },new JsonSerializerOptions { WriteIndented=true }));
            async Task Click(FrameworkElement element)
            {
                host.Activate();host.UpdateLayout();var point=element.PointToScreen(new Point(element.ActualWidth/2,element.ActualHeight/2));
                Require(SystemParameters.WorkArea.Contains(point)&&SetCursorPos((int)point.X,(int)point.Y),"Order fixture was outside the desktop");await Task.Delay(70);
                element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            }
        }
        finally { host.Content=previous; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    { for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++) { var child=VisualTreeHelper.GetChild(parent,index);if(child is T value)yield return value;foreach(var item in Descendants<T>(child))yield return item; } }
    private static async Task Until(Func<bool> condition,string message)
    { var until=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(25);Require(condition(),message); }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
}
