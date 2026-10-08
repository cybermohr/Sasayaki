namespace Sasayaki.Core;

/// <summary>Preserves sample order across callback boundaries, including the final partial frame.</summary>
public sealed class PcmFramer(Action<byte[]> output)
{
    private readonly byte[] frame = new byte[640];
    private int used;
    public void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var length = Math.Min(frame.Length - used, data.Length);
            data[..length].CopyTo(frame.AsSpan(used)); used += length; data = data[length..];
            if (used == frame.Length) { output(frame.ToArray()); used = 0; }
        }
    }
    public void Flush()
    {
        if (used % 2 != 0) throw new InvalidOperationException("Audio ended in a partial PCM16 sample.");
        if (used > 0) output(frame[..used]);
        used = 0;
    }
}
