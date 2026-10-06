using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

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
            var bottomContract = JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root, "bottom.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original bottom bar contract");
            var bottom = new BottomBar(root, bottomContract);
            var moreContract = JsonSerializer.Deserialize<MoreContract>(File.ReadAllText(Path.Combine(root, "more.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original More screen contract");
            var more = new MoreScreen(root, moreContract, contract);
            Canvas Panel(int screen = 0)
            {
                var panel = screen == 38 ? more.Create() : renderer.Create();
                var bar = bottom.Create();
                Canvas.SetTop(bar, bottomContract.Y); panel.Children.Add(bar);
                return panel;
            }
            if (args.Length == 2 && args[0] == "--capture")
            {
                Directory.CreateDirectory(args[1]);
                var canvas = Panel();
                canvas.Measure(new Size(1280, 800));
                canvas.Arrange(new Rect(0, 0, 1280, 800));
                canvas.UpdateLayout();
                var image = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                image.Render(canvas);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(Path.Combine(args[1], "native-home.png"))) encoder.Save(file);
                var moreCanvas = Panel(38);
                moreCanvas.Measure(new Size(1280, 800)); moreCanvas.Arrange(new Rect(0, 0, 1280, 800)); moreCanvas.UpdateLayout();
                var moreImage = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                moreImage.Render(moreCanvas);
                var moreEncoder = new PngBitmapEncoder(); moreEncoder.Frames.Add(BitmapFrame.Create(moreImage));
                using (var file = File.Create(Path.Combine(args[1], "native-more.png"))) moreEncoder.Save(file);
                // The contract was extracted from HomeNewFragment, not guessed.
                var expected = new[] { "singer", "app", "mixcloud", "youtube", "soudcloud", "more" };
                if (!contract.Tiles.Select(t => t.Tag).SequenceEqual(expected))
                    throw new InvalidDataException("Original default home tile order changed");
                using var catalogue = new WholeCatalogue(Path.Combine(root, "wholekmbox.db"));
                var song = catalogue.GetSongById(101000);
                if (song?.Name != "Mộng dưới hoa (sc)" || song.Spell != "MDH" || song.Singer != "Ái Vân,Thái Châu")
                    throw new InvalidDataException("Native catalogue lookup differs from supplied firmware");
                if (catalogue.GetSongById(-1) is not null || catalogue.IsOnline(-1))
                    throw new InvalidDataException("Native catalogue fabricated a missing song");
                var seedPath = Path.Combine(root, "local-seed.db");
                var originalSeedHash = SHA256.HashData(File.ReadAllBytes(seedPath));
                var localPath = Path.Combine(args[1], "local-state-check.db");
                using (var local = new LocalSongDatabase(seedPath, localPath))
                {
                    // Supplied firmware has an empty local seed: catalogue
                    // metadata must not become locally playable song results.
                    if (local.Search.BySpell("", 0, 0, new(), new()).Count != 0)
                        throw new InvalidDataException("Original empty local seed exposed available songs");
                }
                var upgradedHash = SHA256.HashData(File.ReadAllBytes(localPath));
                using (var reopened = new LocalSongDatabase(seedPath, localPath))
                    reopened.Search.ByName("", 0, new(), new());
                if (!SHA256.HashData(File.ReadAllBytes(localPath)).SequenceEqual(upgradedHash) ||
                    !SHA256.HashData(File.ReadAllBytes(seedPath)).SequenceEqual(originalSeedHash))
                    throw new InvalidDataException("Repeat startup changed local state or modified original seed");
                if (!bottom.IsVisible("pause_imv") || bottom.IsVisible("play_imv") ||
                    !bottom.IsVisible("ori_imv") || bottom.IsVisible("accp_imv"))
                    throw new InvalidDataException("Original default paired control state differs");
                bottom.SetConfirmedPlaybackState(true, true);
                if (!bottom.IsVisible("play_imv") || bottom.IsVisible("pause_imv") ||
                    !bottom.IsVisible("accp_imv") || bottom.IsVisible("ori_imv"))
                    throw new InvalidDataException("Paired controls do not follow confirmed playback state");
                var history = new FragmentHistory();
                history.Reload(new(1)); history.Reload(new(28)); history.Back();
                if (history.Current.Tag != 1) throw new InvalidDataException("Singer back navigation differs");
                history.Back();
                if (history.Current.Tag != 0) throw new InvalidDataException("Repeated Back revisits the page just left");
                history.Reload(new(35)); history.Reload(new(2)); history.Back(mixcloudEnabled: false);
                if (history.Current.Tag != 0) throw new InvalidDataException("Disabled Mixcloud back fallback differs");
                var arguments = new Dictionary<string, string> { ["youtube_search"] = "sample" };
                history.Reload(new(34, arguments)); history.Reload(new(2)); history.Back();
                if (history.Current.Tag != 34 || history.Current.Arguments is not null)
                    throw new InvalidDataException("YouTube search arguments were not cleared on Back");
                history.Reload(new(0));
                if (history.Entries.Count != 0) throw new InvalidDataException("Home failed to clear navigation history");
                var guard = new OriginalClickGuard();
                if (!guard.TryClick(1000) || guard.TryClick(1500) || !guard.TryClick(1501) || !guard.TryClick(1000))
                    throw new InvalidDataException("Original click guard boundary/clock-reset rules differ");
                File.WriteAllText(Path.Combine(args[1], "verification.json"), JsonSerializer.Serialize(new
                {
                    nativeWindowsRendering = true,
                    androidRuntimeUsed = false,
                    originalDefaultTileOrderVerified = true,
                    originalAssetsVerifiedDuringPackaging = true,
                    nativeCatalogueLookupVerified = true,
                    originalLocalSeedUpgradeVerified = true,
                    originalSongCount = catalogue.GetCount(),
                    bottomControlStateRulesVerified = true,
                    originalNavigationHistoryVerified = true,
                    originalClickGuardVerified = true,
                    moreScreenNativeRendering = true,
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
                Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() }
            };
            bottom.CommandRequested += command =>
            {
                if (command == "home_imv")
                    window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
                // Playback, queue and ambience requests need their real backends.
                // They are not translated into invented playback success/state.
            };
            renderer.NavigationRequested += fragment =>
            {
                if (fragment == 38) window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel(38) };
            };
            more.HomeRequested += () => window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
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
