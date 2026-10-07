namespace Arirang.Core;

public sealed record MultakCompactStream(MultakTrackLayout Track, byte[] Bytes, int Segments);

// Reassembles compact bytes only. Notes, tempo and instruments require a decoder.
public sealed record MultakSongStreams(IReadOnlyList<MultakCompactStream> Channels, int BlocksRead)
{
    public static MultakSongStreams Parse(ReadOnlySpan<byte> raw)
    {
        var layout = MultakSongLayout.Parse(raw);
        const int blockSize = 336;
        byte mask = raw[38];
        int availableBlocks = (raw.Length - layout.MusicOffset) / blockSize;
        if (availableBlocks < 1 || layout.Tracks.Any(t => t.UnknownFormat != 1 || t.RelativeOffset >= blockSize))
            throw new InvalidDataException("Unsupported MULTAK initial block layout.");
        var targets = layout.Tracks.Select(t => (Block: 0, Offset: t.RelativeOffset)).ToArray();
        var pending = Enumerable.Repeat(true, targets.Length).ToArray();
        var buffers = layout.Tracks.Select(_ => new MemoryStream()).ToArray();
        var segments = new int[targets.Length];
        int blocksRead = 0, totalBytes = 0;
        try
        {
            while (pending.Any(p => p))
            {
                int block = Enumerable.Range(0, targets.Length).Where(i => pending[i]).Min(i => targets[i].Block);
                if (block >= availableBlocks || block > 4095 || ++blocksRead > availableBlocks)
                    throw new InvalidDataException("MULTAK block reference exceeds the bounded record.");
                var active = Enumerable.Range(0, targets.Length).Where(i => pending[i] && targets[i].Block == block)
                    .OrderBy(i => targets[i].Offset).ToArray();
                var decoded = raw.Slice(layout.MusicOffset + block * blockSize, blockSize).ToArray();
                for (int i = 0; i < decoded.Length; i++) decoded[i] ^= mask;
                int marker = decoded.AsSpan().LastIndexOf((byte)255);
                if (marker < 0) throw new InvalidDataException("MULTAK block has no end marker.");
                bool finalMarker = marker >= 5 && decoded.AsSpan(marker - 5, 6)
                    .SequenceEqual(new byte[] { 0x8f, 0xff, 0x2f, 0xff, 0xff, 0xff });
                int lastEnd = finalMarker ? marker + 1 : marker;
                for (int at = 0; at < active.Length; at++)
                {
                    int track = active[at], start = targets[track].Offset;
                    int end = at + 1 < active.Length ? targets[active[at + 1]].Offset : lastEnd;
                    if (start < 0 || start >= end - 3 || end > blockSize)
                        throw new InvalidDataException("MULTAK channel segments overlap or are truncated.");
                    var body = decoded.AsSpan(start, end - start - 3);
                    totalBytes = checked(totalBytes + body.Length);
                    if (totalBytes > MultakSongLayout.MaximumBytes)
                        throw new InvalidDataException("Expanded MULTAK streams exceed the bounded record limit.");
                    buffers[track].Write(body); segments[track]++;
                    if (body.EndsWith(new byte[] { 0x8f, 0xff, 0x2f }))
                    {
                        pending[track] = false;
                        continue;
                    }
                    int nextBlock = decoded[end - 3] | (decoded[end - 2] >> 4) << 8;
                    int nextOffset = (decoded[end - 2] & 15) << 8 | decoded[end - 1];
                    if (nextBlock <= block || nextBlock >= availableBlocks || nextOffset >= blockSize)
                        throw new InvalidDataException("MULTAK channel reference is backward or outside the record.");
                    targets[track] = (nextBlock, nextOffset);
                }
            }
            return new(Enumerable.Range(0, buffers.Length).Select(i =>
                new MultakCompactStream(layout.Tracks[i], buffers[i].ToArray(), segments[i])).ToArray(), blocksRead);
        }
        finally { foreach (var buffer in buffers) buffer.Dispose(); }
    }
}
