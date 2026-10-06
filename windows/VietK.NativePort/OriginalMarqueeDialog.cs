using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VietK.Core;

namespace VietK.NativePort;

// MarqueeSettingDialog and dialog_setting_marquee_view. XML dimensions take
// precedence over the unused Java width/height constants.
public sealed class OriginalMarqueeDialog
{
    public Canvas Overlay { get; }=new() { Width=1280,Height=800,Background=new SolidColorBrush(Color.FromArgb(128,0,0,0)) };
    internal TextBox Editor { get; }
    internal TextBlock Counter { get; }
    private readonly Canvas host;
    private readonly Action<string> save;
    public OriginalMarqueeDialog(Canvas host,OriginalMarqueeSettings settings,Action<string> save)
    {
        this.host=host;this.save=save;
        var content=new Canvas { Width=674,Height=475 };
        Put(Overlay,new Border { Width=674,Height=475,CornerRadius=new(10),Background=new SolidColorBrush(Color.FromRgb(72,23,64)),Child=content },303,117.5);
        Put(content,Label("Lời chào màn hình chờ",674,60,24,true),0,0);
        Put(content,new Border { Width=674,Height=2,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)) },0,60);
        Put(content,Label("Lời chào mẫu",230,36,20),30,72);
        var cloud=new RadioButton { Content="Online",FontSize=20,FontFamily=OriginalFont.Family,Foreground=Brushes.White,IsEnabled=false,
            ToolTip="Dịch vụ lời chào online của nhà sản xuất chưa khả dụng trên Windows." };
        Put(content,cloud,410,74);
        var local=new RadioButton { Content="Đầu máy",IsChecked=true,FontSize=20,FontFamily=OriginalFont.Family,Foreground=Brushes.White };
        Put(content,local,530,74);
        Put(content,Label("Chỉnh sửa lời chào",420,60,20),30,120);
        Button("Xóa",564,130,80,40,Clear,"clear",true);
        Editor=new TextBox { Width=614,Height=175,FontSize=16,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
            Background=new SolidColorBrush(Color.FromArgb(76,0,0,0)),BorderThickness=new(0),Padding=new(20),AcceptsReturn=true,
            TextWrapping=TextWrapping.Wrap,MaxLength=OriginalMarqueeSettings.MaximumLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,ContextMenu=null,
            Text=settings.LocalText,Tag="marquee:editor" };
        Put(content,Editor,30,180);
        local.Click+=(_,_)=>Editor.Text=settings.LocalText;
        Counter=Label("",100,28,14);Counter.TextAlignment=TextAlignment.Right;Counter.IsHitTestVisible=false;Counter.Tag="marquee:counter";
        Put(content,Counter,514,320);Editor.TextChanged+=(_,_)=>UpdateCounter();UpdateCounter();
        Button("Hủy",159.5,387,140,46,Close,"cancel");Button("Xác nhận",374.5,387,140,46,Confirm,"confirm");
        Overlay.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) { Close();e.Handled=true; } };
        // Original setCanceledOnTouchOutside(false): outside taps are absorbed.
        Panel.SetZIndex(Overlay,1000);host.Children.Add(Overlay);
        void Button(string text,double x,double y,double width,double height,Action action,string tag,bool neutral=false)
        {
            Brush background;
            if(neutral)background=new SolidColorBrush(Color.FromRgb(81,86,227));
            else
            {
                var gradient=new LinearGradientBrush { StartPoint=new(0,1),EndPoint=new(0,0) };
                if(tag=="cancel") { gradient.GradientStops.Add(new(Color.FromRgb(216,216,254),0));gradient.GradientStops.Add(new(Color.FromRgb(236,237,242),.5));gradient.GradientStops.Add(new(Colors.White,1)); }
                else { gradient.GradientStops.Add(new(Color.FromRgb(4,160,227),0));gradient.GradientStops.Add(new(Color.FromRgb(0,250,246),1)); }
                background=gradient;
            }
            var label=Label(text,width,height,neutral?16:24,true);if(tag=="cancel")label.Foreground=new SolidColorBrush(Color.FromRgb(38,41,100));
            var button=new Border { Width=width,Height=height,CornerRadius=new(26),Background=background,Child=label,Tag="marquee:"+tag };
            OriginalPressFeedback.Bind(button,.9);button.MouseLeftButtonUp+=(_,e)=> { action();e.Handled=true; };Put(content,button,x,y);
        }
    }
    private void UpdateCounter()=>Counter.Text=$"{OriginalMarqueeSettings.MaximumLength-Editor.Text.Length}/{OriginalMarqueeSettings.MaximumLength}";
    public void Clear()=>Editor.Clear();
    public void Confirm() { save(Editor.Text);Close(); }
    public void Close()=>host.Children.Remove(Overlay);
    private static TextBlock Label(string text,double width,double height,double size,bool centered=false)=>new() {
        Text=text,Width=width,Height=height,FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
        TextAlignment=centered?TextAlignment.Center:TextAlignment.Left,Padding=new(0,Math.Max(0,(height-size*1.3)/2),0,0) };
    private static void Put(Canvas parent,UIElement child,double x,double y) { parent.Children.Add(child);Canvas.SetLeft(child,x);Canvas.SetTop(child,y); }
}
