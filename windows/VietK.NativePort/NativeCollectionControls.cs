using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// CollectLoginDialog / dialog_input_pwd / view_collect_input_pwd. The modal
// lives on the existing panel; TV playback continues in its own window.
public sealed class NativeCollectionControls
{
    private readonly OriginalCollectionProfiles profiles;
    private readonly Func<Canvas?> panel;
    private readonly Action<IReadOnlySet<int>> refresh;
    private Canvas? host,overlay;
    private Canvas? feedbackHost;
    private Border? feedbackToast;
    private DispatcherTimer? feedbackTimer;
    internal TextBox? Username { get; private set; }
    internal PasswordBox? Password { get; private set; }
    internal Button? Confirm { get; private set; }
    internal string LastFeedback { get; private set; }="";
    public NativeCollectionControls(OriginalCollectionProfiles profiles,Func<Canvas?> panel,Action<IReadOnlySet<int>> refresh)
    {
        this.profiles=profiles;this.panel=panel;this.refresh=refresh;
        profiles.Changed+=Refresh;Refresh();
    }
    private void Refresh()=>refresh(profiles.Snapshot().ToHashSet());
    public void Collect(int id)
    {
        var result=profiles.Toggle(id);
        if(result==CollectionToggleResult.LoginRequired) { Login(()=>Collect(id));return; }
        Feedback(result switch {
            CollectionToggleResult.Added=>"Sưu tập thành công",
            CollectionToggleResult.Removed=>"Hủy bộ sưu tập",
            CollectionToggleResult.LimitReached=>"Ưa thích đạt giới hạn",
            _=>"Không thể lưu bộ sưu tập." });
    }
    public void Logout()
    {
        try { profiles.Logout();Feedback("chưa đăng nhập"); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { Feedback("Không thể lưu bộ sưu tập."); }
    }
    public void Login(Action? completed=null)
    {
        Close();host=panel();if(host is null)return;
        overlay=new Canvas { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
        var content=new Canvas { Width=480,Height=350 };
        Put(overlay,new Border { Width=480,Height=350,CornerRadius=new(10),Background=Brush("#ff481740"),Child=content },400,225);
        Put(content,Text("Đăng nhập",18,480,TextAlignment.Center),0,20);
        Put(content,new Border { Width=480,Height=2,Background=Brush("#1fffffff") },0,60);
        Username=new TextBox { Width=245,Height=50,MaxLength=12,FontSize=18,FontFamily=OriginalFont.Family,
            Foreground=Brushes.White,Background=Brush("#88000000"),BorderThickness=new(0),Padding=new(10),VerticalContentAlignment=VerticalAlignment.Center };
        Password=new PasswordBox { Width=245,Height=50,MaxLength=12,FontSize=18,FontFamily=OriginalFont.Family,
            Foreground=Brushes.White,Background=Brush("#88000000"),BorderThickness=new(0),Padding=new(10),VerticalContentAlignment=VerticalAlignment.Center };
        Put(content,Username,117.5,92);Put(content,Password,117.5,182);
        var userHint=Text("Vui lòng nhập tên người dùng",18,245);userHint.Foreground=Brush("#ccffffff");userHint.IsHitTestVisible=false;
        Put(content,userHint,127.5,106);Username.TextChanged+=(_,_)=>userHint.Visibility=Username.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        var passwordHint=Text("Vui lòng nhập mật khẩu",18,245);passwordHint.Foreground=Brush("#ccffffff");passwordHint.IsHitTestVisible=false;
        Put(content,passwordHint,127.5,196);Password.PasswordChanged+=(_,_)=>passwordHint.Visibility=Password.Password.Length==0?Visibility.Visible:Visibility.Collapsed;
        Put(content,Text("(độ dài：4~12)",12,100),372.5,110);Put(content,Text("(độ dài：4~12)",12,100),372.5,200);
        Confirm=new Button { Content="Xác nhận",Width=140,Height=46,FontSize=20,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
            Background=new LinearGradientBrush(Color.FromRgb(4,160,227),Color.FromRgb(0,250,246),new Point(0,1),new Point(0,0)),BorderThickness=new(0) };
        var buttonBorder=new FrameworkElementFactory(typeof(Border));buttonBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(26));
        buttonBorder.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent });
        var buttonLabel=new FrameworkElementFactory(typeof(ContentPresenter));buttonLabel.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        buttonLabel.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);buttonBorder.AppendChild(buttonLabel);
        Confirm.Template=new ControlTemplate(typeof(Button)) { VisualTree=buttonBorder };
        Confirm.Click+=(_,_)=>
        {
            var empty=Username.Text.Length==0||Password.Password.Length==0;
            var result=profiles.Login(Username.Text,Password.Password);
            if(result==CollectionLoginResult.Success) { Close();completed?.Invoke();return; }
            Feedback(result switch {
                CollectionLoginResult.WrongPassword=>"Sai mật mã, vui lòng nhập lại!",
                CollectionLoginResult.InvalidLength when empty=>"Tên người dùng và mật khẩu không thể để trống",
                CollectionLoginResult.InvalidLength=>"Tên người dùng và mật khẩu không thể ít hơn 4 chữ số",
                _=>"Tạo người dùng thất bại" });
            // Android clears both fields after a valid-length login attempt.
            if(result!=CollectionLoginResult.InvalidLength) { Username.Clear();Password.Clear();Username.Focus(); }
        };
        Put(content,Confirm,170,258);
        overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        host.Children.Add(overlay);Username.Focus();
    }
    internal void Close()
    { if(overlay is not null)host?.Children.Remove(overlay);overlay=null;Username=null;Password=null;Confirm=null; }
    private void Feedback(string text)
    {
        feedbackTimer?.Stop();
        if(feedbackToast is not null)feedbackHost?.Children.Remove(feedbackToast);
        feedbackToast=null;feedbackHost=null;
        LastFeedback=text;var target=panel();if(target is null)return;
        var toast=new Border { Tag="collection-feedback",Background=Brush("#dd222222"),CornerRadius=new(8),Padding=new(15),Child=Text(text,18,460,TextAlignment.Center),IsHitTestVisible=false };
        Put(target,toast,395,640);var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
        feedbackHost=target;feedbackToast=toast;feedbackTimer=timer;
        timer.Tick+=(_,_)=> { timer.Stop();target.Children.Remove(toast); };timer.Start();
    }
    private static TextBlock Text(string value,double size,double width,TextAlignment alignment=TextAlignment.Left)=>new()
    { Text=value,FontSize=size,Width=width,Foreground=Brushes.White,FontFamily=OriginalFont.Family,TextAlignment=alignment };
    private static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
    private static void Put(Canvas target,UIElement child,double x,double y) { target.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
