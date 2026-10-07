using System.Text.Json;
using Arirang.Core;

internal static class MasecoResearch
{
    internal static void Export(string input, string header, long datBytes, long da1Bytes, string output)
    {
        if (new FileInfo(input).Length > MasecoIndex.MaximumBytes || new FileInfo(header).Length > MultakIndex.MaximumHeaderBytes)
            throw new InvalidDataException("Research input exceeds limit.");
        var songs = MasecoIndex.Parse(File.ReadAllBytes(input));
        var table = MultakIndex.Parse(File.ReadAllBytes(header), datBytes, da1Bytes);
        var mapped = songs.Select(s => table.FindSong(s.DeviceCode)
            ?? throw new InvalidDataException($"Device code {s.DeviceCode} has no music pointer.")).ToArray();
        bool complete = mapped.Select(p => p.TableIndex).ToHashSet().SetEquals(table.Pointers.Select(p => p.TableIndex))
            && mapped.Select(p => p.TableIndex).Distinct().Count() == songs.Count;
        if (!complete) throw new InvalidDataException("Catalogue mapping is not one-to-one and complete.");
        int checkedHeaders = 0;
        foreach (int code in new[] { 30001, 30093, 50001 })
        {
            var song = songs.Single(s => s.DeviceCode == code);
            var sample = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, code + ".bin"));
            if (sample.Length != 1024 || !sample.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 0x4f, 0x4b }))
                throw new InvalidDataException("Unsupported verification song header.");
            byte mask = sample[38];
            int end = Array.IndexOf(sample, mask, 42);
            if (end < 42 || end > 554) throw new InvalidDataException("Invalid bounded title terminator.");
            var decoded = sample.AsSpan(42, end - 42).ToArray();
            for (int i = 0; i < decoded.Length; i++) decoded[i] ^= mask;
            int slash = Array.IndexOf(decoded, (byte)'/');
            var title = slash < 0 ? decoded : decoded[..slash];
            var catalogueTitle = song.TitleBytes;
            while (catalogueTitle.Length > 0 && catalogueTitle[^1] == 32) catalogueTitle = catalogueTitle[..^1];
            if (!title.SequenceEqual(catalogueTitle)) throw new InvalidDataException("Device code points to a different song title.");
            checkedHeaders++;
        }
        var report = new
        {
            catalogueRecords = songs.Count, englishTitles = songs.Count(s => s.EnglishTitle is not null),
            mappedMusicPointers = mapped.Length, uniqueMappedSlots = mapped.Select(p => p.TableIndex).Distinct().Count(),
            originalSongHeadersMatched = checkedHeaders, completeOneToOneMapping = complete,
            deviceCodeMappingVerified = true, vietnameseTextEncodingVerified = false,
            musicEventsDecoded = false, playbackVerified = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
