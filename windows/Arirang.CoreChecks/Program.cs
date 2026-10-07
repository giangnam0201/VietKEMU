using Arirang.Core;
using System.Buffers.Binary;
using System.Text;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static byte[] Chunk(string type, byte[] bytes)
{
    var result = new byte[bytes.Length + 8]; Encoding.ASCII.GetBytes(type).CopyTo(result, 0);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), bytes.Length); bytes.CopyTo(result, 8); return result;
}
static byte[] Midi(params byte[][] tracks)
{
    byte[] header = [0, (byte)(tracks.Length == 1 ? 0 : 1), 0, (byte)tracks.Length, 1, 0xe0];
    return Chunk("MThd", header).Concat(tracks.SelectMany(t => Chunk("MTrk", t))).ToArray();
}
byte[] tempo = [0, 0xff, 0x51, 3, 7, 0xa1, 0x20, 0x83, 0x60, 0xff, 0x51, 3, 0x0f, 0x42, 0x40, 0x83, 0x60, 0xff, 0x2f, 0];
byte[] notes = [0, 0xc0, 0, 0, 0x90, 60, 100, 0x83, 0x60, 60, 0, 0, 0x90, 64, 100, 0x83, 0x60, 64, 0, 0, 0xff, 0x2f, 0];
byte[] lyrics = [0, 0xff, 5, 3, (byte)'/', (byte)'L', (byte)'a', 0x83, 0x60, 0xff, 5, 3, (byte)' ', (byte)'l', (byte)'a', 0x83, 0x60, 0xff, 0x2f, 0];
var fixture = Midi(tempo, notes, lyrics);
var song = MidiSong.Parse(fixture, "Synthetic timing fixture");
Check(Math.Abs(song.Duration - 1.5) < .00001, "Multi-track tempo map must use 0.5 s + 1 s.");
Check(song.Messages.Count == 5 && Math.Abs(song.Messages[2].Seconds - .5) < .00001, "Running status / merged notes.");
Check(song.Lyrics.Count == 2 && song.Lyrics[0].NewLine && song.Lyrics[1].Seconds == .5, "Timed KAR lyrics.");
Check((MidiPlayback.Transform(0x643c99, 4, 100) >> 8 & 127) == 60, "Drums must not transpose.");
Check((MidiPlayback.Transform(0x643c90, 4, 100) >> 8 & 127) == 64, "Melody transposition.");
Check((MidiPlayback.Transform(0x6407b0, 0, 50) >> 16 & 127) == 50, "Channel gain scaling.");
foreach (var invalid in new[] { fixture[..^2], Midi([0, 60, 100]), Midi([0x81, 0x81, 0x81, 0x81, 0]) })
{
    bool rejected = false;
    try { MidiSong.Parse(invalid, "invalid"); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Malformed/truncated MIDI must fail safely.");
}
var output = new RecordingOutput();
using (var player = new MidiPlayback(output))
{
    player.Load(song); player.Play(); await Task.Delay(180); player.Pause();
    double paused = player.Position; await Task.Delay(80);
    Check(Math.Abs(paused - player.Position) < .01 && output.Messages.Any(m => (m & 0xf0) == 0x90), "Pause freezes clock after sending notes.");
    player.Seek(.7); player.SetKey(2); player.Play();
    Check(output.Messages.Any(m => (m & 0xff) == 0x90 && (m >> 8 & 127) == 66), "Seek/resume restores held note with transposition.");
    player.SetSpeed(2); player.Seek(1.4); await Task.Delay(200);
    Check(!player.Playing && player.Position == song.Duration, "Completion silences output and stops clock.");
}
string temporary = Path.Combine(Path.GetTempPath(), "arirang-disc-check-" + Guid.NewGuid().ToString("N") + ".iso");
try
{
    byte[] iso = new byte[24 * 2048];
    byte[] Record(uint sector, uint size, byte flags, byte[] name)
    {
        var entry = new byte[33 + name.Length + (name.Length % 2 == 0 ? 1 : 0)]; entry[0] = (byte)entry.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(2), sector); BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(10), size);
        entry[25] = flags; entry[32] = (byte)name.Length; name.CopyTo(entry, 33); return entry;
    }
    int pvd = 16 * 2048; iso[pvd] = 1; "CD001"u8.CopyTo(iso.AsSpan(pvd + 1)); iso[pvd + 6] = 1;
    Record(20, 2048, 2, [0]).CopyTo(iso, pvd + 156);
    Record(21, 12, 0, "SONG.MID;1"u8.ToArray()).CopyTo(iso, 20 * 2048);
    File.WriteAllBytes(temporary, iso);
    var inventory = DiscInventory.Read(temporary);
    Check(inventory.Files.Single().Name == "SONG.MID" && inventory.Files.Single().Offset == 21 * 2048, "ISO directory extent / version suffix.");
    File.WriteAllBytes(temporary, "Rar!\u001a\u0007\u0001\0"u8.ToArray());
    Check(DiscInventory.Read(temporary).Format.StartsWith("RAR"), "Mislabeled RAR is not treated as ISO.");
}
finally { File.Delete(temporary); }
if (args.Length == 2 && args[0] == "--fixture")
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!); File.WriteAllBytes(args[1], fixture);
}
Console.WriteLine("PASS: MIDI tempo, running status, lyrics, transpose, gain, malformed input, pause/seek/completion, ISO inventory and RAR detection.");

sealed class RecordingOutput : IMidiOutput
{
    public System.Collections.Concurrent.ConcurrentQueue<uint> Messages { get; } = new();
    public void Send(uint message) => Messages.Enqueue(message);
    public void SendSystemExclusive(byte[] packet) { }
    public void Reset() { }
    public void Dispose() { }
}
