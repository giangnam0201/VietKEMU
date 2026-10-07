using Arirang.Core;
using System.Buffers.Binary;
using System.Text;

if (args.Length == 3 && args[0] == "--index")
{
    IndexResearch.Export(args[1], args[2]); return;
}
if (args.Length == 5 && args[0] == "--multak")
{
    MultakResearch.Export(args[1], long.Parse(args[2]), long.Parse(args[3]), args[4]); return;
}
if (args.Length == 6 && args[0] == "--maseco")
{
    MasecoResearch.Export(args[1], args[2], long.Parse(args[3]), long.Parse(args[4]), args[5]); return;
}

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
byte[] multakHeader = new byte[MultakIndex.TableOffset + 12];
"multak3.3"u8.CopyTo(multakHeader.AsSpan(4));
BinaryPrimitives.WriteUInt16LittleEndian(multakHeader.AsSpan(334), 3);
new byte[] { 0xff, 0, 0xff, 0xff, 0x49, 0x31, 0x2b, 0x09, 0, 3, 0x17, 0x10 }.CopyTo(multakHeader, MultakIndex.TableOffset);
var multak = MultakIndex.Parse(multakHeader, 700_000_000, 600_000);
Check(multak.Slots == 3 && multak.NullSlots == 1 && multak.Pointers.Count == 2, "MULTAK null slots and pointer count.");
Check(multak.Pointers[0].Offset == 680448000 && multak.Pointers[0].Flags == 9, "MULTAK DAT sector calculation preserves flags.");
Check(multak.Pointers[1].Offset == 507904 && multak.Pointers[1].StorageFile == 1, "MULTAK DA1 uses no DAT base offset.");
var badSelector = multakHeader.ToArray(); badSelector[MultakIndex.TableOffset + 7] = 0x29;
var badSector = multakHeader.ToArray(); badSector[MultakIndex.TableOffset + 5] = 60;
foreach (var invalid in new[] { multakHeader[..^1], badSelector, badSector })
{
    bool rejected = false;
    try { MultakIndex.Parse(invalid, 700_000_000, 600_000); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "Truncated/unsupported MULTAK pointers must not be accepted.");
}
bool outsideRejected = false;
try { MultakIndex.Parse(multakHeader, 500, 600_000); } catch (InvalidDataException) { outsideRejected = true; }
Check(outsideRejected, "MULTAK extents must fit the declared storage file.");
MasecoChecks.Run();
LayoutChecks.Run();
if (args.Length == 4 && args[0] == "--layouts")
{
    LayoutChecks.ExportOriginal(args[1], args[2], args[3]); return;
}
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
var acrossMeta = MidiSong.Parse(Midi([0, 0x90, 60, 100, 0, 0xff, 1, 1, (byte)'a', 0, 60, 0, 0, 0xff, 0x2f, 0]), "running");
Check(acrossMeta.Messages.Count == 2, "Meta events do not cancel SMF running status.");
var smpte = fixture.ToArray(); smpte[12] = 0xe7; smpte[13] = 40;
Check(Math.Abs(MidiSong.Parse(smpte, "SMPTE").Duration - .96) < .00001, "SMPTE timing ignores tempo map.");
var exclusive = MidiSong.Parse(Midi([0, 0xf0, 5, 0x7e, 0x7f, 9, 1, 0xf7, 0, 0xff, 0x2f, 0]), "GM reset");
Check(exclusive.Messages[0].SystemExclusive!.SequenceEqual(new byte[] { 0xf0, 0x7e, 0x7f, 9, 1, 0xf7 }), "Preserve full system-exclusive packet.");
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
    int beforeGain = output.Messages.Count(m => (m & 0xf0) == 0x90);
    player.SetVolume(30);
    Check(output.Messages.Count(m => (m & 0xf0) == 0x90) == beforeGain, "Gain change must not retrigger held notes.");
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
var index = new List<byte>(new byte[100]);
for (int n = 0; n < 20; n++)
{
    byte[] title = Encoding.ASCII.GetBytes("SONG " + n);
    index.AddRange(new byte[] { 0, 12, 1, 0, 1, 5, 0, (byte)title.Length });
    index.AddRange(title);
    if (n == 0) index.AddRange(new byte[] { 9, (byte)'P', (byte)'\'', 4, (byte)'S', (byte)'e', (byte)'e', (byte)'g', (byte)'e', (byte)'r', 0, 0, 0 });
    else index.AddRange(new byte[] { 0, 0, 0, 0 });
}
var records = InfoDatIndex.ReadEnglish(index.ToArray());
Check(records.Count == 20 && records[19].Title == "SONG 19" && records.All(s => s.SongNumber is null && !s.PlaybackVerified), "Disc text records must not invent music/song IDs.");
Check(records[0].Artist == "P' Seeger", "Artist control byte must not truncate an otherwise valid record block.");
if (args.Length == 2 && args[0] == "--fixture")
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!); File.WriteAllBytes(args[1], fixture);
}
Console.WriteLine("PASS: MIDI tempo, running status, lyrics, transpose, gain, malformed input, pause/seek/completion, ISO inventory, RAR detection and disc index metadata.");

sealed class RecordingOutput : IMidiOutput
{
    public System.Collections.Concurrent.ConcurrentQueue<uint> Messages { get; } = new();
    public void Send(uint message) => Messages.Enqueue(message);
    public void SendSystemExclusive(byte[] packet) { }
    public void Reset() { }
    public void Dispose() { }
}
