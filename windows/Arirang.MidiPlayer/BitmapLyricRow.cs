using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arirang.Core;

namespace Arirang.MidiPlayer;

internal sealed class BitmapLyricRow : FrameworkElement
{
    private MultakBitmapFont? font;
    private IReadOnlyList<LyricCue>? source;
    private int currentIndex = int.MinValue;
    private byte[] codes = [];
    private readonly Dictionary<(byte Code, bool Sung), BitmapSource> images = [];
    internal string Text { get; private set; } = "";
    internal int HighlightedCharacters { get; private set; }
    internal int CachedBitmaps => images.Count;

    internal bool Present(MultakBitmapFont nextFont, IReadOnlyList<LyricCue> cues, double seconds)
    {
        int low = 0, high = cues.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (cues[middle].Seconds <= seconds) low = middle + 1; else high = middle;
        }
        int current = low - 1;
        if (ReferenceEquals(font, nextFont) && ReferenceEquals(source, cues) && current == currentIndex) return true;
        int start = Math.Max(current, 0), end = Math.Min(start + 1, cues.Count);
        while (start > 0 && !cues[start].NewLine) start--;
        while (end < cues.Count && !cues[end].NewLine) end++;
        var nextCodes = new byte[end - start];
        for (int i = start; i < end; i++)
        {
            var cue = cues[i];
            if (cue.Text.Length != 1) return false;
            if (cue.OriginalGlyphCode is byte original)
            {
                if (original < MultakBitmapFont.FirstCode) return false;
                nextCodes[i - start] = original;
            }
            else if (nextFont.TryGetCode(cue.Text[0], out byte translated)) nextCodes[i - start] = translated;
            else return false;
        }
        if (!ReferenceEquals(font, nextFont)) images.Clear();
        font = nextFont; source = cues; currentIndex = current; codes = nextCodes;
        Text = string.Concat(cues.Skip(start).Take(end - start).Select(c => c.Text));
        HighlightedCharacters = Math.Clamp(current - start + 1, 0, codes.Length);
        InvalidateVisual(); return true;
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : codes.Length * 32, 64);

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (font is null || codes.Length == 0 || RenderSize.Width <= 0) return;
        double scale = Math.Min(64d / MultakBitmapFont.Height,
            RenderSize.Width / (codes.Length * MultakBitmapFont.Width));
        double width = MultakBitmapFont.Width * scale, height = MultakBitmapFont.Height * scale;
        double x = (RenderSize.Width - codes.Length * width) / 2, y = (RenderSize.Height - height) / 2;
        for (int i = 0; i < codes.Length; i++)
        {
            var key = (codes[i], i < HighlightedCharacters);
            if (!images.TryGetValue(key, out var image))
            {
                var packed = font.Glyph(codes[i]).Span;
                var pixels = new byte[MultakBitmapFont.Width * MultakBitmapFont.Height * 4];
                for (int pixel = 0; pixel < pixels.Length / 4; pixel++)
                {
                    int level = packed[pixel / 4] >> (6 - (pixel % 4) * 2) & 3;
                    if (level == 0) continue;
                    int at = pixel * 4; pixels[at + 3] = 255;
                    if (level != 3) continue; // Black outline survives the highlight.
                    pixels[at] = key.Item2 ? (byte)0 : (byte)255;
                    pixels[at + 1] = key.Item2 ? (byte)215 : (byte)255;
                    pixels[at + 2] = 255;
                }
                image = BitmapSource.Create(MultakBitmapFont.Width, MultakBitmapFont.Height, 96, 96,
                    PixelFormats.Bgra32, null, pixels, MultakBitmapFont.Width * 4);
                image.Freeze(); images.Add(key, image);
            }
            drawing.DrawImage(image, new Rect(x + i * width, y, width, height));
        }
    }
}
