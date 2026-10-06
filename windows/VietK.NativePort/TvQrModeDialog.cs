using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VietK.NativePort;

// SettingTvQrcodeModeDialog: staged selection, apply only on confirmation.
public sealed class TvQrModeDialog
{
    public Canvas Overlay { get; }=new() { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    public int Pending { get; private set; }
    private readonly Canvas host;
    private readonly TelevisionQr qr;
    public TvQrModeDialog(Canvas host,TelevisionQr qr)
    {
        this.host=host;this.qr=qr;Pending=qr.State.Mode;
        var content=new Canvas { Width=418,Height=398 };
        var box=new Border { Width=418,Height=398,CornerRadius=new(10),Background=new SolidColorBrush(Color.FromRgb(72,23,64)),Child=content };
        Put(Overlay,box,431,156);
        var title=new TextBlock { Text="Chế độ hiển thị mã QR lên TV",FontSize=24,Foreground=Brushes.White,FontFamily=OriginalFont.Family,Width=358,Height=60,TextWrapping=TextWrapping.Wrap };
        Put(content,title,15,5);
        var close=new Button { Content="×",Width=60,Height=60,FontSize=30 };close.Click+=(_,_)=>Close();Put(content,close,358,0);
        Put(content,new Border { Width=418,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,60);
        var labels=new[]{"Luôn hiển thị","Ẩn sau 20 giây","Không hiển thị"};
        for(var i=0;i<labels.Length;i++)
        {
            var index=i;var option=new RadioButton { Content=labels[i],GroupName="tv-qr-mode",IsChecked=i==Pending,FontSize=22,FontFamily=OriginalFont.Family,Foreground=Brushes.White,Width=380,Height=68,VerticalContentAlignment=VerticalAlignment.Center };
            option.Checked+=(_,_)=>Pending=index;Put(content,option,20,65+i*73);
        }
        var confirm=new Button { Content="Xác nhận",Width=140,Height=46,FontSize=22 };confirm.Click+=(_,_)=>Confirm();Put(content,confirm,139,290);
        Overlay.MouseLeftButtonDown+=(_,e)=> { if(e.OriginalSource==Overlay)Close(); };host.Children.Add(Overlay);
    }
    public void Select(int mode) { if(mode is <0 or >2)throw new ArgumentOutOfRangeException(nameof(mode));Pending=mode; }
    public void Confirm(bool persist=true) { qr.SetMode(Pending,persist);Close(); }
    public void Close()=>host.Children.Remove(Overlay);
    private static void Put(Canvas canvas,UIElement child,double x,double y) { canvas.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
