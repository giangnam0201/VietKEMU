using System.Buffers.Binary;
using System.Text.Json;
using Arirang.Core;

internal static class LayoutChecks
{
    internal static void Run()
    {
        var raw = new byte[140]; byte mask = 66;
        raw[2] = 0x4f; raw[3] = 0x4b; raw[38] = mask;
        "TEST/\0"u8.CopyTo(raw.AsSpan(42));
        for (int i = 42; i < 48; i++) raw[i] ^= mask;
        byte[] metadata = [0, 0, 23, 0, 0, 17, 1, 4, 0, 1, 0, 0, 0, 60];
        for (int i = 0; i < metadata.Length; i++) raw[48 + i] = (byte)(metadata[i] ^ mask);
        raw[98] = (byte)(0x1a ^ mask); raw[99] = (byte)(0xff ^ mask);
        var layout = MultakSongLayout.Parse(raw);
        if (layout.MusicOffset != 100 || layout.Tracks.Count != 1 || layout.Tracks[0].Channel != 0 ||
            !layout.TitleBytes.SequenceEqual("TEST/"u8.ToArray())) throw new Exception("MULTAK title, track table and boundary.");
        var badChannel = raw.ToArray(); badChannel[56] = (byte)(16 ^ mask);
        var badLayout = raw.ToArray(); badLayout[50] = (byte)(24 ^ mask);
        var badTrack = raw.ToArray(); badTrack[60] = (byte)(1 ^ mask);
        var badBoundary = raw.ToArray(); badBoundary[98] = mask;
        foreach (var invalid in new[] { raw[..99], badChannel, badLayout, badTrack, badBoundary })
        {
            bool rejected = false;
            try { MultakSongLayout.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Malformed original song layout must be rejected.");
        }
        var blocks = new byte[100 + 672]; raw.CopyTo(blocks, 0);
        for (int i = 100; i < blocks.Length; i++) blocks[i] = mask;
        byte[] firstBlock = [0x12, 0x34, 1, 0, 0, 0xff];
        byte[] lastBlock = [0x56, 0x8f, 0xff, 0x2f, 0xff, 0xff, 0xff];
        for (int i = 0; i < firstBlock.Length; i++) blocks[100 + i] = (byte)(firstBlock[i] ^ mask);
        for (int i = 0; i < lastBlock.Length; i++) blocks[436 + i] = (byte)(lastBlock[i] ^ mask);
        var streams = MultakSongStreams.Parse(blocks);
        if (streams.BlocksRead != 2 || streams.Channels[0].Segments != 2 ||
            !streams.Channels[0].Bytes.SequenceEqual(new byte[] { 0x12, 0x34, 0x56, 0x8f, 0xff, 0x2f }))
            throw new Exception("MULTAK block links must preserve exact compact stream bytes.");
        var backward = blocks.ToArray(); backward[102] = mask;
        var badOffset = blocks.ToArray(); badOffset[103] = (byte)(1 ^ mask); badOffset[104] = (byte)(80 ^ mask);
        foreach (var invalid in new[] { backward, badOffset, blocks[..^1] })
        {
            bool rejected = false;
            try { MultakSongStreams.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid MULTAK block references must be rejected.");
        }
        var header = new byte[MultakIndex.TableOffset + 8];
        "multak3.3"u8.CopyTo(header.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(334), 2);
        header[MultakIndex.TableOffset] = header[MultakIndex.TableOffset + 2] = header[MultakIndex.TableOffset + 3] = 255;
        var table = MultakIndex.Parse(header, 65536 + raw.Length, 0);
        string path = Path.Combine(Path.GetTempPath(), "arirang-record-" + Guid.NewGuid().ToString("N") + ".iso");
        try
        {
            var iso = new byte[512 + 65536 + raw.Length]; raw.CopyTo(iso, 512 + 65536); File.WriteAllBytes(path, iso);
            var inventory = new DiscInventory("ISO9660", [new DiscFile("MULTAK.DAT", 65536 + raw.Length, 512)]);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16 + 30 * 2), 0);
            table = MultakIndex.Parse(header, 65536 + raw.Length, 0);
            if (!table.ReadSongRecordIso(path, inventory, 30001).SequenceEqual(raw))
                throw new Exception("Original code mapping must read the exact bounded ISO record.");
            File.WriteAllBytes(path, iso[..^1]);
            bool rejected = false;
            try { table.ReadSongRecordIso(path, inventory, 30001); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Truncated ISO song extent must be rejected.");
        }
        finally { File.Delete(path); }
    }

    internal static void ExportOriginal(string first, string second, string output)
    {
        MultakSongLayout Read(string path)
        {
            if (new FileInfo(path).Length > MultakSongLayout.MaximumBytes) throw new InvalidDataException("Song sample exceeds limit.");
            return MultakSongLayout.Parse(File.ReadAllBytes(path));
        }
        var a = Read(first); var b = Read(second);
        var streamsA = MultakSongStreams.Parse(File.ReadAllBytes(first));
        var streamsB = MultakSongStreams.Parse(File.ReadAllBytes(second));
        if (a.Tracks.Count != 5 || b.Tracks.Count != 8 || a.MusicOffset != 1195 || b.MusicOffset != 772 ||
            !a.Tracks.Any(t => t.Channel == 9) || !b.Tracks.Any(t => t.Channel == 9))
            throw new InvalidDataException("Original Happy Birthday layout does not match independent inspection.");
        if (streamsA.BlocksRead != 9 || streamsB.BlocksRead != 6 ||
            streamsA.Channels.Count != 5 || streamsB.Channels.Count != 8 ||
            streamsA.Channels.Concat(streamsB.Channels).Any(c => !c.Bytes.AsSpan().EndsWith(new byte[] { 0x8f, 0xff, 0x2f })))
            throw new InvalidDataException("Original compact channel reassembly does not match independent block inspection.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            originalSongRecords = 2, firstSongCode = 30655, secondSongCode = 32153,
            firstRecordBytes = new FileInfo(first).Length, secondRecordBytes = new FileInfo(second).Length,
            firstChannelStreams = a.Tracks.Count, secondChannelStreams = b.Tracks.Count,
            firstMusicOffset = a.MusicOffset, secondMusicOffset = b.MusicOffset,
            percussionChannelsLocated = true, trackOffsetsVerified = true,
            firstBlocksRead = streamsA.BlocksRead, secondBlocksRead = streamsB.BlocksRead,
            compactStreamsReassembled = true, allChannelsReachedEndOfTrack = true,
            notesDecoded = false, timingDecoded = false, playbackVerified = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
