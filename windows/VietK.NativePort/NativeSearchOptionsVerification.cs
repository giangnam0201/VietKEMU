using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeSearchOptionsVerification
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    internal static async Task Run(Window host,NativePlayback playback,string root,string directory,string output)
    {
        var previous=host.Content;var previousOverride=playback.CommandOverride;
        try
        {
            T Read<T>(string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root,file)),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var settings=new OriginalSearchSettings(directory);
            using var options=new NativeSearchOptions(settings,()=>host.Content is Viewbox { Child:Canvas panel }?panel:null);
            using var database=new LocalSongDatabase(Path.Combine(root,"local-seed.db"),Path.Combine(directory,"search-settings-state.db"));
            var browser=new SongBrowser(root,Read<SongBrowserContract>("song-browser.json"),Read<MoreContract>("more.json"),database,Read<SongGridContract>("song-grid.json")) { Playback=playback,SearchOptions=options };
            var song=new CatalogueSong(70000001,"Search fixture","SF",2,"Fixture singer",8,0,1,"") { LocalState=1 };
            var panel=browser.CreateVerificationFixture(new[]{song});host.Content=new Viewbox { Child=panel };
            await Until(()=>panel.IsLoaded,"Search fixture did not load");browser.Input!.Letter("F");await Task.Delay(250);
            await Click(Tile());await Task.Delay(600);Require(browser.Input.Text=="F","Default-off option cleared the native search");
            options.SetClearAfterOrder(true);await Click(Tile());await Task.Delay(200);
            Require(browser.Input.Text=="F","Search was cleared before the original delay");browser.Input.Letter("X");
            await Until(()=>browser.Input.Text.Length==0,"Enabled option did not clear edited native input");
            browser.Input.Letter("A");
            await Click(Descendants<Image>((DependencyObject)Descendants<ScrollViewer>(panel).Single().Content).Single(element=>Equals(element.Tag,"top:70000001")));
            var searches=new List<string>();
            using var youtube=new YouTubeMusicScreen(root,directory,playback,new BottomBar(root,Read<BottomContract>("bottom.json"))) { SearchOptions=options,
                SearchFixture=(query,_)=> { searches.Add(query);return Task.FromResult<IReadOnlyList<YouTubeVideo>>([]); } };
            youtube.SeedRemoteFixture();var next=youtube.Create(loadDefault:false);host.Content=new Viewbox { Child=next };host.UpdateLayout();
            var input=Descendants<TextBox>(next).Single();input.Text="Next screen";
            await Until(()=>input.Text.Length==0&&searches.Count>0,"Pending clear did not resolve the current shared search screen");
            Require(browser.Input.Text=="A","Pending clear altered a retained hidden search view");
            searches.Clear();input.Text="Phone order";youtube.RemoteAdd(new("fixture0003","Phone search fixture","",""),false);await Task.Delay(200);
            Require(input.Text=="Phone order","YouTube order cleared input immediately");
            await Until(()=>input.Text.Length==0&&searches.Count>0,"YouTube order did not clear and refresh its current search");
            Require(new OriginalSearchSettings(directory).ClearAfterOrder,"Native enabled preference was not persisted");
            File.WriteAllText(Path.Combine(output,"search-options-verification.json"),JsonSerializer.Serialize(new {
                defaultOff=true,delayedNativeClear=true,editedTextCleared=true,prioritySchedulesClear=true,currentScreenResolution=true,
                retainedHiddenInputPreserved=true,youtubeOrderClear=true,youtubeSearchRefresh=true,persistedPreference=true,
                originalSettingsDialogPorted=false,liveYouTubeSearchTested=false
            },new JsonSerializerOptions { WriteIndented=true }));
            Canvas Tile()=>Descendants<Canvas>((DependencyObject)Descendants<ScrollViewer>(panel).Single().Content).Single(element=>Equals(element.Tag,"song-tile:70000001"));
            async Task Click(FrameworkElement element)
            {
                host.Activate();host.UpdateLayout();var point=element.PointToScreen(new Point(element.ActualWidth/2,element.ActualHeight/2));
                Require(SystemParameters.WorkArea.Contains(point)&&SetCursorPos((int)point.X,(int)point.Y),"Search fixture control was outside the desktop");await Task.Delay(70);
                element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            }
        }
        finally { playback.CommandOverride=previousOverride;host.Content=previous; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    { for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++) { var child=VisualTreeHelper.GetChild(parent,index);if(child is T value)yield return value;foreach(var item in Descendants<T>(child))yield return item; } }
    private static async Task Until(Func<bool> condition,string message)
    { var until=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(25);Require(condition(),message); }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
}
