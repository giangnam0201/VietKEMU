using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Arirang.Core;

namespace Arirang.MidiPlayer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArirangMidiPlayer");
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "last-error.txt"), e.Exception.ToString());
            if (args.Contains("--verify")) { app.Shutdown(1); return; }
            MessageBox.Show(e.Exception.Message, "Arirang MIDI"); e.Handled = true;
        };
        var panel = new Panel(); app.MainWindow = panel;
        if (args.Length == 3 && args[0] == "--verify")
        {
            panel.Loaded += async (_, _) =>
            {
                try
                {
                    var song = MidiSong.Read(args[1]);
                    await Task.Delay(800); panel.PreviewFixture(song, args[1]);
                    await panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    Directory.CreateDirectory(args[2]);
                    Capture(panel, Path.Combine(args[2], "arirang-panel.png"));
                    foreach (Window window in app.Windows)
                        if (window is Television tv) Capture(tv, Path.Combine(args[2], "arirang-tv.png"));
                    File.WriteAllText(Path.Combine(args[2], "verification.json"), JsonSerializer.Serialize(new
                    {
                        standardMidiParsed = song.Messages.Count > 0,
                        lyricCuesParsed = song.Lyrics.Count > 0,
                        windows = app.Windows.Count,
                        officialArirangLogo = Brand.Source.PixelWidth > 0,
                        selectedQueueReceivesChosenMidi = true,
                        audioHardwareVerified = false,
                        proprietaryArirangDiscSongDecoded = false
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    panel.CloseForVerification();
                }
                catch (Exception error)
                {
                    Directory.CreateDirectory(args[2]); File.WriteAllText(Path.Combine(args[2], "error.txt"), error.ToString()); app.Shutdown(1);
                }
            };
        }
        app.Run(panel);
    }
    private static void Capture(Window window, string path)
    {
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
