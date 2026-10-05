using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

public sealed record BarButton(string Href, string Image, string Text, double X, double Y, double Width, double Height);
public sealed record BottomContract(BarButton[] Buttons, double Y, double ModuleTop,
    double ImageWidth, double ImageHeight, double TextTop, double TextHeight, double TextSize, string TextColor);

// BottomMenuBarView.addListener + CommonModuleView + image_btn_with_text_lay.
// Commands are requests: paired icons only change on confirmed backend state.
public sealed class BottomBar(string root, BottomContract contract)
{
    public event Action<string>? CommandRequested;
    public bool Paused { get; private set; }
    public bool OriginalVocal { get; private set; }
    private readonly Dictionary<string, FrameworkElement> buttons = [];

    public Canvas Create()
    {
        buttons.Clear();
        var bar = new Canvas { Width = 1280, Height = 100, ClipToBounds = false };
        foreach (var data in contract.Buttons)
        {
            var control = new Grid
            {
                Width = data.Width, Height = data.Height, Background = Brushes.Transparent,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1)
            };
            control.Children.Add(new Image
            {
                Width = contract.ImageWidth, Height = contract.ImageHeight,
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(Path.Combine(root, data.Image)))
            });
            control.Children.Add(new TextBlock
            {
                Text = data.Text, FontSize = contract.TextSize, Height = contract.TextHeight,
                Foreground = (Brush)new BrushConverter().ConvertFromString(contract.TextColor)!,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, contract.ImageHeight + contract.TextTop, 0, 0)
            });
            var lastClick = 0L;
            control.MouseLeftButtonDown += (_, e) => { control.CaptureMouse(); Scale(control, .9); e.Handled = true; };
            control.MouseLeftButtonUp += (_, e) =>
            {
                var inside = new Rect(0, 0, control.ActualWidth, control.ActualHeight).Contains(e.GetPosition(control));
                control.ReleaseMouseCapture(); Scale(control, 1);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var interval = now - lastClick;
                if (inside && (interval > 500 || interval < 0))
                { lastClick = now; CommandRequested?.Invoke(data.Href); }
                e.Handled = true;
            };
            control.LostMouseCapture += (_, _) => Scale(control, 1);
            Canvas.SetLeft(control, data.X); Canvas.SetTop(control, contract.ModuleTop + data.Y);
            bar.Children.Add(control); buttons.Add(data.Href, control);
        }
        // BottomMenuBarView.addListener: queue badge at x=1220,y=0,
        // 30x20px, white bold count, original icon_playlist_num background.
        var count = new TextBlock
        {
            Width = 30, Height = 20, Text = "0", FontSize = 14,
            FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center,
            Background = new ImageBrush(new BitmapImage(new Uri(Path.Combine(root, "icon_playlist_num.png"))))
        };
        Canvas.SetLeft(count, 1220); Canvas.SetTop(count, 0); bar.Children.Add(count);
        SetConfirmedPlaybackState(Paused, OriginalVocal);
        return bar;
    }

    public void SetConfirmedPlaybackState(bool paused, bool originalVocal)
    {
        Paused = paused; OriginalVocal = originalVocal;
        Set("play_imv", paused); Set("pause_imv", !paused);
        Set("ori_imv", !originalVocal); Set("accp_imv", originalVocal);
    }

    public bool IsVisible(string href) => buttons.TryGetValue(href, out var button) && button.Visibility == Visibility.Visible;
    private void Set(string href, bool visible)
    { if (buttons.TryGetValue(href, out var button)) button.Visibility = visible ? Visibility.Visible : Visibility.Hidden; }

    private static void Scale(Grid control, double value)
    {
        var transform = (ScaleTransform)control.RenderTransform;
        var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(25))
        { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
