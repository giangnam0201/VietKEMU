using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

internal static class NativeTvQrDialogVerification
{
    internal static void Run(Window host,string directory,string output)
    {
        var previous=host.Content;
        var state=Path.Combine(directory,"qr-dialog-state");var qr=new TelevisionQr(state);
        var panel=new Canvas { Width=1280,Height=800,Background=Brushes.Black };host.Content=new Viewbox { Child=panel };
        try
        {
            var dialog=new TvQrModeDialog(panel,qr);host.UpdateLayout();
            Require(Marker(dialog,0).Visibility==Visibility.Visible&&Marker(dialog,1).Visibility==Visibility.Hidden,"Initial QR row marker did not follow saved mode");
            Click(dialog,"option:2");Require(dialog.Pending==2&&qr.State.Mode==0&&Marker(dialog,2).Visibility==Visibility.Visible&&Marker(dialog,0).Visibility==Visibility.Hidden,"QR row selection was not staged or markers stale");
            Click(dialog,"close");Require(qr.State.Mode==0&&!panel.Children.Contains(dialog.Overlay),"Close committed the QR draft");
            dialog=new TvQrModeDialog(panel,qr);Click(dialog,"option:1");host.UpdateLayout();
            var frame=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);frame.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using(var file=File.Create(Path.Combine(output,"synthetic-tv-qr-mode-dialog.png")))encoder.Save(file);
            Click(dialog,"confirm");Require(qr.State.Mode==1&&!qr.State.ImageVisible&&!panel.Children.Contains(dialog.Overlay),"Confirmation failed original mode-one branch or dismissal");
            Require(JsonSerializer.Deserialize<int>(File.ReadAllText(Path.Combine(state,"tv-qr-mode.json")))==1,"QR confirmation did not persist");
            dialog=new TvQrModeDialog(panel,qr);Click(dialog,"option:0");
            dialog.Overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonDownEvent });
            Require(qr.State.Mode==1&&!panel.Children.Contains(dialog.Overlay),"Outside dismissal saved QR draft");
            File.WriteAllText(Path.Combine(output,"tv-qr-dialog-verification.json"),JsonSerializer.Serialize(new {
                savedSelectionMarker=true,rowSelectionStagesOnly=true,markerFollowsSelection=true,closeDiscardsDraft=true,
                confirmPersistsAndDismisses=true,originalModeOneVisibilityPreserved=true,outsideDismissDiscardsDraft=true,
                originalSelectedArtworkPresent=File.Exists(Path.Combine(OriginalSupplement.Root,"ambience","settings","setting_general_language_selected.png"))
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally { host.Content=previous; }
    }
    private static FrameworkElement Marker(TvQrModeDialog dialog,int mode)=>Descendants(dialog.Overlay).Single(x=>Equals(x.Tag,"qr-mode:selected:"+mode));
    private static void Click(TvQrModeDialog dialog,string name)
    {
        var target=Descendants(dialog.Overlay).Single(x=>Equals(x.Tag,"qr-mode:"+name));
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
    }
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement element)yield return element;foreach(var next in Descendants(child))yield return next; }
    }
    private static void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
}
