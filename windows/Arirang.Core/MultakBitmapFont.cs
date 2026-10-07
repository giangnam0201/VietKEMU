namespace Arirang.Core;

// Bounded reader for the independently inspected FONT1 24x48 Latin/Vietnamese
// banks. Original font bytes remain in the user's ISO and in memory only.
public sealed class MultakBitmapFont
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public const int Width = 24, Height = 48, GlyphBytes = Width * Height / 4;
    public const int FirstCode = 32, GlyphCount = 224;
    private const int BankBytes = 65536;
    private readonly byte[] packed;
    public byte LanguageId { get; }

    private MultakBitmapFont(byte languageId, ReadOnlySpan<byte> bank)
    {
        if (bank.Length != BankBytes) throw new InvalidDataException("Truncated FONT1 bank.");
        // These inspected banks use 00 transparent, 01 outline, 11 fill.
        // Reject the reserved 10 palette entry instead of inventing its color.
        foreach (byte value in bank[..(GlyphCount * GlyphBytes)])
            for (int shift = 0; shift < 8; shift += 2)
                if ((value >> shift & 3) == 2)
                    throw new InvalidDataException("Unsupported FONT1 palette.");
        LanguageId = languageId;
        packed = bank[..(GlyphCount * GlyphBytes)].ToArray();
    }

    public ReadOnlyMemory<byte> Glyph(byte code)
    {
        if (code < FirstCode) throw new ArgumentOutOfRangeException(nameof(code));
        return packed.AsMemory((code - FirstCode) * GlyphBytes, GlyphBytes);
    }

    public bool TryGetCode(char text, out byte code)
    {
        code = 0;
        if (LanguageId == 4)
        {
            if (text is < (char)32 or > (char)126) return false;
            code = (byte)text; return true;
        }
        for (int value = FirstCode; value < 255; value++)
            if (MultakVietnameseText.TryDecodeGlyph((byte)value, out char candidate) && candidate == text)
            { code = (byte)value; return true; }
        return false;
    }

    public static MultakBitmapFont Parse(ReadOnlySpan<byte> resource, byte languageId)
    {
        long offset = BankOffset(resource, resource.Length, languageId);
        return new(languageId, resource.Slice((int)offset, BankBytes));
    }

    public static MultakBitmapFont? ReadIso(string path, DiscInventory inventory, byte languageId)
    {
        var file = inventory.Files.SingleOrDefault(f =>
            f.Name.TrimStart('/').Equals("FONT1.BIN", StringComparison.OrdinalIgnoreCase));
        if (file is null) return null;
        using var stream = File.OpenRead(path);
        if (file.Offset < 0 || file.Bytes < 2048 || file.Bytes > MaximumBytes ||
            file.Offset > stream.Length - file.Bytes)
            throw new InvalidDataException("Invalid FONT1 disc extent.");
        stream.Position = file.Offset;
        var header = new byte[2048]; stream.ReadExactly(header);
        long offset = BankOffset(header, file.Bytes, languageId);
        stream.Position = file.Offset + offset;
        var bank = new byte[BankBytes]; stream.ReadExactly(bank);
        return new(languageId, bank);
    }

    private static long BankOffset(ReadOnlySpan<byte> header, long length, byte languageId)
    {
        if (header.Length < 2048 || length is < 2048 or > MaximumBytes || languageId is not (4 or 7))
            throw new InvalidDataException("Unsupported FONT1 resource or language.");
        int bank = languageId == 4 ? 2 : 4;
        int metadata = Read24(header[72..]) - 1 + (bank - 1) * 8;
        if (metadata < 76 || metadata > 2048 - 8 || header[metadata] != Width ||
            header[metadata + 1] != Height || header[metadata + 2] != 4 ||
            header[metadata + 3] != (languageId == 4 ? 0 : 1))
            throw new InvalidDataException("Unsupported FONT1 glyph geometry.");
        long offset = Read24(header[(bank * 6)..]) * 2048L;
        if (Read24(header[(bank * 6 + 3)..]) * 256L != BankBytes ||
            offset < 2048 || offset > length - BankBytes)
            throw new InvalidDataException("FONT1 glyph bank lies outside the resource.");
        return offset;
    }

    private static int Read24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
}
