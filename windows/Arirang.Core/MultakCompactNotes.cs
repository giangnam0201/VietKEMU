namespace Arirang.Core;

public sealed record MultakNoteEvent(long Tick, byte Status, byte Data1, byte? Data2);
public sealed record MultakOpaqueCommand(long Tick, byte Command, byte[] Parameters);
// Ticks and opaque commands are preserved; this does not assign seconds or synthesize audio.
public sealed record MultakCompactNotes(IReadOnlyList<MultakNoteEvent> Events,
    IReadOnlyList<MultakOpaqueCommand> Commands, long EndTick)
{
    public static MultakCompactNotes Parse(MultakCompactStream stream)
    {
        var track = stream.Track;
        byte[] body = stream.Bytes;
        if (body.Length is < 4 or > MultakSongLayout.MaximumBytes || track.Channel > 15 ||
            track.PitchBits is < 2 or > 8 || track.BasePitch > 127)
            throw new InvalidDataException("Invalid compact MULTAK channel.");
        int position = 0;
        byte Read()
        {
            if (position >= body.Length) throw new InvalidDataException("Truncated compact MULTAK command.");
            return body[position++];
        }
        int ReadTime()
        {
            byte first = Read();
            return first < 128 ? first : (first & 127) << 8 | Read();
        }
        var events = new List<MultakNoteEvent>();
        var commands = new List<MultakOpaqueCommand>();
        var recent = new byte[4];
        long tick = ReadTime();
        byte status = (byte)(0xb0 | track.Channel);
        for (int iterations = 0; iterations < 2_000_000; iterations++)
        {
            byte value = Read();
            int? embeddedDelay = null;
            if (value == 0x80)
            {
                byte command = Read();
                int count = command switch { 0 or 255 => 0, 2 => 2, 4 => 3, _ => 1 };
                var parameters = new byte[count];
                for (int i = 0; i < count; i++) parameters[i] = Read();
                commands.Add(new(tick, command, parameters));
            }
            else if (value == 0x8f)
            {
                if (Read() != 0xff || Read() != 0x2f || position != body.Length)
                    throw new InvalidDataException("Unsupported MULTAK system command or trailing channel data.");
                return new(events, commands, tick);
            }
            else
            {
                if (value < 0x80) status = (byte)(0x90 | track.Channel);
                else if (value <= 0x8f)
                {
                    status = (byte)((value << 4 & 0xf0) | track.Channel);
                    value = (byte)(value << 4);
                }
                else if (value <= 0xbf) value = Read(); // Reuse current status.
                else status = (byte)(0x80 | track.Channel);
                switch (status >> 4)
                {
                    case 9:
                        int word = value << 8 | Read();
                        int pitch = track.BasePitch + (word >> 5 & (1 << track.PitchBits) - 1);
                        if (pitch > 127) throw new InvalidDataException("Expanded MULTAK pitch exceeds MIDI range.");
                        byte velocity = (byte)((word & 31) << 2);
                        for (int i = 3; i > 0; i--) recent[i] = recent[i - 1];
                        recent[0] = (byte)pitch;
                        events.Add(new(tick, status, (byte)pitch, velocity));
                        int delay = word >> (5 + track.PitchBits) & (1 << (10 - track.PitchBits)) - 1;
                        if (delay != 0) embeddedDelay = delay - 1;
                        if (track.Channel == 9) events.Add(new(tick, status, (byte)pitch, 0));
                        break;
                    case 8:
                        events.Add(new(tick, status, recent[value & 3], 64));
                        int offDelay = (value & 0x3c) >> 2;
                        if (offDelay != 0) embeddedDelay = offDelay - 1;
                        break;
                    case 10:
                    case 11:
                        byte first = Read(), second = Read();
                        if (first > 127) throw new InvalidDataException("Invalid MULTAK controller data.");
                        events.Add(new(tick, status, first, (byte)(second & 127)));
                        if (second >= 128) embeddedDelay = 0;
                        break;
                    case 12:
                    case 13:
                        byte data = Read();
                        events.Add(new(tick, status, (byte)(data & 127), null));
                        if (data >= 128) embeddedDelay = 0;
                        break;
                    case 14:
                        byte bend = Read();
                        events.Add(new(tick, status, 0, (byte)(bend & 127)));
                        if (bend >= 128) embeddedDelay = 0;
                        break;
                    default: throw new InvalidDataException("Unsupported compact MULTAK status.");
                }
            }
            tick = checked(tick + (embeddedDelay ?? ReadTime()));
            if (tick > int.MaxValue) throw new InvalidDataException("MULTAK tick limit exceeded.");
        }
        throw new InvalidDataException("MULTAK event count limit exceeded.");
    }
}
