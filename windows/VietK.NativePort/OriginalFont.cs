using System.IO;
using System.Windows;
using System.Windows.Media;

namespace VietK.NativePort;

public static class OriginalFont
{
    public static FontFamily Family { get; private set; } = null!;

    public static void Initialize(string root)
    {
        var directory = Path.GetFullPath(Path.Combine(root, "fonts")) + Path.DirectorySeparatorChar;
        Family = new FontFamily(new Uri(directory), "./#Roboto");
        // Fail the cloud capture if Windows silently substitutes a system font.
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.Bold })
        {
            var face = new Typeface(Family, FontStyles.Normal, weight, FontStretches.Normal);
            if (!face.TryGetGlyphTypeface(out var glyph) || !glyph.FontUri.IsFile ||
                !Path.GetFullPath(glyph.FontUri.LocalPath).StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original firmware Roboto font did not load");
        }
    }
}
