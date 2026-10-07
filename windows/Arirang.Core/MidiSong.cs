using System.Buffers.Binary;
using System.Text;

namespace Arirang.Core;

public sealed record MidiMessage(double Seconds, uint Packed, byte[]? SystemExclusive = null);
public sealed record LyricCue(double Seconds, string Text, bool NewLine, byte Voice = 0, byte? OriginalGlyphCode = null);
public sealed record MidiSong(string Title, double Duration, IReadOnlyList<MidiMessage> Messages,
    IReadOnlyList<LyricCue> Lyrics, string? Notice = null)
{
    public MultakBitmapFont? LyricFont { get; init; }
    private sealed record Raw(long Tick, int Order, uint Packed = 0, int Tempo = 0,
        byte[]? Text = null, int TextType = 0, byte[]? SystemExclusive = null);

    public static MidiSong Read(string path) => Parse(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    public static MidiSong Parse(byte[] bytes, string fallbackTitle)
    {
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("MIDI file exceeds 64 MiB.");
        var reader = new Reader(bytes);
        if (reader.Ascii(4) != "MThd") throw new InvalidDataException("Not a standard MIDI/KAR file.");
        var headerLength = reader.U32();
        if (headerLength < 6 || headerLength > int.MaxValue) throw new InvalidDataException("Invalid MIDI header.");
        int format = reader.U16(), tracks = reader.U16(), division = reader.U16();
        if (format > 1) throw new InvalidDataException("Independent format-2 MIDI tracks are not supported.");
        if (tracks == 0 || tracks > 1024 || (format == 0 && tracks != 1) || division == 0)
            throw new InvalidDataException("Invalid MIDI tracks or timing.");
        reader.Take((int)headerLength - 6);
        var raw = new List<Raw>();
        int order = 0;
        long finalTick = 0;
        for (int track = 0; track < tracks; track++)
        {
            if (reader.Ascii(4) != "MTrk") throw new InvalidDataException("Missing MIDI track.");
            uint length = reader.U32();
            if (length > int.MaxValue) throw new InvalidDataException("Invalid track size.");
            var data = new Reader(reader.Take((int)length));
            long tick = 0;
            int running = 0;
            while (data.Remaining > 0)
            {
                tick = checked(tick + data.Variable());
                int status = data.Byte();
                int first = -1;
                if (status < 0x80)
                {
                    if (running == 0) throw new InvalidDataException("Missing MIDI running status.");
                    first = status;
                    status = running;
                }
                if (status < 0xf0)
                {
                    running = status;
                    int a = first < 0 ? data.DataByte() : first;
                    int b = (status & 0xf0) is 0xc0 or 0xd0 ? 0 : data.DataByte();
                    raw.Add(new(tick, order++, (uint)(status | a << 8 | b << 16)));
                }
                else
                {
                    if (status == 0xff)
                    {
                        int type = data.Byte();
                        byte[] payload = data.Take(data.Variable());
                        if (type == 0x51)
                        {
                            if (payload.Length != 3) throw new InvalidDataException("Invalid tempo event.");
                            int tempo = payload[0] << 16 | payload[1] << 8 | payload[2];
                            if (tempo == 0) throw new InvalidDataException("Invalid zero tempo.");
                            raw.Add(new(tick, order++, Tempo: tempo));
                        }
                        else if (type is 1 or 3 or 5)
                            raw.Add(new(tick, order++, Text: payload, TextType: type));
                        else if (type == 0x2f)
                        {
                            if (payload.Length != 0) throw new InvalidDataException("Invalid end-of-track event.");
                            break;
                        }
                    }
                    else if (status is 0xf0 or 0xf7)
                    {
                        running = 0;
                        byte[] payload = data.Take(data.Variable());
                        // F7 is an SMF escape/continuation packet; preserve its bytes.
                        byte[] packet = status == 0xf0 ? new byte[] { 0xf0 }.Concat(payload).ToArray() : payload;
                        raw.Add(new(tick, order++, SystemExclusive: packet));
                    }
                    else throw new InvalidDataException("Unsupported MIDI system event.");
                }
                if (raw.Count > 2_000_000) throw new InvalidDataException("Too many MIDI events.");
            }
            finalTick = Math.Max(finalTick, tick);
        }
        bool smpte = (division & 0x8000) != 0;
        double ticksPerSecond = 0;
        if (smpte)
        {
            int fps = -(sbyte)(division >> 8), subframes = division & 255;
            if (fps is not (24 or 25 or 29 or 30) || subframes == 0) throw new InvalidDataException("Invalid SMPTE timing.");
            ticksPerSecond = (fps == 29 ? 30000d / 1001 : fps) * subframes;
        }
        int currentTempo = 500000;
        long previousTick = 0;
        double seconds = 0;
        string title = fallbackTitle;
        var messages = new List<MidiMessage>();
        var lyrics = new List<LyricCue>();
        bool hasLyrics = raw.Any(e => e.TextType == 5);
        foreach (var ev in raw.OrderBy(e => e.Tick).ThenBy(e => e.Order))
        {
            seconds += (ev.Tick - previousTick) * (smpte ? 1 / ticksPerSecond : currentTempo / (division * 1_000_000d));
            previousTick = ev.Tick;
            if (ev.Tempo > 0) currentTempo = ev.Tempo;
            if (ev.Packed != 0 || ev.SystemExclusive is not null)
                messages.Add(new(seconds, ev.Packed, ev.SystemExclusive));
            if (ev.Text is not null)
            {
                string text = Decode(ev.Text);
                if (text.StartsWith("@T") && title == fallbackTitle) title = text[2..].Trim();
                else if (ev.TextType == 3 && title == fallbackTitle && !string.IsNullOrWhiteSpace(text)) title = text.Trim();
                if ((ev.TextType == 5 || (!hasLyrics && ev.TextType == 1)) && !text.StartsWith('@') && text.Length > 0)
                {
                    bool newLine = text[0] is '/' or '\\' or '\r' or '\n';
                    lyrics.Add(new(seconds, text.TrimStart('/', '\\').Replace("\r", "").Replace("\n", ""), newLine));
                }
            }
        }
        seconds += (finalTick - previousTick) * (smpte ? 1 / ticksPerSecond : currentTempo / (division * 1_000_000d));
        return new(title, seconds, messages, lyrics);
    }

    private static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private sealed class Reader(byte[] data)
    {
        private int position;
        public int Remaining => data.Length - position;
        public byte Byte() => Take(1)[0];
        public int DataByte()
        {
            int value = Byte();
            if (value >= 128) throw new InvalidDataException("Invalid MIDI data byte.");
            return value;
        }
        public byte[] Take(int count)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException("Truncated MIDI data.");
            var result = data.AsSpan(position, count).ToArray(); position += count; return result;
        }
        public string Ascii(int count) => Encoding.ASCII.GetString(Take(count));
        public int U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public int Variable()
        {
            int result = 0;
            for (int n = 0; n < 4; n++)
            {
                int value = Byte(); result = result << 7 | value & 127;
                if ((value & 128) == 0) return result;
            }
            throw new InvalidDataException("Invalid MIDI variable-length value.");
        }
    }
}
