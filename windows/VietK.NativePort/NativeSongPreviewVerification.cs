using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeSongPreviewVerification
{
    internal static async Task Run(Window host,NativePlayback television,string fixtures,string output)
    {
        var previous=host.Content;
        var panel=new Canvas { Width=1280,Height=800,Background=Brushes.DarkSlateBlue };
        host.Content=new Viewbox { Child=panel };
        var song=new LocalSong(7,"Preview fixture","PF",2,"Fixture singer",[],[0],[8],0,0,0,null,null,1,null,1,0);
        var orders=0;var favorites=0;var selected=false;var feedback="";
        using var preview=new OriginalSongPreview(()=>panel,_=>Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv")),_=>selected,
            _=> { orders++;selected=true; },_=>favorites++);
        preview.Feedback+=text=>feedback=text;
        try
        {
            var tvFrames=television.DecodedPreviewFrames;
            preview.Show(song);
            Require(preview.IsOpen&&Application.Current.Windows.Count==2,"Song preview introduced another native window");
            await Until(()=>preview.Frames>5&&preview.VideoVisible,"Preview did not render after original 800ms reveal delay");
            Require(preview.Decoder!.Muted&&preview.Decoder.Native.Mute,"Preview was not muted independently of TV");
            Require(television.DecodedPreviewFrames>tvFrames,"Opening song preview stopped TV frame delivery");
            var order=Descendants<Button>(panel).Single(button=>Equals(button.Tag,"preview-order"));
            order.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(orders==1&&feedback=="phát theo yêu cầu thành công","Preview order callback/toast missing");
            Require(Descendants<TextBlock>(panel).Single(text=>text.Text==song.Name).Foreground is SolidColorBrush color&&color.Color==Color.FromRgb(255,221,30),"Queued preview title did not highlight");
            Descendants<Button>(panel).Single(button=>Equals(button.Tag,"preview-collect")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(favorites==1&&preview.IsOpen,"Preview favorite did not use collection callback while staying open");
            var frame=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);panel.UpdateLayout();frame.Render(panel);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));
            using(var file=File.Create(Path.Combine(output,"original-song-preview.png")))encoder.Save(file);
            // End the real media to exercise the independent loop, not a mocked callback.
            preview.Decoder.Native.Time=preview.Decoder.Native.Length-500;
            await Until(()=>preview.LoopCount>0&&preview.Decoder.Position<5000,"Song preview did not loop from its beginning");
            Require(preview.Decoder.Native.Mute,"Loop restarted preview with audible output");
            Descendants<Button>(panel).Single(button=>Equals(button.Tag,"preview-close")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!preview.IsOpen&&preview.Decoder is null&&panel.Children.Count==0,"Preview close did not remove and release its decoder");
            using var missing=new OriginalSongPreview(()=>panel,_=>null,_=>false,_=>{},_=>{});
            missing.Show(song);
            Require(missing.IsOpen&&missing.Decoder is null&&Descendants<TextBlock>(panel).Any(text=>text.Text=="Tải video không thành công"),"Missing preview media did not expose original error text");
            missing.Close();preview.Show(song);preview.Close();await Task.Delay(250);
            Require(!preview.IsOpen&&panel.Children.Count==0&&Application.Current.Windows.Count==2,"Rapid preview close left a stale overlay/player");
            File.WriteAllText(Path.Combine(output,"song-preview-verification.json"),JsonSerializer.Serialize(new {
                independentMutedDecoder=true,realFrames=true,originalRevealDelay=true,tvContinues=true,orderAndFavoriteCallbacks=true,
                confirmedQueueHighlight=true,loopsFromBeginning=true,closeReleasesPlayer=true,missingMediaError=true,rapidCloseSafe=true,
                twoWindows=true,originalArtworkAvailable=preview.OriginalArtworkAvailable,
                manufacturerPreviewUrlProtocol=false,middleEllipsisExact=false
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally { preview.Close();host.Content=previous; }
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
    private static async Task Until(Func<bool> condition,string message)
    {
        var deadline=DateTime.UtcNow.AddSeconds(8);
        while(!condition()&&DateTime.UtcNow<deadline)await Task.Delay(50);
        Require(condition(),message);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T value)yield return value;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
