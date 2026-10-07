using System.Diagnostics;

namespace Arirang.Core;

public interface IMidiOutput : IDisposable
{
    void Send(uint message);
    void SendSystemExclusive(byte[] packet);
    void Reset();
}

public sealed class MidiPlayback(IMidiOutput output) : IDisposable
{
    private readonly object gate = new();
    private readonly Stopwatch clock = new();
    private readonly CancellationTokenSource cancellation = new();
    private Task? worker;
    private MidiSong? song;
    private int next, key, volume = 80;
    private double position, speed = 1;
    private bool playing;
    public string? Error { get; private set; }
    public bool Playing { get { lock (gate) return playing; } }
    public double Position { get { lock (gate) return Current(); } }
    public MidiSong? Song { get { lock (gate) return song; } }

    private double Current() => Math.Min(song?.Duration ?? 0, position + (playing ? clock.Elapsed.TotalSeconds * speed : 0));

    public void Load(MidiSong value)
    {
        lock (gate)
        {
            output.Reset(); song = value; position = 0; next = 0; playing = false; clock.Reset(); Error = null;
            worker ??= Task.Run(Run);
        }
    }

    public void Play()
    {
        lock (gate)
        {
            if (song is null || playing) return;
            if (position >= song.Duration) { position = 0; next = 0; }
            Restore(); clock.Restart(); playing = true;
        }
    }

    public void Pause()
    {
        lock (gate) { position = Current(); playing = false; clock.Reset(); output.Reset(); }
    }

    public void Seek(double seconds)
    {
        lock (gate)
        {
            bool resume = playing;
            output.Reset(); position = Math.Clamp(seconds, 0, song?.Duration ?? 0); clock.Restart();
            next = 0;
            if (song is not null) while (next < song.Messages.Count && song.Messages[next].Seconds < position) next++;
            if (resume) Restore();
        }
    }

    public void SetSpeed(double value)
    {
        if (!double.IsFinite(value) || value < .5 || value > 2) throw new ArgumentOutOfRangeException(nameof(value));
        lock (gate) { position = Current(); clock.Restart(); speed = value; }
    }

    public void SetKey(int value)
    {
        if (value < -12 || value > 12) throw new ArgumentOutOfRangeException(nameof(value));
        lock (gate) { key = value; output.Reset(); if (playing) Restore(); }
    }

    public void SetVolume(int value)
    {
        if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
        lock (gate)
        {
            volume = value;
            if (song is null || !playing) return;
            var levels = Enumerable.Repeat(100, 16).ToArray();
            for (int i = 0; i < next; i++)
            {
                uint p = song.Messages[i].Packed;
                if ((p & 0xf0) == 0xb0 && (p >> 8 & 127) == 7) levels[p & 15] = (int)(p >> 16 & 127);
            }
            for (int channel = 0; channel < 16; channel++)
                output.Send(Transform((uint)(0xb0 | channel | 7 << 8 | levels[channel] << 16), key, volume));
        }
    }

    public static uint Transform(uint packed, int key, int volume)
    {
        int status = (int)(packed & 255), a = (int)(packed >> 8 & 127), b = (int)(packed >> 16 & 127);
        int kind = status & 0xf0;
        if ((status & 15) != 9 && kind is 0x80 or 0x90 or 0xa0) a = Math.Clamp(a + key, 0, 127);
        if (kind == 0xb0 && a == 7) b = b * volume / 100;
        return (uint)(status | a << 8 | b << 16);
    }

    private void Send(MidiMessage message)
    {
        if (message.SystemExclusive is not null) output.SendSystemExclusive(message.SystemExclusive);
        else output.Send(Transform(message.Packed, key, volume));
    }

    private void Restore()
    {
        if (song is null) return;
        // Reconstruct programs/controllers and held notes without replaying old music.
        var state = new Dictionary<(int Channel, int Kind, int Index), uint>();
        var held = new Dictionary<(int Channel, int Note), uint>();
        for (int i = 0; i < next; i++)
        {
            var message = song.Messages[i];
            if (message.SystemExclusive is not null) continue;
            uint p = message.Packed;
            int status = (int)(p & 255), channel = status & 15, kind = status & 0xf0,
                a = (int)(p >> 8 & 127), b = (int)(p >> 16 & 127);
            if (kind is 0xc0 or 0xd0 or 0xe0 or 0xb0) state[(channel, kind, kind == 0xb0 ? a : 0)] = p;
            if (kind == 0x90 && b > 0) held[(channel, a)] = p;
            else if (kind == 0x80 || kind == 0x90 && b == 0) held.Remove((channel, a));
            if (kind == 0xb0 && a is 120 or 123)
                foreach (var note in held.Keys.Where(n => n.Channel == channel).ToArray()) held.Remove(note);
        }
        for (int channel = 0; channel < 16; channel++)
            output.Send(Transform((uint)(0xb0 | channel | 7 << 8 | 100 << 16), key, volume));
        foreach (uint p in state.OrderBy(e => e.Key.Kind == 0xc0 ? 1 : 0).Select(e => e.Value))
            output.Send(Transform(p, key, volume));
        foreach (uint p in held.Values) output.Send(Transform(p, key, volume));
    }

    private async Task Run()
    {
        while (!cancellation.IsCancellationRequested)
        {
            int delay = 30;
            try
            {
              lock (gate)
              {
                if (playing && song is not null)
                {
                    double now = Current();
                    while (next < song.Messages.Count && song.Messages[next].Seconds <= now) Send(song.Messages[next++]);
                    if (now >= song.Duration)
                    {
                        position = song.Duration; playing = false; clock.Reset(); output.Reset();
                    }
                    else if (next < song.Messages.Count)
                        delay = Math.Clamp((int)((song.Messages[next].Seconds - now) / speed * 1000), 1, 20);
                }
              }
            }
            catch (Exception error)
            {
                lock (gate) { position = Current(); playing = false; clock.Reset(); Error = error.Message; }
                try { output.Reset(); } catch { /* The UI reports the original device error. */ }
            }
            try { await Task.Delay(delay, cancellation.Token); } catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        cancellation.Cancel(); worker?.GetAwaiter().GetResult();
        lock (gate) { playing = false; output.Reset(); output.Dispose(); }
        cancellation.Dispose();
    }
}
