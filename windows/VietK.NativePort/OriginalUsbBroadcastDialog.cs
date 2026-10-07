using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VietK.NativePort;

// USBSetBroadcastDialog: confirm -> copying -> result -> second confirmation.
internal sealed class OriginalUsbBroadcastDialog
{
    internal Canvas Overlay { get; }=BroadcastDialogUi.Overlay();
    internal bool Busy { get; private set; }
    internal bool? Result { get; private set; }
    private readonly Canvas host;
    private readonly Func<Task<bool>> operation;
    private readonly Action completed;
    private readonly bool deleting;
    private readonly TextBlock title;
    private readonly FrameworkElement cancel,confirm,tip,loading;
    internal OriginalUsbBroadcastDialog(Canvas host,Func<Task<bool>> operation,Action completed,bool deleting=false)
    {
        this.host=host;this.operation=operation;this.completed=completed;this.deleting=deleting;
        var body=new Canvas { Width=500,Height=350 };BroadcastDialogUi.Body(Overlay,body,390,225);
        title=BroadcastDialogUi.Label(deleting?"Bạn có muốn xoá đoạn video không?":"Bạn có muốn đặt lại phát sóng công cộng?",470,95,24,true);
        title.TextWrapping=TextWrapping.Wrap;title.TextTrimming=TextTrimming.None;BroadcastDialogUi.Put(body,title,15,90);
        loading=new ProgressBar { Width=180,Height=10,IsIndeterminate=true,Visibility=Visibility.Collapsed };BroadcastDialogUi.Put(body,loading,160,100);
        tip=BroadcastDialogUi.Label("Không rút USB khi đang sao chép!",470,35,14,true);tip.Visibility=Visibility.Collapsed;((TextBlock)tip).Foreground=Brushes.Red;BroadcastDialogUi.Put(body,tip,15,205);
        cancel=BroadcastDialogUi.Action("Hủy",140,46,20,Close,"broadcast-usb:cancel",true,true);
        confirm=BroadcastDialogUi.Action("Xác nhận",140,46,20,()=>_=Confirm(),"broadcast-usb:confirm",true);
        BroadcastDialogUi.Put(body,cancel,100,254);BroadcastDialogUi.Put(body,confirm,260,254);
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { if(!Busy)Close();e.Handled=true; } };
        Panel.SetZIndex(Overlay,1001);host.Children.Add(Overlay);Keyboard.Focus(Overlay);
    }
    internal async Task Confirm()
    {
        if(Busy)return;
        if(Result is not null) { Close();return; }
        Busy=true;title.Visibility=Visibility.Collapsed;loading.Visibility=Visibility.Visible;
        tip.Visibility=deleting?Visibility.Collapsed:Visibility.Visible;cancel.Visibility=Visibility.Collapsed;confirm.IsEnabled=false;
        try { Result=await operation(); }
        catch(Exception error) when(error is System.IO.IOException or UnauthorizedAccessException) { Result=false; }
        finally { Busy=false;loading.Visibility=Visibility.Collapsed;title.Visibility=Visibility.Visible;confirm.IsEnabled=true; }
        if(deleting&&Result==true) { Close();return; }
        title.Text=Result==true?"Đặt thành công phát sóng":"Đặt phát sóng không thành công";
    }
    private void Close() { if(Busy)return;host.Children.Remove(Overlay);completed(); }
}
