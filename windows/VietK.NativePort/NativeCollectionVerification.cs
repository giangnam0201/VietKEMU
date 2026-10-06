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

internal static class NativeCollectionVerification
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    public static async Task Run(Window host,string root,string output)
    {
        static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
        var directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-native-collection-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);var previous=host.Content;
        var panel=new Canvas { Width=1280,Height=800,Background=new ImageBrush(new BitmapImage(new Uri(Path.Combine(root,"main_bg.jpg")))) };
        host.Content=new Viewbox { Child=panel };
        try
        {
            var model=new OriginalCollectionProfiles(directory);
            var contract=JsonSerializer.Deserialize<SongGridContract>(File.ReadAllText(Path.Combine(root,"song-grid.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var grid=new SongGrid(root,contract);ScrollViewer? view=null;
            var fixture=new CatalogueSong(1,"Collection fixture","CF",2,"Fixture Singer",8,1,1,"") { LocalState=1 };
            void Refresh(IReadOnlySet<int> ids)
            {
                if(view is not null)panel.Children.Remove(view);
                view=grid.Create(new[]{fixture},collected:ids);panel.Children.Add(view);Canvas.SetLeft(view,35);Canvas.SetTop(view,150);
            }
            var controls=new NativeCollectionControls(model,()=>panel,Refresh);
            grid.ActionRequested+=(song,action)=> { if(action=="collect")controls.Collect(song.Id); };
            Image Favorite()=>Descendants<Image>(panel).Single(image=>Equals(image.Tag,"collect:1"));
            async Task ClickFavorite()
            {
                host.Activate();host.UpdateLayout();var icon=Favorite();var point=icon.PointToScreen(new Point(icon.ActualWidth/2,icon.ActualHeight/2));
                Require(SetCursorPos((int)point.X,(int)point.Y),"Could not position fixture pointer");await Task.Delay(70);
                icon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            }
            void Confirm(string user,string password)
            {
                Require(controls.Username is not null&&controls.Password is not null&&controls.Confirm is not null,"Original collection dialog is missing");
                controls.Username!.Text=user;controls.Password!.Password=password;
                controls.Confirm!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await ClickFavorite();Require(controls.Username is not null&&!model.Contains(1),"Favorite icon did not require login");
            var windows=Application.Current.Windows.Count;
            Require(windows==2,"Collection login opened a third window");Capture("original-collection-login.png");
            Confirm("tiny","a");Require(controls.Username is not null&&!model.Contains(1),"Invalid login confirmed pending favorite");
            Confirm("fixture","fixture");Require(model.Contains(1)&&controls.Username is null,"Original login did not retry pending favorite");
            host.UpdateLayout();
            Require(((BitmapImage)Favorite().Source).UriSource.LocalPath==Path.GetFullPath(Path.Combine(root,contract.Icons["button_add_song_item_collected_normal"].File)),"Favorite icon did not reflect confirmed profile");
            Require(new OriginalCollectionProfiles(directory).Contains(1),"Native favorite was not persisted");Capture("original-collection-confirmed.png");
            Require(Descendants<Border>(panel).Count(border=>Equals(border.Tag,"collection-feedback"))==1&&controls.LastFeedback=="Sưu tập thành công","Collection feedback overlapped an earlier validation message");
            controls.Logout();await ClickFavorite();Confirm("fixture","fixture");
            Require(model.Contains(1)&&new OriginalCollectionProfiles(directory).Contains(1),"Pending add after login removed an existing profile favorite");
            await ClickFavorite();Require(!model.Contains(1)&&new OriginalCollectionProfiles(directory).Snapshot().Count==0,"Favorite icon did not remove persisted song");
            controls.Logout();await ClickFavorite();controls.Close();Require(!model.Contains(1),"Cancel collected a pending song");
            controls.Login();Confirm("fixture","wrong");Require(model.CurrentUser.Length==0&&controls.Username is not null&&controls.Username.Text.Length==0&&controls.Password!.Password.Length==0,"Failed login changed session or did not clear fields");
            Confirm("fixture","fixture");Require(model.CurrentUser=="fixture"&&model.Snapshot().Count==0,"Existing native profile login failed");
            File.WriteAllText(Path.Combine(output,"collection-verification.json"),JsonSerializer.Serialize(new { actualGridIcon=true,pendingFavoriteAfterLogin=true,pendingAddPreservesExistingFavorite=true,confirmedIcon=true,persistedAddRemove=true,cancelPreservesCollection=true,wrongPasswordClearsFields=true,singleFeedbackMessage=true,twoWindows=true,fullCollectionFragment=false },new JsonSerializerOptions { WriteIndented=true }));
            void Capture(string name)
            {
                panel.UpdateLayout();panel.Measure(new Size(1280,800));panel.Arrange(new Rect(0,0,1280,800));
                var bitmap=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
            }
        }
        finally
        {
            host.Content=previous;
            var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!directory.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("vietk-native-collection-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture cleanup path");
            Directory.Delete(directory,true);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
