namespace Arirang.Core;

public sealed record DiscFile(string Name, long Bytes, long Offset);
public sealed record DiscInventory(string Format, IReadOnlyList<DiscFile> Files)
{
    public static DiscInventory Read(string path)
    {
        using var stream = File.OpenRead(path);
        var header = ReadAt(stream, 0, (int)Math.Min(8, stream.Length));
        if (header.AsSpan().StartsWith("Rar!"u8)) return new("RAR archive (not an ISO image)", []);
        if (header.AsSpan().StartsWith("MThd"u8)) return new("Standard MIDI", []);
        if (stream.Length < 17 * 2048) throw new InvalidDataException("Disc image is too small.");
        byte[]? primary = null;
        for (int sector = 16; sector < 64; sector++)
        {
            var descriptor = ReadAt(stream, sector * 2048L, 2048);
            if (!descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8)) break;
            if (descriptor[0] == 1) { primary = descriptor; break; }
            if (descriptor[0] == 255) break;
        }
        if (primary is null) throw new InvalidDataException("ISO9660 filesystem not found. UDF-only discs are not supported yet.");
        var files = new List<DiscFile>();
        var visited = new HashSet<(long, int)>();
        void Walk(byte[] record, string prefix, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Disc directory nesting exceeds limit.");
            long offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(2, 4)) * 2048L;
            uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(10, 4));
            if (size > 8 * 1024 * 1024) throw new InvalidDataException("Disc directory exceeds limit.");
            if (!visited.Add((offset, (int)size))) return;
            var bytes = ReadAt(stream, offset, (int)size);
            for (int at = 0; at < bytes.Length;)
            {
                int length = bytes[at];
                if (length == 0) { at = (at / 2048 + 1) * 2048; continue; }
                if (length < 34 || at + length > bytes.Length) throw new InvalidDataException("Invalid ISO directory record.");
                var entry = bytes.AsSpan(at, length).ToArray(); at += length;
                int nameLength = entry[32];
                if (33 + nameLength > length) throw new InvalidDataException("Invalid ISO filename.");
                if (nameLength == 1 && entry[33] is 0 or 1) continue;
                string name = System.Text.Encoding.ASCII.GetString(entry, 33, nameLength).Split(';')[0];
                if (name is "." or ".." || name.Contains('/') || name.Contains('\\')) throw new InvalidDataException("Invalid ISO path.");
                string fullName = prefix + name;
                if ((entry[25] & 2) != 0) Walk(entry, fullName + "/", depth + 1);
                else
                {
                    long fileOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(2, 4)) * 2048L;
                    long fileSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(10, 4));
                    if (fileOffset + fileSize > stream.Length) throw new InvalidDataException("Disc file extends beyond image.");
                    files.Add(new(fullName, fileSize, fileOffset));
                    if (files.Count > 100_000) throw new InvalidDataException("Too many disc files.");
                }
            }
        }
        Walk(primary.AsSpan(156, primary[156]).ToArray(), "", 0);
        return new("ISO9660", files);
    }

    private static byte[] ReadAt(Stream stream, long offset, int length)
    {
        if (offset < 0 || offset > stream.Length || length > stream.Length - offset) throw new InvalidDataException("Truncated disc image.");
        stream.Position = offset;
        var bytes = new byte[length]; stream.ReadExactly(bytes); return bytes;
    }
}
