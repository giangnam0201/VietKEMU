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
                    bool dualVoicesVerified = false;
                    bool vietnameseRowsVerified = false;
                    bool bitmapFontRowsVerified = false;
                    foreach (Window window in app.Windows)
                        if (window is Television tv)
                        {
                            Capture(tv, Path.Combine(args[2], "arirang-tv.png"));
                            var dual = new MidiSong("Synthetic dual voice fixture", 2, [], [
                                new(0, "L", true, 1), new(.5, "l", true, 2),
                                new(1, "a", false, 1), new(1.5, "a", false, 2)]);
                            tv.Update(dual, .75, true); tv.UpdateLayout();
                            var rows = tv.RenderedVoiceRows;
                            if (rows.First != "La" || rows.Second != "la" || rows.FirstHighlighted != 1 || rows.SecondHighlighted != 1)
                                throw new InvalidDataException("Independent lyric voice rows or highlighting failed.");
                            Capture(tv, Path.Combine(args[2], "synthetic-dual-voice-tv.png"));
                            Capture(panel, Path.Combine(args[2], "synthetic-dual-voice-preview.png"));
                            tv.Update(dual, 1.75, true);
                            rows = tv.RenderedVoiceRows;
                            if (rows.FirstHighlighted != 2 || rows.SecondHighlighted != 2)
                                throw new InvalidDataException("Both lyric voices must advance independently.");
                            var vietnamese = new MidiSong("Synthetic Vietnamese voice fixture", 2, [], [
                                new(0, "Đ", true, 1), new(.5, "T", true, 2),
                                new(1, "á", false, 1), new(1, "i", false, 2),
                                new(1.2, "ễ", false, 2), new(1.4, "n", false, 2)]);
                            tv.Update(vietnamese, .75, true); tv.UpdateLayout();
                            rows = tv.RenderedVoiceRows;
                            if (rows.First != "Đá" || rows.Second != "Tiễn" || rows.FirstHighlighted != 1 || rows.SecondHighlighted != 1)
                                throw new InvalidDataException("Vietnamese accents or independent voice highlighting failed.");
                            Capture(tv, Path.Combine(args[2], "synthetic-vietnamese-voice-tv.png"));
                            Capture(panel, Path.Combine(args[2], "synthetic-vietnamese-voice-preview.png"));
                            tv.Update(vietnamese, 1.75, true);
                            rows = tv.RenderedVoiceRows;
                            if (rows.FirstHighlighted != 2 || rows.SecondHighlighted != 4)
                                throw new InvalidDataException("Vietnamese lyric rows must finish highlighting independently.");
                            vietnameseRowsVerified = true;
                            var bitmap = new MidiSong("Synthetic bitmap font fixture", 2, [], [
                                new(0, "A", true, 1, 65), new(.5, "B", true, 2, 66),
                                new(1, "B", false, 1, 66), new(1.5, "A", false, 2, 65)])
                                { LyricFont = SyntheticFont() };
                            tv.Update(bitmap, .75, true); tv.UpdateLayout();
                            rows = tv.RenderedVoiceRows;
                            if (!tv.OriginalBitmapFontActive || rows.First != "AB" || rows.Second != "BA" ||
                                rows.FirstHighlighted != 1 || rows.SecondHighlighted != 1)
                                throw new InvalidDataException("Original bitmap rendering path lost lyric rows or highlighting.");
                            Capture(tv, Path.Combine(args[2], "synthetic-bitmap-font-tv.png"));
                            Capture(panel, Path.Combine(args[2], "synthetic-bitmap-font-preview.png"));
                            int cached = tv.CachedGlyphBitmaps;
                            for (int repeat = 0; repeat < 10; repeat++) tv.Update(bitmap, .75, true);
                            if (cached != 4 || tv.CachedGlyphBitmaps != cached)
                                throw new InvalidDataException("Bitmap glyphs must be cached across unchanged frames.");
                            tv.Update(bitmap, 1.75, true); tv.UpdateLayout();
                            rows = tv.RenderedVoiceRows;
                            if (rows.FirstHighlighted != 2 || rows.SecondHighlighted != 2)
                                throw new InvalidDataException("Original bitmap rows must advance independently.");
                            bitmapFontRowsVerified = true;
                            tv.Update(song, .55, true);
                            if (tv.OriginalBitmapFontActive) throw new InvalidDataException("Standard MIDI must restore its text renderer.");
                            dualVoicesVerified = true;
                        }
                    File.WriteAllText(Path.Combine(args[2], "verification.json"), JsonSerializer.Serialize(new
                    {
                        standardMidiParsed = song.Messages.Count > 0,
                        lyricCuesParsed = song.Lyrics.Count > 0,
                        windows = app.Windows.Count,
                        officialArirangLogo = Brand.Source.PixelWidth > 0,
                        selectedQueueReceivesChosenMidi = true,
                        audioHardwareVerified = false,
                        independentLyricVoiceRowsVerified = dualVoicesVerified,
                        vietnameseLyricVoiceRowsVerified = vietnameseRowsVerified,
                        syntheticBitmapFontRowsRendered = bitmapFontRowsVerified,
                        unchangedBitmapFramesReuseCache = bitmapFontRowsVerified,
                        originalDiscPlaybackVerified = false
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

    private static MultakBitmapFont SyntheticFont()
    {
        // Independently generated toy glyphs; never a copy of original font data.
        var resource = new byte[4096 + 65536]; resource[24] = 2; resource[28] = 1;
        resource[72] = 84; resource[107] = 24; resource[108] = 48; resource[109] = 4; resource[110] = 1;
        string[][] patterns = [
            ["00100", "01010", "10001", "11111", "10001", "10001", "10001"],
            ["11110", "10001", "10001", "11110", "10001", "10001", "11110"]];
        for (int letter = 0; letter < patterns.Length; letter++)
        {
            var fill = new bool[24 * 48];
            for (int y = 0; y < 7; y++) for (int x = 0; x < 5; x++)
                if (patterns[letter][y][x] == '1')
                    for (int dy = 0; dy < 3; dy++) for (int dx = 0; dx < 3; dx++)
                        fill[(12 + y * 3 + dy) * 24 + 4 + x * 3 + dx] = true;
            for (int y = 0; y < 48; y++) for (int x = 0; x < 24; x++)
            {
                int level = fill[y * 24 + x] ? 3 : 0;
                if (level == 0)
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        if (y + dy is >= 0 and < 48 && x + dx is >= 0 and < 24 && fill[(y + dy) * 24 + x + dx]) level = 1;
                int pixel = y * 24 + x;
                resource[4096 + (65 + letter - 32) * 288 + pixel / 4] |= (byte)(level << (6 - pixel % 4 * 2));
            }
        }
        return MultakBitmapFont.Parse(resource, 7);
    }
}
