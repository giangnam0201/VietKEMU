namespace Arirang.Core;

// Unicode identities checked against the Volume 40 FONT1 Vietnamese bitmap
// bank (sector 699, 24x48, two bits per pixel, code origin 0x20).
// This table contains character identities only, not the original font asset.
public static class MultakVietnameseText
{
    private const string Extended =
        "ẤẻéâẽàẹẦêếèềẨìểễ" + // 80..8F
        "Ẫỏõôọòốùồổỗộủũụư" + // 90..9F
        "áíóúứừửữựỉĩịệđĐẬ" + // A0..AF
        "ẮẰẲẴÉÈẺẼẸÊỀỂỄỆÍÌ" + // B0..BF
        "ỈĨỊÓÒỎÕỌỐỒỔỖỘỚỜỞ" + // C0..CF
        "ỠỢÚÙỦŨỤỨỪỬỮỰÝỲỶỸ" + // D0..DF
        "ảãạấầẩẫậăắằẳẵặýỳ" + // E0..EF
        "ỷỹỵơớờởỡợÔƠƯĂÂÊ";   // F0..FE

    public static string? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 512) return null;
        var text = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte value = bytes[i];
            // 7F is a placeholder glyph; FF is blank in this bank. Neither
            // supplies a reliable character identity. Reject control bytes.
            if (value is < 32 or 127 or 255) return null;
            text[i] = value switch
            {
                0x5e => 'Á', 0x60 => 'À',
                0x7b => 'Ặ', 0x7c => 'Ả', 0x7d => 'Ã', 0x7e => 'Ạ',
                >= 0x80 => Extended[value - 0x80],
                _ => (char)value
            };
        }
        return new string(text).TrimEnd(' ');
    }
}
