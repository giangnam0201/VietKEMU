using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

public sealed record HomeTile(string Tag, string Image, string Text, int Fragment);
public sealed record HomeContract(HomeTile[] Tiles, string SongName, double TileHeight,
    double TileWidth, double RowGap, double ColumnGap, double TextSize, double TextBottom,
    double PaddingTop, string Provenance);

// Translation of HomeNewFragment + HomeNewAdapter + layout_home_fragment.xml.
// The rest of MainActivity (bars, services and navigation) is a separate port.
public sealed class HomeScreen(string root, HomeContract contract)
{
    public event Action<int>? NavigationRequested;

    public Canvas Create()
    {
        var background = Image("main_bg.jpg");
        var canvas = new Canvas
        {
            Width = 1280, Height = 800, ClipToBounds = true,
            Background = new ImageBrush(background) { Stretch = Stretch.UniformToFill }
        };
        // layout_home_fragment: 50dp left, 64dp top, 404dp phantom, 35dp gap.
        var phantom = new Border
        {
            Width = 404, Height = contract.TileHeight,
            Background = Brushes.Black, CornerRadius = new CornerRadius(15)
        };
        Put(canvas, phantom, 50, contract.PaddingTop);
        var song = Tile("icon_song_name.png", contract.SongName, 404, 2);
        Put(canvas, song, 50, contract.PaddingTop + contract.TileHeight + contract.RowGap);
        // RecyclerView width 750dp / 3 columns = 250dp. Item is 235dp wide;
        // GridLayoutManager supplies its own column slots, not a guessed gap.
        for (var index = 0; index < contract.Tiles.Length; index++)
        {
            var data = contract.Tiles[index];
            Put(canvas, Tile(data.Image, data.Text, contract.TileWidth, data.Fragment),
                50 + 404 + 35 + index % 3 * 250,
                contract.PaddingTop + index / 3 * (contract.TileHeight + contract.RowGap));
        }
        return canvas;
    }

    public Grid Tile(string image, string text, double width, int fragment,
        double? height = null, double? textBottom = null)
    {
        var tile = new Grid
        {
            Width = width, Height = height ?? contract.TileHeight,
            Background = new ImageBrush(Image(image)) { Stretch = Stretch.Fill },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
        };
        tile.Children.Add(new TextBlock
        {
            Text = text, Foreground = Brushes.White, FontSize = contract.TextSize,
            FontFamily = new FontFamily("sans-serif"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, textBottom ?? contract.TextBottom)
        });
        // BaseScaleOnTouchListener: down 1->0.9, up 0.9->1.
        tile.MouseLeftButtonDown += (_, e) => { tile.CaptureMouse(); Scale(tile, 0.9); e.Handled = true; };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            var inside = new Rect(0, 0, tile.ActualWidth, tile.ActualHeight).Contains(e.GetPosition(tile));
            tile.ReleaseMouseCapture(); Scale(tile, 1);
            if (inside) NavigationRequested?.Invoke(fragment);
            e.Handled = true;
        };
        tile.LostMouseCapture += (_, _) => Scale(tile, 1);
        return tile;
    }

    private static void Scale(Grid tile, double value)
    {
        var transform = (ScaleTransform)tile.RenderTransform;
        // AnimCommonUtils.scaleXY uses 25 ms and Android's default
        // AccelerateDecelerateInterpolator (cosine ease-in/ease-out).
        var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(25))
        { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private BitmapImage Image(string name)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.Combine(root, name)); image.EndInit(); image.Freeze();
        return image;
    }

    private static void Put(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element);
    }
}
