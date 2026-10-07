using System.Runtime.InteropServices;
using Arirang.Core;

namespace Arirang.MidiPlayer;

internal sealed class WindowsMidiOutput : IMidiOutput
{
    private nint handle;
    private readonly List<(nint Header, nint Data)> pending = [];
    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHeader
    {
        public nint Data;
        public uint BufferLength, BytesRecorded;
        public nuint User;
        public uint Flags;
        public nint Next;
        public nuint Reserved;
        public uint Offset;
        public nuint Reserved0, Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7;
    }
    private static uint HeaderSize => (uint)Marshal.SizeOf<MidiHeader>();
    public WindowsMidiOutput()
    {
        uint result = midiOutOpen(out handle, uint.MaxValue, 0, 0, 0);
        if (result != 0) throw new IOException($"Windows MIDI synthesizer could not open (code {result}). Check your audio/MIDI device.");
    }
    public void Send(uint message) { Reap(); Check(midiOutShortMsg(handle, message)); }
    public void SendSystemExclusive(byte[] packet)
    {
        if (packet.Length == 0) return;
        Reap();
        nint data = Marshal.AllocHGlobal(packet.Length), header = Marshal.AllocHGlobal((int)HeaderSize);
        bool prepared = false;
        try
        {
            Marshal.Copy(packet, 0, data, packet.Length);
            Marshal.StructureToPtr(new MidiHeader { Data = data, BufferLength = (uint)packet.Length }, header, false);
            Check(midiOutPrepareHeader(handle, header, HeaderSize)); prepared = true;
            Check(midiOutLongMsg(handle, header, HeaderSize)); pending.Add((header, data));
        }
        catch
        {
            if (prepared) midiOutUnprepareHeader(handle, header, HeaderSize);
            Marshal.FreeHGlobal(header); Marshal.FreeHGlobal(data); throw;
        }
    }
    private void Reap()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var buffer = pending[i];
            if ((Marshal.PtrToStructure<MidiHeader>(buffer.Header).Flags & 1) == 0) continue;
            if (midiOutUnprepareHeader(handle, buffer.Header, HeaderSize) != 0) continue;
            Marshal.FreeHGlobal(buffer.Header); Marshal.FreeHGlobal(buffer.Data); pending.RemoveAt(i);
        }
    }
    public void Reset() { if (handle != 0) { midiOutReset(handle); Reap(); } }
    public void Dispose() { Reset(); if (handle != 0) { midiOutClose(handle); handle = 0; } }
    private static void Check(uint code) { if (code != 0) throw new IOException($"Windows MIDI output error {code}."); }
    [DllImport("winmm.dll")] private static extern uint midiOutOpen(out nint handle, uint device, nint callback, nuint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiOutShortMsg(nint handle, uint message);
    [DllImport("winmm.dll")] private static extern uint midiOutLongMsg(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiOutClose(nint handle);
}
