using System.Text;

namespace Arirang.Core;

public sealed record DiscSongMetadata(int RecordOffset, string Title, string Artist, string Composer)
{
    // INFO.DAT text records alone do not establish the device's song number or
    // the corresponding music-container extent. Never invent those values.
    public int? SongNumber => null;
    public bool PlaybackVerified => false;
}

public static class InfoDatIndex
{
    public static IReadOnlyList<DiscSongMetadata> ReadEnglish(byte[] bytes)
    {
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("INFO.DAT exceeds 32 MiB.");
        var best = new List<DiscSongMetadata>();
        for (int candidate = 0; candidate + 8 < bytes.Length; candidate++)
        {
            if (bytes[candidate] != 0 || bytes[candidate + 1] != 12 || bytes[candidate + 6] != 0) continue;
            var block = new List<DiscSongMetadata>();
            int at = candidate;
            try
            {
                while (at + 8 <= bytes.Length && bytes[at] == 0 && bytes[at + 1] == 12 && bytes[at + 6] == 0)
                {
                    int record = at; at += 7;
                    string title = Field(bytes, ref at, ascii: true);
                    if (title.Length == 0) break;
                    string artist = Field(bytes, ref at, ascii: false);
                    if (at + 2 > bytes.Length) break;
                    at += 2;
                    string composer = Field(bytes, ref at, ascii: false);
                    block.Add(new(record, title, artist, composer));
                    if (block.Count > 100_000) throw new InvalidDataException("Too many index records.");
                }
            }
            catch (InvalidDataException) { /* Candidate ends at an invalid record; try later candidates. */ }
            if (block.Count < 16) continue;
            if (block.Count > best.Count) best = block;
            candidate = Math.Max(candidate, at - 1);
        }
        if (best.Count == 0) throw new InvalidDataException("No validated English INFO.DAT record block found. Other disc/index versions need separate decoding.");
        return best;
    }

    private static string Field(byte[] bytes, ref int at, bool ascii)
    {
        if (at >= bytes.Length) throw new InvalidDataException("Truncated index field.");
        int size = bytes[at++];
        if (size > bytes.Length - at) throw new InvalidDataException("Truncated index text.");
        var text = bytes.AsSpan(at, size);
        foreach (byte b in text)
            if (ascii && (b < 32 || b > 126)) throw new InvalidDataException("Invalid index title.");
        at += size;
        // One real Volume 48 artist field contains a control byte. It is not a
        // record delimiter; retain the length-prefixed record and display a space.
        return new string(Encoding.Latin1.GetString(text).Select(c => c < 32 ? ' ' : c).ToArray());
    }
}
