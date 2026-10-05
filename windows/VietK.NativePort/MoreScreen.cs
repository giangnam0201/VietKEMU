using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

public sealed record MoreTile(string Id, string Image, string Text, double X, double Y,
    double Width, double Height, double TextBottom);
public sealed record MoreContract(MoreTile[] Tiles, double BackX, double BackY, double BackWidth, double BackHeight);

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
            var tile = tileFactory.Tile(data.Image, data.Text, data.Width, -1, data.Height, data.TextBottom);
            Canvas.SetLeft(tile, data.X); Canvas.SetTop(tile, data.Y); canvas.Children.Add(tile);
        }
        var back = new Grid
        {
            Width = contract.BackWidth, Height = contract.BackHeight,
            Background = new ImageBrush(new BitmapImage(new Uri(Path.Combine(root, "icon_back_bg.png")))) { Stretch = Stretch.Fill }
        };
        back.Children.Add(new Image
        {
            Width = 27, Height = 20, Stretch = Stretch.Fill,
            Source = new BitmapImage(new Uri(Path.Combine(root, "icon_back.png")))
        });
        back.MouseLeftButtonUp += (_, _) => HomeRequested?.Invoke();
        Canvas.SetLeft(back, contract.BackX); Canvas.SetTop(back, contract.BackY); canvas.Children.Add(back);
        return canvas;
    }
}
