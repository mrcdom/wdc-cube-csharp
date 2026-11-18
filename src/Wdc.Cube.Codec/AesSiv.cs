using System.Security.Cryptography;

namespace Wdc.Cube.Codec;

/// <summary>
/// AES-SIV (RFC 5297): deterministic authenticated encryption. The key is split in two halves: the first keys S2V
/// (CMAC), the second keys CTR. Output is <c>V ‖ C</c>, with the 16-byte synthetic IV first.
/// </summary>
/// <remarks>Built on the platform's AES block (ECB, one block at a time); not available where AES is not (WebAssembly).</remarks>
internal sealed class AesSiv : IDisposable
{
    private const int Block = 16;
    private readonly Aes _mac;
    private readonly Aes _ctr;
    private readonly byte[] _k1 = new byte[Block];
    private readonly byte[] _k2 = new byte[Block];

    public AesSiv(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (32 or 48 or 64))
        {
            throw new ArgumentException("An AES-SIV key is 32, 48 or 64 bytes.", nameof(key));
        }

        var half = key.Length / 2;
        _mac = Aes.Create();
        _mac.Key = key[..half].ToArray();
        _ctr = Aes.Create();
        _ctr.Key = key[half..].ToArray();

        // CMAC subkeys: L = AES(0); K1 = dbl(L); K2 = dbl(K1).
        Span<byte> l = stackalloc byte[Block];
        _mac.EncryptEcb(new byte[Block], l, PaddingMode.None);
        l.CopyTo(_k1);
        Dbl(_k1);
        _k1.CopyTo(_k2, 0);
        Dbl(_k2);
    }

    public byte[] Seal(IReadOnlyList<byte[]> associated, ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[Block + plaintext.Length];
        var v = S2V(associated, plaintext);
        v.CopyTo(output, 0);
        Ctr(v, plaintext, output.AsSpan(Block));
        return output;
    }

    /// <summary>The plaintext, or <see langword="null"/> when the tag does not match (wrong key, altered).</summary>
    public byte[]? Open(IReadOnlyList<byte[]> associated, ReadOnlySpan<byte> sealedBytes)
    {
        if (sealedBytes.Length < Block)
        {
            return null;
        }

        var v = sealedBytes[..Block];
        var plaintext = new byte[sealedBytes.Length - Block];
        Ctr(v, sealedBytes[Block..], plaintext);
        var expected = S2V(associated, plaintext);
        return CryptographicOperations.FixedTimeEquals(expected, v) ? plaintext : null;
    }

    public void Dispose()
    {
        _mac.Dispose();
        _ctr.Dispose();
    }

    // :: S2V

    private byte[] S2V(IReadOnlyList<byte[]> associated, ReadOnlySpan<byte> last)
    {
        var d = Cmac(new byte[Block]);
        foreach (var component in associated)
        {
            Dbl(d);
            Xor(d, Cmac(component));
        }

        byte[] t;
        if (last.Length >= Block)
        {
            t = last.ToArray();
            Xor(t.AsSpan(t.Length - Block), d); // xorend
        }
        else
        {
            Dbl(d);
            t = new byte[Block];
            last.CopyTo(t);
            t[last.Length] = 0x80;
            Xor(t, d);
        }

        return Cmac(t);
    }

    private byte[] Cmac(ReadOnlySpan<byte> message)
    {
        var x = new byte[Block];
        var blocks = Math.Max(1, (message.Length + Block - 1) / Block);
        var complete = message.Length > 0 && message.Length % Block == 0;
        for (var i = 0; i < blocks - 1; i++)
        {
            Xor(x, message.Slice(i * Block, Block));
            _mac.EncryptEcb(x, x, PaddingMode.None);
        }

        var lastBlock = new byte[Block];
        var tail = message[((blocks - 1) * Block)..];
        tail.CopyTo(lastBlock);
        if (complete)
        {
            Xor(lastBlock, _k1);
        }
        else
        {
            lastBlock[tail.Length] = 0x80;
            Xor(lastBlock, _k2);
        }

        Xor(x, lastBlock);
        _mac.EncryptEcb(x, x, PaddingMode.None);
        return x;
    }

    // :: CTR

    private void Ctr(ReadOnlySpan<byte> v, ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> counter = stackalloc byte[Block];
        v.CopyTo(counter);
        counter[8] &= 0x7f; // RFC 5297 §2.6: clear the 31st and 63rd bits from the right
        counter[12] &= 0x7f;
        Span<byte> stream = stackalloc byte[Block];
        for (var offset = 0; offset < input.Length; offset += Block)
        {
            _ctr.EncryptEcb(counter, stream, PaddingMode.None);
            var count = Math.Min(Block, input.Length - offset);
            for (var i = 0; i < count; i++)
            {
                output[offset + i] = (byte)(input[offset + i] ^ stream[i]);
            }

            Increment(counter);
        }
    }

    private static void Increment(Span<byte> counter)
    {
        for (var i = counter.Length - 1; i >= 0 && ++counter[i] == 0; i--)
        {
        }
    }

    // :: Bits

    /// <summary>Doubling in GF(2^128): shift left one bit; when the top bit fell out, xor 0x87 into the last byte.</summary>
    private static void Dbl(Span<byte> block)
    {
        var carry = (block[0] & 0x80) != 0;
        for (var i = 0; i < Block - 1; i++)
        {
            block[i] = (byte)((block[i] << 1) | (block[i + 1] >> 7));
        }

        block[Block - 1] = (byte)(block[Block - 1] << 1);
        if (carry)
        {
            block[Block - 1] ^= 0x87;
        }
    }

    private static void Xor(Span<byte> target, ReadOnlySpan<byte> other)
    {
        for (var i = 0; i < other.Length; i++)
        {
            target[i] ^= other[i];
        }
    }
}
