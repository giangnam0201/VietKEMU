using Arirang.Core;
using System.Text.Json;

internal static class BitmapFontChecks
{
    private static byte[] Synthetic()
    {
        var data = new byte[4096 + 65536];
        data[24] = 2; data[28] = 1; // bank four: sector two, 256 allocation units
        data[72] = 84; data[107] = 24; data[108] = 48; data[109] = 4; data[110] = 1;
        data[4096 + (65 - 32) * 288] = 0x1c; // transparent, outline, fill, transparent
        return data;
    }

    internal static void Run()
    {
        var resource = Synthetic(); var font = MultakBitmapFont.Parse(resource, 7);
        if (font.Glyph(65).Span[0] != 0x1c || font.Glyph(32).Span.IndexOfAnyExcept((byte)0) >= 0 ||
            !font.TryGetCode('Đ', out byte code) || code != 0xae || font.TryGetCode('中', out _))
            throw new Exception("Bitmap bank, pixel order and Vietnamese glyph identity.");
        resource[4096 + (65 - 32) * 288] = 0;
        if (font.Glyph(65).Span[0] != 0x1c) throw new Exception("Font must own an immutable bank copy.");
        var badGeometry = Synthetic(); badGeometry[107] = 48;
        var badOffset = Synthetic(); badOffset[26] = 255;
        var badPalette = Synthetic(); badPalette[4096] = 0x80;
        var badCount = Synthetic(); badCount[28] = 2;
        foreach (var invalid in new[] { Synthetic()[..^1], badGeometry, badOffset, badPalette, badCount })
        {
            bool rejected = false;
            try { MultakBitmapFont.Parse(invalid, 7); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Unsupported/truncated font banks must be rejected.");
        }
        string path = Path.Combine(Path.GetTempPath(), "arirang-font-check-" + Guid.NewGuid() + ".iso");
        try
        {
            var bytes = new byte[2048 + resource.Length]; Synthetic().CopyTo(bytes, 2048);
            File.WriteAllBytes(path, bytes);
            var inventory = new DiscInventory("synthetic", [new("/FONT1.BIN", resource.Length, 2048)]);
            var fromIso = MultakBitmapFont.ReadIso(path, inventory, 7);
            if (fromIso is null || fromIso.Glyph(65).Span[0] != 0x1c)
                throw new Exception("Bounded font ISO reading.");
            if (MultakBitmapFont.ReadIso(path, new("synthetic", []), 7) is not null)
                throw new Exception("Absent original font must permit fallback.");
        }
        finally { File.Delete(path); }
    }

    internal static void ExportOriginal(string input, string output)
    {
        if (new FileInfo(input).Length > MultakBitmapFont.MaximumBytes)
            throw new InvalidDataException("Original font input exceeds limit.");
        byte[] data = File.ReadAllBytes(input);
        var reports = new List<object>();
        foreach (byte language in new byte[] { 4, 7 })
        {
            var font = MultakBitmapFont.Parse(data, language);
            var counts = new int[4];
            for (int code = 32; code < 256; code++)
                foreach (byte packed in font.Glyph((byte)code).Span)
                    for (int shift = 6; shift >= 0; shift -= 2) counts[packed >> shift & 3]++;
            int[] expected = language == 4 ? [161578, 66687, 0, 29783] : [124700, 75555, 0, 57793];
            if (!counts.SequenceEqual(expected) || font.Glyph(32).Span.IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException("Original font pixels differ from independent bitmap inspection.");
            reports.Add(new { languageId = language, glyphs = 224, width = 24, height = 48,
                transparentPixels = counts[0], outlinePixels = counts[1], fillPixels = counts[3] });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            originalBanksVerified = 2, bitmapLayoutVerified = true, pixelLevelsVerified = true,
            originalFontBundled = false, originalDeviceRenderingVerified = false, banks = reports
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
