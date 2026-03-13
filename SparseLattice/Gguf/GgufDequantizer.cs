using System.Buffers.Binary;
/////////////////////////////
namespace SparseLattice.Gguf;

internal static class GgufDequantizer
{
    public static void ReadF32(Stream s, float[] dest)
    {
        byte[] buf = new byte[dest.Length * 4];
        ReadExact(s, buf);
        Buffer.BlockCopy(buf, 0, dest, 0, buf.Length);
        if (!BitConverter.IsLittleEndian)
            for (int i = 0; i < dest.Length; i++)
                dest[i] = BinaryPrimitives.ReverseEndianness(
                    BitConverter.SingleToInt32Bits(dest[i])) is int bits
                    ? BitConverter.Int32BitsToSingle(bits) : dest[i];
    }

    public static void ReadF16(Stream s, float[] dest)
    {
        int n   = dest.Length;
        byte[] buf = new byte[n * 2];
        ReadExact(s, buf);
        for (int i = 0; i < n; i++)
        {
            ushort h = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(i * 2, 2));
            dest[i]  = HalfToFloat(h);
        }
    }

    public static void DequantizeQ8_0(Stream s, float[] dest)
    {
        const int BlockElements = 32;
        const int BlockBytes    = 2 + BlockElements;

        int blocks  = dest.Length / BlockElements;
        byte[] buf  = new byte[blocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx  = 0;
        for (int b = 0; b < blocks; b++)
        {
            float scale = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            inIdx += 2;
            for (int j = 0; j < BlockElements; j++)
                dest[outIdx++] = scale * (float)(sbyte)buf[inIdx++];
        }
    }

    public static void DequantizeQ4_0(Stream s, float[] dest)
    {
        const int BlockElements = 32;
        const int NibbleBytes   = BlockElements / 2;
        const int BlockBytes    = 2 + NibbleBytes;

        int blocks = dest.Length / BlockElements;
        byte[] buf = new byte[blocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx  = 0;
        for (int b = 0; b < blocks; b++)
        {
            float scale = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            inIdx += 2;
            for (int j = 0; j < NibbleBytes; j++)
            {
                byte raw    = buf[inIdx++];
                int  lo     = (raw & 0x0F) - 8;
                int  hi     = (raw >> 4)   - 8;
                dest[outIdx++] = scale * lo;
                dest[outIdx++] = scale * hi;
            }
        }
    }

    public static void DequantizeBF16(Stream s, float[] dest)
    {
        byte[] buf = new byte[dest.Length * 2];
        ReadExact(s, buf);

        for (int i = 0; i < dest.Length; i++)
        {
            ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(i * 2, 2));
            int bits = raw << 16;
            dest[i] = BitConverter.Int32BitsToSingle(bits);
        }
    }

    private static readonly float[] s_mxfp4E2M1 =
    [
        0.0f,  0.5f,  1.0f,  1.5f,  2.0f,  3.0f,  4.0f,  6.0f,
       -0.0f, -0.5f, -1.0f, -1.5f, -2.0f, -3.0f, -4.0f -6.0f,
    ];

    public static void DequantizeMXFP4(Stream s, float[] dest)
    {
        const int BlockElements = 32;
        const int NibbleBytes = BlockElements / 2;
        const int BlockBytes = 1 + NibbleBytes;

        int blocks = dest.Length / BlockElements;
        byte[] buf = new byte[blocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int b = 0; b < blocks; b++)
        {
            byte e8m0 = buf[inIdx++];
            float sharedScale = (e8m0 == 0) ? 0f : MathF.Pow(2f, e8m0 - 127);

            for (int j = 0; j < NibbleBytes; j++)
            {
                byte raw = buf[inIdx++];
                int lo = raw & 0x0F;
                int hi = (raw >> 4) & 0x0F;
                dest[outIdx++] = s_mxfp4E2M1[lo] * sharedScale;
                dest[outIdx++] = s_mxfp4E2M1[hi] * sharedScale;
            }
        }
    }

    public static void DequantizeQ2K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 2 + 2 + 16 + 64;

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d    = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            float dmin = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx + 2, 2)));
            int scalesOff = inIdx + 4;
            int qsOff     = inIdx + 4 + 16;
            inIdx += BlockBytes;

            for (int j = 0; j < 256; j++)
            {
                int group = j / 16;
                byte scByte = buf[scalesOff + group];
                int sc  = scByte & 0x0F;
                int m   = scByte >> 4;
                int qByte = buf[qsOff + j / 4];
                int shift = (j % 4) * 2;
                int q = (qByte >> shift) & 3;
                dest[outIdx++] = d * sc * q - dmin * m;
            }
        }
    }

    public static void DequantizeQ3K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 32 + 64 + 12 + 2;

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        Span<int> scales = stackalloc int[16];
        for (int sb = 0; sb < superBlocks; sb++)
        {
            int hmaskOff  = inIdx;
            int qsOff     = inIdx + 32;
            int scalesOff = inIdx + 32 + 64;
            float d = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(
                buf.AsSpan(inIdx + 32 + 64 + 12, 2)));
            inIdx += BlockBytes;

            for (int j = 0; j < 16; j++)
            {
                int low4 = (j < 8)
                    ? (buf[scalesOff + j] & 0x0F)
                    : (buf[scalesOff + j - 8] >> 4);
                int high2 = (buf[scalesOff + 8 + j / 4] >> (2 * (j % 4))) & 3;
                scales[j] = (low4 | (high2 << 4)) - 32;
            }

            for (int j = 0; j < 256; j++)
            {
                int group = j / 16;
                int qByte = buf[qsOff + j / 4];
                int shift = (j % 4) * 2;
                int qLow = (qByte >> shift) & 3;
                int hBit = (buf[hmaskOff + j / 8] >> (j % 8)) & 1;
                int q = qLow | (hBit << 2);
                dest[outIdx++] = d * scales[group] * (q - 4);
            }
        }
    }

    public static void DequantizeQ4K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 2 + 2 + 12 + 128;

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        Span<byte> sc = stackalloc byte[8];
        Span<byte> m  = stackalloc byte[8];
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d    = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            float dmin = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx + 2, 2)));
            int scalesOff = inIdx + 4;
            int qsOff     = inIdx + 4 + 12;
            inIdx += BlockBytes;

            UnpackQ4KScales(buf.AsSpan(scalesOff, 12), sc, m);

            for (int j = 0; j < 256; j++)
            {
                int subBlock = j / 32;
                int qByte = buf[qsOff + j / 2];
                int q = (j % 2 == 0) ? (qByte & 0x0F) : (qByte >> 4);
                dest[outIdx++] = d * sc[subBlock] * q - dmin * m[subBlock];
            }
        }
    }

    public static void DequantizeQ5K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 2 + 2 + 12 + 32 + 128;

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        Span<byte> sc = stackalloc byte[8];
        Span<byte> m  = stackalloc byte[8];
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d    = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            float dmin = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx + 2, 2)));
            int scalesOff = inIdx + 4;
            int qhOff     = inIdx + 4 + 12;
            int qsOff     = inIdx + 4 + 12 + 32;
            inIdx += BlockBytes;

            UnpackQ4KScales(buf.AsSpan(scalesOff, 12), sc, m);

            for (int j = 0; j < 256; j++)
            {
                int subBlock = j / 32;
                int qByte = buf[qsOff + j / 2];
                int qLow = (j % 2 == 0) ? (qByte & 0x0F) : (qByte >> 4);
                int hBit = (buf[qhOff + j / 8] >> (j % 8)) & 1;
                int q = qLow | (hBit << 4);
                dest[outIdx++] = d * sc[subBlock] * q - dmin * m[subBlock];
            }
        }
    }

    public static void DequantizeQ6K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 128 + 64 + 16 + 2;

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int sb = 0; sb < superBlocks; sb++)
        {
            int qlOff     = inIdx;
            int qhOff     = inIdx + 128;
            int scalesOff = inIdx + 128 + 64;
            float d = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(
                buf.AsSpan(inIdx + 128 + 64 + 16, 2)));
            inIdx += BlockBytes;

            for (int j = 0; j < 256; j++)
            {
                int group = j / 16;
                sbyte sc = (sbyte)buf[scalesOff + group];
                int qlByte = buf[qlOff + j / 2];
                int qLow = (j % 2 == 0) ? (qlByte & 0x0F) : (qlByte >> 4);
                int qhByte = buf[qhOff + j / 4];
                int shift = (j % 4) * 2;
                int qHigh = (qhByte >> shift) & 3;
                int q = qLow | (qHigh << 4);
                dest[outIdx++] = d * sc * (q - 32);
            }
        }
    }

    public static void DequantizeQ8K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 4 + 256 + 32;

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d = BitConverter.ToSingle(buf, inIdx);
            int qsOff = inIdx + 4;
            inIdx += BlockBytes;

            for (int j = 0; j < 256; j++)
                dest[outIdx++] = d * (sbyte)buf[qsOff + j];
        }
    }

    public static float HalfToFloat(ushort h)
        => (float)(BitConverter.Int16BitsToHalf((short)h));

    public static void ReadExact(Stream s, byte[] buf)
    {
        int total   = 0;
        int remaining = buf.Length;
        while (remaining > 0)
        {
            int read = s.Read(buf, total, remaining);
            if (read == 0)
                throw new EndOfStreamException(
                    $"Unexpected end of GGUF stream: expected {buf.Length} bytes, got {total}");
            total     += read;
            remaining -= read;
        }
    }

    static void UnpackQ4KScales(ReadOnlySpan<byte> raw, Span<byte> sc, Span<byte> m)
    {
        for (int j = 0; j < 4; j++)
        {
            sc[j] = (byte)(raw[j] & 63);
            m[j]  = (byte)(raw[j + 4] & 63);
        }
        for (int j = 4; j < 8; j++)
        {
            sc[j] = (byte)((raw[j + 4] & 0x0F) | ((raw[j - 4] >> 6) << 4));
            m[j]  = (byte)((raw[j + 4] >> 4) | ((raw[j] >> 6) << 4));
        }
    }
}
