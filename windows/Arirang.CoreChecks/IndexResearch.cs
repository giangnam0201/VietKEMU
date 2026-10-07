using System.Text.Json;
using Arirang.Core;

internal static class IndexResearch
{
    internal static void Export(string input, string output)
    {
        var songs = InfoDatIndex.ReadEnglish(File.ReadAllBytes(input));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            sourceFile = Path.GetFileName(input), language = "English", count = songs.Count,
            songIdsVerified = false, playbackVerified = false, songs
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Read {songs.Count} English records; song IDs and music playback remain unverified.");
    }
}
