using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

public sealed record TopItem(string Href, string Image, double X, double Y, double Width, double Height);
public sealed record TopContract(TopItem[] Items, double DynamicWidth, double CommonHeight,
    double IconLeft, double TextLeft, double LanguageWidth, double LanguageHeight, double LanguageTop,
    double DownloadedLogoWidth, double DownloadedLogoHeight);

// TopMenuBarView + the default module_top skin. Dynamic controls use the
// disconnected/home state until their original service adapters are available.
public sealed class TopBar(string root, TopContract contract, string logoDirectory)
{
    public event Action<string>? CommandRequested;

    public Canvas Create()
    {
        var canvas = new Canvas { Width = 1280, Height = 73 };
        var logo = contract.Items.Single(item => item.Href == "logo");
        var downloaded = Path.Combine(logoDirectory, "touch.png");
        // TopMenuBarView.initLogoView/updateLogoView: retain the skin if the
        // server-delivered override is absent; downloaded logos have a new frame.
        var hasDownloadedLogo = File.Exists(downloaded);
        var image = Load(hasDownloadedLogo ? downloaded : Path.Combine(root, logo.Image));
        var logoView = new Image
        {
            Source = image, Stretch = Stretch.Fill,
            Width = hasDownloadedLogo ? contract.DownloadedLogoWidth : logo.Width,
            Height = hasDownloadedLogo ? contract.DownloadedLogoHeight : logo.Height,
        };
        logoView.MouseLeftButtonUp += (_, _) => CommandRequested?.Invoke("logo");
        Put(canvas, logoView, hasDownloadedLogo ? 10 : logo.X, hasDownloadedLogo ? 10 : logo.Y);

        // Original landscape top_bg is INVISIBLE, not a full-width bitmap.
        // USB/hotspot are GONE without a registered removable volume/AP.
        // The room-call replacement is GONE for MANAGER_TYPE_NULL.
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var network = Icon("wifi_no");
        var setting = Icon("setting");
        var shutdown = Icon("shut_down_btn");
        var language = Language();
        foreach (var view in new FrameworkElement[] { setting, network, shutdown, language })
            controls.Children.Add(view);
        // AutoAdaptionLayout: measure visible child widths, clamp the evenly
        // distributed right margins to 8..30px, then reset language margins.
        controls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var length = controls.Children.Cast<FrameworkElement>().Sum(view => view.DesiredSize.Width);
        var margin = Math.Clamp((int)(contract.DynamicWidth - length) / controls.Children.Count, 8, 30);
        foreach (var view in new[] { setting, network, shutdown })
            view.Margin = new Thickness(0, 0, margin, 0);
        language.Margin = new Thickness(0, contract.LanguageTop, 0, 0);
        controls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Put(canvas, controls, canvas.Width - controls.DesiredSize.Width, 0);
        return canvas;
    }

    private FrameworkElement Icon(string href)
    {
        var item = contract.Items.Single(item => item.Href == href);
        var bitmap = Load(Path.Combine(root, item.Image));
        var contents = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        contents.Children.Add(new Image
        {
            Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight,
            Stretch = Stretch.Fill, Margin = new Thickness(contract.IconLeft, 0, 0, 0)
        });
        // AutoAdaptionIconView retains its empty TextView's left margin.
        contents.Children.Add(new Border { Width = contract.TextLeft });
        var view = new Border { Height = contract.CommonHeight, Child = contents, Background = Brushes.Transparent };
        view.MouseLeftButtonUp += (_, _) => CommandRequested?.Invoke(href);
        return view;
    }

    private FrameworkElement Language()
    {
        var contents = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        contents.Children.Add(new Image
        {
            Source = Load(Path.Combine(root, "keyboard_earth.png")), Width = 25, Height = 25,
            Margin = new Thickness(contract.IconLeft, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        });
        contents.Children.Add(new TextBlock
        {
            Text = "Language", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            Margin = new Thickness(contract.TextLeft, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        });
        // Exact language_bg.xml: horizontal purple gradient, rounded left only.
        var view = new Border
        {
            Width = contract.LanguageWidth, Height = contract.LanguageHeight,
            CornerRadius = new CornerRadius(19, 0, 0, 19),
            Background = new LinearGradientBrush(Color.FromRgb(0xc0, 0x37, 0xd0), Color.FromRgb(0x74, 0x37, 0xe9), 0),
            Child = contents
        };
        view.MouseLeftButtonUp += (_, _) => CommandRequested?.Invoke("language");
        return view;
    }

    private static BitmapImage Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
        return image;
    }

    private static void Put(Canvas canvas, UIElement child, double x, double y)
    {
        Canvas.SetLeft(child, x); Canvas.SetTop(child, y); canvas.Children.Add(child);
    }
}
