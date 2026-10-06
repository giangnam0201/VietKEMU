using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeDefaultVolumeVerification
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    internal static async Task Run(Window host,NativePlayback playback,string root,string directory,string output)
    {
        var previous=host.Content;var liveVolume=playback.Decoder.OutputVolumeStep;
        var state=Path.Combine(directory,"default-volume-state");var settings=new OriginalDefaultVolumeSettings(state);
        var panel=new Canvas { Width=1280,Height=800,Background=Brushes.Black };host.Content=new Viewbox { Child=panel };host.UpdateLayout();
        try
        {
            var dialog=new OriginalDefaultVolumeDialog(panel,settings);host.UpdateLayout();
            var hint=Descendants<TextBlock>(dialog.Overlay).Single(element=>Equals(element.Tag,"default-volume:hint"));
            var measuredHint=new TextBlock { Text=hint.Text,FontFamily=hint.FontFamily,FontSize=hint.FontSize,LineHeight=hint.LineHeight,Padding=hint.Padding,TextWrapping=hint.TextWrapping };
            measuredHint.Measure(new Size(hint.ActualWidth,double.PositiveInfinity));
            Require(measuredHint.DesiredSize.Height<=hint.ActualHeight,"Original default-volume hint was clipped");
            await Click(dialog,"volume_increase");Require(dialog.Pending==16,"Default volume increase did not stage one step");
            await Click(dialog,"volume_decrease");Require(dialog.Pending==15,"Default volume decrease did not restore one step");
            dialog.Change(100);Require(dialog.Pending==20,"Default volume exceeded 20");dialog.Change(-100);Require(dialog.Pending==0,"Default volume dropped below zero");dialog.Change(15);
            await Position(dialog.SeekBar,29+6/20d*(256-59));
            dialog.SeekBar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonDownEvent });
            Require(dialog.SeekBar.Progress==6&&dialog.Pending==15,"Slider applied staged volume before drag release");
            dialog.SeekBar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            Require(dialog.Pending==6&&settings.Volume==15&&playback.Decoder.OutputVolumeStep==liveVolume,"Slider changed saved or live volume before confirmation");
            host.UpdateLayout();var frame=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);frame.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using(var file=File.Create(Path.Combine(output,"synthetic-default-volume-dialog.png")))encoder.Save(file);
            await Click(dialog,"cancel");Require(settings.Volume==15&&!panel.Children.Contains(dialog.Overlay),"Cancel saved the staged default or left its overlay open");
            dialog=new OriginalDefaultVolumeDialog(panel,settings);dialog.Change(-9);await Click(dialog,"confirm");
            Require(settings.Volume==6&&new OriginalDefaultVolumeSettings(state).Volume==6&&playback.Decoder.OutputVolumeStep==liveVolume,"Confirm failed persistence or changed live volume");
            dialog=new OriginalDefaultVolumeDialog(panel,settings);dialog.Change(1);
            dialog.Overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonDownEvent });
            Require(settings.Volume==6&&!panel.Children.Contains(dialog.Overlay),"Outside dismissal changed saved default");
            // Legacy live-volume state must not override the separately configured
            // default at startup; it may still contain the last playback level.
            File.WriteAllText(Path.Combine(state,"playback-state.json"),"{\"Volume\":2,\"IdleVideoPath\":\"\"}");
            var contract=JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root,"bottom.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            using(var restarted=new NativePlayback(new BottomBar(root,contract),state))
                Require(restarted.Decoder.OutputVolumeStep==6,"Startup restored legacy live volume instead of configured default");
            File.WriteAllText(Path.Combine(output,"default-volume-verification.json"),JsonSerializer.Serialize(new {
                originalDefault15=true,fullHintFits=true,stagedButtons=true,bounds0To20=true,sliderStagesOnRelease=true,cancelPreservesDefault=true,
                confirmPersistsDefault=true,outsideDismissPreservesDefault=true,liveVolumeUnaffected=true,startupUsesDefault=true,
                originalArtworkPresent=File.Exists(Path.Combine(OriginalSupplement.Root,"ambience","volume","volume_seekbar_thumb.png")),
                completeOriginalSettingsScreen=false,roomPluginLifecycleTested=false
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally { host.Content=previous; }
        async Task Click(OriginalDefaultVolumeDialog dialog,string tag)
        {
            var element=Descendants<FrameworkElement>(dialog.Overlay).Single(element=>Equals(element.Tag,"default-volume:"+tag));
            await Position(element,element.ActualWidth/2);
            element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
        }
        async Task Position(FrameworkElement element,double x)
        {
            host.Activate();host.UpdateLayout();var point=element.PointToScreen(new Point(x,element.ActualHeight/2));
            Require(SystemParameters.WorkArea.Contains(point)&&SetCursorPos((int)point.X,(int)point.Y),"Volume control outside desktop");await Task.Delay(70);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    { for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++) { var child=VisualTreeHelper.GetChild(parent,index);if(child is T value)yield return value;foreach(var item in Descendants<T>(child))yield return item; } }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
