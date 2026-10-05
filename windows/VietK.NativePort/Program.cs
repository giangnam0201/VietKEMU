using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Original");
            var contract = JsonSerializer.Deserialize<HomeContract>(File.ReadAllText(Path.Combine(root, "home.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original home contract");
            var app = new Application();
            var renderer = new HomeScreen(root, contract);
            if (args.Length == 2 && args[0] == "--capture")
            {
                Directory.CreateDirectory(args[1]);
                var canvas = renderer.Create();
                canvas.Measure(new Size(1280, 800));
                canvas.Arrange(new Rect(0, 0, 1280, 800));
                canvas.UpdateLayout();
                var image = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                image.Render(canvas);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(Path.Combine(args[1], "native-home.png"))) encoder.Save(file);
                // The contract was extracted from HomeNewFragment, not guessed.
                var expected = new[] { "singer", "app", "mixcloud", "youtube", "soudcloud", "more" };
                if (!contract.Tiles.Select(t => t.Tag).SequenceEqual(expected))
                    throw new InvalidDataException("Original default home tile order changed");
                File.WriteAllText(Path.Combine(args[1], "verification.json"), JsonSerializer.Serialize(new
                {
                    nativeWindowsRendering = true,
                    androidRuntimeUsed = false,
                    originalDefaultTileOrderVerified = true,
                    originalAssetsVerifiedDuringPackaging = true,
                    homeResourcePort = "implemented; visual fidelity requires comparison",
                    navigation = "pending", television = "pending", playback = "pending", servers = "pending",
                    fullFidelity = "unverified"
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            // Component host while the remaining screens and handlers are ported.
            // Pending handlers are deliberately not represented as implemented.
            var window = new Window
            {
                Title = "VietK — native home component (port in development)",
                Width = 1280, Height = 800, Background = Brushes.Black,
                Content = new Viewbox { Stretch = Stretch.Uniform, Child = renderer.Create() }
            };
            return app.Run(window);
        }
        catch (Exception error)
        {
            var log = Path.Combine(Path.GetTempPath(), "vietk-native-startup-error.txt");
            File.WriteAllText(log, error.ToString());
            if (!args.Contains("--capture")) MessageBox.Show(error.Message, "VietK native component");
            return 1;
        }
    }
}
