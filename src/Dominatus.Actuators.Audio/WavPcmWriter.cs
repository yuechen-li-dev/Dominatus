namespace Dominatus.Actuators.Audio;

internal static class WavPcmWriter
{
    public static void WriteMono16(string path, int sampleRate, TimeSpan duration, Func<int, short> sampleAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        int samples = Math.Max(1, (int)(duration.TotalSeconds * sampleRate));
        int dataBytes = checked(samples * 2); using var fs = File.Create(path); using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8); bw.Write(36 + dataBytes); bw.Write("WAVE"u8); bw.Write("fmt "u8); bw.Write(16);
        bw.Write((short)1); bw.Write((short)1); bw.Write(sampleRate); bw.Write(sampleRate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(dataBytes);
        for (int i = 0; i < samples; i++) bw.Write(sampleAt(i));
    }
}
