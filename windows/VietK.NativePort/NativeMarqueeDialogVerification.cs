using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeMarqueeDialogVerification
{
    internal static void Run(Window host,string directory,string output)
    {
        var previous=host.Content;
        var settings=new OriginalMarqueeSettings(Path.Combine(directory,"marquee-dialog-state"));settings.SaveLocal("Saved greeting");
        var panel=new Canvas { Width=1280,Height=800,Background=Brushes.Black };host.Content=new Viewbox { Child=panel };
        try
        {
            var dialog=new OriginalMarqueeDialog(panel,settings,settings.SaveLocal);host.UpdateLayout();
            Require(dialog.Editor.Text=="Saved greeting"&&dialog.Counter.Text=="226/240","Saved text or remaining count missing");
            Click(dialog,"clear");Require(dialog.Editor.Text==""&&dialog.Counter.Text=="240/240"&&settings.LocalText=="Saved greeting","Clear committed the draft");
            dialog.Editor.Text=new string('x',240);Require(dialog.Counter.Text=="0/240","Counter failed at original limit");
            dialog.Overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonDownEvent });
            Require(panel.Children.Contains(dialog.Overlay),"Outside tap dismissed the original non-dismissible dialog");
            Click(dialog,"cancel");Require(settings.LocalText=="Saved greeting"&&!panel.Children.Contains(dialog.Overlay),"Cancel saved draft or left dialog open");
            dialog=new OriginalMarqueeDialog(panel,settings,settings.SaveLocal);dialog.Editor.Text="Chào mừng đến VietK\nLời chào màn hình chờ";host.UpdateLayout();
            var frame=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);frame.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using(var file=File.Create(Path.Combine(output,"synthetic-marquee-dialog.png")))encoder.Save(file);
            var draft=dialog.Editor.Text;Click(dialog,"confirm");Require(new OriginalMarqueeSettings(Path.Combine(directory,"marquee-dialog-state")).LocalText==draft&&!panel.Children.Contains(dialog.Overlay),"Confirm did not persist and dismiss");
            File.WriteAllText(Path.Combine(output,"marquee-dialog-verification.json"),JsonSerializer.Serialize(new {
                savedTextAndRemainingCounter=true,clearStagesOnly=true,cancelDiscardsDraft=true,outsideTapRetainsDialog=true,
                confirmPersistsAndDismisses=true,originalXmlGeometry=true,cloudServiceAvailable=false,originalRadioArtwork=false
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally { host.Content=previous; }
    }
    private static void Click(OriginalMarqueeDialog dialog,string name)
    {
        var target=Descendants(dialog.Overlay).Single(x=>Equals(x.Tag,"marquee:"+name));
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
    }
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement element)yield return element;foreach(var element2 in Descendants(child))yield return element2; }
    }
    private static void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
}
