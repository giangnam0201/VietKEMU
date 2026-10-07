using System.Text.Json;
using Arirang.Core;

internal static class MultakResearch
{
    internal static void Export(string input, long datBytes, long da1Bytes, string output)
    {
        using var stream = File.OpenRead(input);
        if (stream.Length > MultakIndex.MaximumHeaderBytes)
            throw new InvalidDataException("Expected a bounded header sample.");
        var header = new byte[checked((int)stream.Length)]; stream.ReadExactly(header);
        var index = MultakIndex.Parse(header, datBytes, da1Bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            slots = index.Slots, nullSlots = index.NullSlots,
            datPointers = index.Pointers.Count(p => p.StorageFile == 0),
            da1Pointers = index.Pointers.Count(p => p.StorageFile == 1),
            first = index.Pointers.First(), last = index.Pointers.Last(),
            songNumbersVerified = false, musicEventsDecoded = false, playbackVerified = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
