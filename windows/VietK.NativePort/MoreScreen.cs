using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace VietK.NativePort;

public sealed record MoreTile(string Id, string Image, string Text, double X, double Y,
    double Width, double Height, double TextBottom, double TextPadding, bool Multilingual, bool SingleLine);
public sealed record MoreContract(MoreTile[] Tiles, double BackX, double BackY, double BackWidth, double BackHeight,
    double BackCorner, string BackStartColor, string BackEndColor);

// MoreFragment's layout. Feature tiles await their complete handlers (including
// password/room guards); Back matches recoveryMenuBarTab -> Home.
public sealed class MoreScreen(string root, MoreContract contract, HomeContract home)
{
    public event Action? HomeRequested;
    public Canvas Create()
    {
        var canvas = new Canvas
        {
            Width = 1280, Height = 800, ClipToBounds = true,
            Background = new ImageBrush(new BitmapImage(new Uri(Path.Combine(root, "main_bg.jpg"))))
            { Stretch = Stretch.UniformToFill }
        };
        var tileFactory = new HomeScreen(root, home);
        foreach (var data in contract.Tiles)
        {
            var tile = tileFactory.Tile(data.Image, data.Text, data.Width, -1, data.Height, data.TextBottom,
                data.TextPadding, data.Multilingual, data.SingleLine);
            Canvas.SetLeft(tile, data.X); Canvas.SetTop(tile, data.Y); canvas.Children.Add(tile);
        }
        var back = new Border
        {
            Width = contract.BackWidth, Height = contract.BackHeight,
            CornerRadius = new CornerRadius(contract.BackCorner),
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(contract.BackStartColor),
                (Color)ColorConverter.ConvertFromString(contract.BackEndColor), new Point(0, .5), new Point(1, .5)),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1)
        };
        back.Child = new Image
        {
            Width = 27, Height = 20, Stretch = Stretch.Fill,
            Source = new BitmapImage(new Uri(Path.Combine(root, "icon_back.png")))
        };
        void Scale(double value)
        {
            var transform = (ScaleTransform)back.RenderTransform;
            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(25))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        back.MouseLeftButtonDown += (_, e) => { back.CaptureMouse(); Scale(.9); e.Handled = true; };
        back.MouseLeftButtonUp += (_, e) =>
        {
            var inside = new Rect(0, 0, back.ActualWidth, back.ActualHeight).Contains(e.GetPosition(back));
            back.ReleaseMouseCapture(); Scale(1); if (inside) HomeRequested?.Invoke(); e.Handled = true;
        };
        back.LostMouseCapture += (_, _) => Scale(1);
        Canvas.SetLeft(back, contract.BackX); Canvas.SetTop(back, contract.BackY); canvas.Children.Add(back);
        return canvas;
    }
}
