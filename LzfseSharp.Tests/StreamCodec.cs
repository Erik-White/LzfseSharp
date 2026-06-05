namespace LzfseSharp.Tests;

/// <summary>
/// Test helper that bridges the stream-based <see cref="LzfseDecoder"/> API to the
/// in-memory byte[] form most tests are written against. Materializing the full result
/// is fine for tests; production callers stream to bound memory.
/// </summary>
internal static class StreamCodec
{
    public static byte[] Decode(byte[] compressed)
    {
        using MemoryStream source = new(compressed, writable: false);
        using MemoryStream destination = new();
        LzfseDecoder.Decompress(source, destination);
        return destination.ToArray();
    }

    public static async Task<byte[]> DecodeAsync(byte[] compressed)
    {
        using MemoryStream source = new(compressed, writable: false);
        using MemoryStream destination = new();
        await LzfseDecoder.DecompressAsync(source, destination);
        return destination.ToArray();
    }
}
