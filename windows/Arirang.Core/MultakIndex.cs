using System.Buffers.Binary;

namespace Arirang.Core;

// Song-block bases map device codes to table positions. Low flags are retained
// without guessing their meaning; a valid extent is not proof of playable MIDI.
public sealed record MultakPointer(int TableIndex, int StorageFile, long Offset, byte Flags);
public sealed record MultakIndex(int Slots, int NullSlots, IReadOnlyList<MultakPointer> Pointers,
    IReadOnlyList<int> SongBlockBases)
{
    public const int TableOffset = 0xd20;
    public const int MaximumHeaderBytes = TableOffset + ushort.MaxValue * 4;
    private readonly Dictionary<int, MultakPointer> bySlot = Pointers.ToDictionary(p => p.TableIndex);

    public MultakPointer? FindSong(int deviceCode)
    {
        if (deviceCode <= 0 || deviceCode / 1000 >= SongBlockBases.Count) return null;
        int blockBase = SongBlockBases[deviceCode / 1000];
        if (blockBase < 0) return null;
        int slot = blockBase + deviceCode % 1000;
        return bySlot.GetValueOrDefault(slot);
    }

    public byte[] ReadSongRecordIso(string path, DiscInventory inventory, int deviceCode)
    {
        var pointer = FindSong(deviceCode) ?? throw new InvalidDataException("Device song code has no music pointer.");
        string name = pointer.StorageFile == 0 ? "MULTAK.DAT" : "MULTAK.DA1";
        var file = inventory.Files.SingleOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Music storage file not found.");
        long end = Pointers.Where(p => p.StorageFile == pointer.StorageFile && p.Offset > pointer.Offset)
            .Select(p => p.Offset).DefaultIfEmpty(file.Bytes).Min();
        long size = end - pointer.Offset;
        if (pointer.Offset < 0 || end > file.Bytes || size is <= 0 or > MultakSongLayout.MaximumBytes)
            throw new InvalidDataException("Song record exceeds supported bounded size.");
        using var stream = File.OpenRead(path);
        long offset = checked(file.Offset + pointer.Offset);
        if (offset < 0 || offset > stream.Length - size)
            throw new InvalidDataException("Truncated music record extent.");
        stream.Position = offset;
        var raw = new byte[(int)size]; stream.ReadExactly(raw); return raw;
    }

    public static MultakIndex Parse(ReadOnlySpan<byte> header, long datBytes, long da1Bytes)
    {
        if (header.Length < 336 || !header.Slice(4, 9).SequenceEqual("multak3.3"u8))
            throw new InvalidDataException("Unsupported MULTAK header; music decoding is not established for this disc.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(334, 2));
        if (count == 0 || header.Length < TableOffset + count * 4)
            throw new InvalidDataException("Truncated MULTAK pointer table.");
        var pointers = new List<MultakPointer>(count);
        int nulls = 0;
        for (int i = 0; i < count; i++)
        {
            var pointer = header.Slice(TableOffset + i * 4, 4);
            if (pointer.SequenceEqual(new byte[] { 0xff, 0, 0xff, 0xff })) { nulls++; continue; }
            int storage = pointer[3] >> 4;
            if (storage > 1 || pointer[1] >= 60 || pointer[2] >= 75)
                throw new InvalidDataException($"Unsupported MULTAK pointer at table slot {i}.");
            long sector = (pointer[0] * 60L + pointer[1]) * 75 + pointer[2];
            long offset = sector * 2048 + (storage == 0 ? 65536 : 0);
            long length = storage == 0 ? datBytes : da1Bytes;
            if (offset < 0 || length < 0 || offset > length - 2)
                throw new InvalidDataException($"MULTAK pointer {i} extends beyond its storage file.");
            pointers.Add(new(i, storage, offset, (byte)(pointer[3] & 15)));
        }
        var bases = new List<int>();
        for (int offset = 16; offset < 334; offset += 2)
        {
            int value = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(offset, 2));
            bases.Add(value == ushort.MaxValue ? -1 : value);
        }
        return new(count, nulls, pointers, bases);
    }

    public static MultakIndex ReadIso(string path, DiscInventory inventory)
    {
        var dat = inventory.Files.SingleOrDefault(f => f.Name.Equals("MULTAK.DAT", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Root MULTAK.DAT not found.");
        var da1 = inventory.Files.SingleOrDefault(f => f.Name.Equals("MULTAK.DA1", StringComparison.OrdinalIgnoreCase));
        using var stream = File.OpenRead(path);
        int bytes = checked((int)Math.Min(dat.Bytes, MaximumHeaderBytes));
        if (dat.Offset < 0 || dat.Offset > stream.Length - bytes)
            throw new InvalidDataException("Truncated MULTAK extent.");
        stream.Position = dat.Offset;
        var header = new byte[bytes]; stream.ReadExactly(header);
        return Parse(header, dat.Bytes, da1?.Bytes ?? 0);
    }
}
