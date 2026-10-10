
using System.Security.Cryptography;

namespace XISO.Core.Metadata;

public sealed class Xex2ImageReconstructor
{
    private const uint FileFormatInfoKey = 0x000003FF;
    private const int RetailKeySize = 16;
    private const int NormalBlockHeaderSize = 24;
    private const int LzxFrameSize = 0x8000;

    private static readonly byte[] RetailKey =
    [
        0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3,
        0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91
    ];

    public byte[] Reconstruct(byte[] xexData)
    {
        ArgumentNullException.ThrowIfNull(xexData);

        var reader = new Xex2Reader();
        var header = reader.ReadHeader(xexData);
        var security = reader.ReadSecurityInfo(xexData);
        var optionalHeaders = reader.ReadOptionalHeaders(xexData);

        if (header.PeDataOffset > int.MaxValue ||
            header.PeDataOffset > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 basefile data offset is outside the file.");
        }

        Xex2OptionalHeader? formatHeader =
            optionalHeaders.Find(h => h.Key == FileFormatInfoKey);

        if (formatHeader is null)
        {
            throw new InvalidDataException(
                "XEX2 File Format Info header was not found.");
        }

        if (formatHeader.Value > int.MaxValue ||
            (long)formatHeader.Value + 8 > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 File Format Info header is outside the file.");
        }

        int formatOffset = (int)formatHeader.Value;

        uint infoSize = ReadUInt32BigEndian(xexData, formatOffset);
        ushort encryptionType =
            ReadUInt16BigEndian(xexData, formatOffset + 4);
        ushort compressionType =
            ReadUInt16BigEndian(xexData, formatOffset + 6);

        if (encryptionType != 1)
        {
            throw new NotSupportedException(
                $"XEX encryption type 0x{encryptionType:X4} is not supported.");
        }

        if (compressionType != 1 && compressionType != 2)
        {
            throw new NotSupportedException(
                $"XEX compression type 0x{compressionType:X4} is not supported.");
        }

        if (security.ImageSize == 0 ||
            security.ImageSize > int.MaxValue)
        {
            throw new InvalidDataException("XEX2 image size is invalid.");
        }

        if (infoSize < 8 ||
            (long)formatOffset + infoSize > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 File Format Info has an invalid size.");
        }

        int imageSize = (int)security.ImageSize;
        int payloadOffset = (int)header.PeDataOffset;
        int payloadLength = xexData.Length - payloadOffset;

        if (payloadLength <= 0)
        {
            throw new InvalidDataException(
                "XEX2 basefile payload is empty.");
        }

        byte[] work = xexData.AsSpan(
            payloadOffset, payloadLength).ToArray();

        // Decrypt the title key using the retail key.
        byte[] titleKey;

        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 128;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = RetailKey;

            using ICryptoTransform decryptor = aes.CreateDecryptor();

            titleKey = decryptor.TransformFinalBlock(
                security.AesKey, 0, RetailKeySize);
        }

        // Decrypt the executable payload with AES-CBC and a zero IV.
        int encryptedLength = work.Length & ~15;

        if (encryptedLength == 0)
        {
            throw new InvalidDataException(
                "XEX2 encrypted payload is too short.");
        }

        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 128;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = titleKey;
            aes.IV = new byte[16];

            using ICryptoTransform decryptor = aes.CreateDecryptor();

            byte[] decrypted = decryptor.TransformFinalBlock(
                work, 0, encryptedLength);

            Buffer.BlockCopy(decrypted, 0, work, 0, decrypted.Length);
        }

        if (compressionType == 1)
        {
            return ReconstructBasicCompression(
                xexData, formatOffset, infoSize, work, imageSize);
        }

        return ReconstructNormalCompression(
            xexData, formatOffset, infoSize, work, imageSize);
    }

    private static byte[] ReconstructBasicCompression(
        byte[] xexData,
        int formatOffset,
        uint infoSize,
        byte[] work,
        int imageSize)
    {
        if ((infoSize - 8) % 8 != 0)
        {
            throw new InvalidDataException(
                "XEX2 basic-compression records have an invalid size.");
        }

        uint recordCount = (infoSize - 8) / 8;
        byte[] image = new byte[imageSize];

        long sourceOffset = 0;
        long destinationOffset = 0;

        for (uint i = 0; i < recordCount; i++)
        {
            int recordOffset =
                checked(formatOffset + 8 + (int)i * 8);

            uint dataSize =
                ReadUInt32BigEndian(xexData, recordOffset);

            uint zeroSize =
                ReadUInt32BigEndian(xexData, recordOffset + 4);

            if (dataSize == 0 && zeroSize == 0)
                break;

            if (sourceOffset + dataSize > work.Length ||
                destinationOffset + dataSize + zeroSize > image.Length)
            {
                throw new InvalidDataException(
                    "XEX2 basic-compression record exceeds image bounds.");
            }

            Buffer.BlockCopy(
                work,
                (int)sourceOffset,
                image,
                (int)destinationOffset,
                (int)dataSize);

            sourceOffset += dataSize;
            destinationOffset += dataSize + zeroSize;
        }

        return image;
    }

    private static byte[] ReconstructNormalCompression(
        byte[] xexData,
        int formatOffset,
        uint infoSize,
        byte[] decryptedPayload,
        int imageSize)
    {
        // Normal File Format Info:
        // +0x08: LZX window size in bytes.
        // +0x0C: first compressed-block descriptor:
        //        4-byte block size followed by a 20-byte SHA-1 hash.
        //
        // Each block in the payload starts with a descriptor for the
        // following block, followed by length-prefixed LZX chunks.

        const int firstBlockDescriptorOffset = 12;

        if (infoSize < firstBlockDescriptorOffset + NormalBlockHeaderSize)
        {
            throw new InvalidDataException(
                "XEX2 normal-compression metadata is incomplete.");
        }

        uint windowSizeBytes =
            ReadUInt32BigEndian(xexData, formatOffset + 8);

        if (windowSizeBytes < 0x8000 ||
            windowSizeBytes > 0x200000 ||
            (windowSizeBytes & (windowSizeBytes - 1)) != 0)
        {
            throw new InvalidDataException(
                $"XEX2 LZX window size 0x{windowSizeBytes:X8} is invalid.");
        }

        int windowBits = 0;

        for (uint size = windowSizeBytes; size > 1; size >>= 1)
        {
            windowBits++;
        }

        uint currentBlockSize =
            ReadUInt32BigEndian(
                xexData, formatOffset + firstBlockDescriptorOffset);

        byte[] currentBlockHash = xexData.AsSpan(
            formatOffset + firstBlockDescriptorOffset + 4, 20).ToArray();

        var decoder = new Lzx.LzxDecoder(windowBits);
        byte[] image = new byte[imageSize];

        int sourceOffset = 0;
        int destinationOffset = 0;
        int blockIndex = 0;

        while (currentBlockSize != 0)
        {
            if (currentBlockSize < NormalBlockHeaderSize ||
                currentBlockSize > int.MaxValue ||
                sourceOffset + (long)currentBlockSize > decryptedPayload.Length)
            {
                throw new InvalidDataException(
                    $"XEX2 normal-compression block {blockIndex} has an invalid size.");
            }

            int blockSize = (int)currentBlockSize;

            ReadOnlySpan<byte> block =
                decryptedPayload.AsSpan(sourceOffset, blockSize);

            // The descriptor in the XEX metadata contains the hash for
            // the first block. Each payload block carries the descriptor
            // for the next block in its first 24 bytes.
            byte[] calculatedHash = SHA1.HashData(block);

            if (!CryptographicOperations.FixedTimeEquals(
                    calculatedHash, currentBlockHash))
            {
                throw new InvalidDataException(
                    $"XEX2 normal-compression block {blockIndex} failed SHA-1 verification.");
            }

            // Capture the following block's descriptor before processing
            // the current block.
            uint nextBlockSize =
                ReadUInt32BigEndian(block.ToArray(), 0);

            byte[] nextBlockHash = block.Slice(4, 20).ToArray();

            int blockPosition = NormalBlockHeaderSize;
            bool foundTerminator = false;

            while (blockPosition + 2 <= blockSize)
            {
                int chunkSize =
                    (block[blockPosition] << 8) |
                    block[blockPosition + 1];

                blockPosition += 2;

                if (chunkSize == 0)
                {
                    foundTerminator = true;
                    break;
                }

                if (blockPosition + (long)chunkSize > blockSize)
                {
                    throw new InvalidDataException(
                        $"XEX2 LZX chunk in block {blockIndex} exceeds block bounds.");
                }

                if (destinationOffset >= imageSize)
                {
                    throw new InvalidDataException(
                        "XEX2 LZX data exceeds the expected image size.");
                }

                int frameOutputSize = Math.Min(
                    LzxFrameSize, imageSize - destinationOffset);

                byte[] chunkData = block.Slice(
                    blockPosition, chunkSize).ToArray();

                using var input = new MemoryStream(
                    chunkData, writable: false);

                using var output = new MemoryStream(frameOutputSize);

                // Decompress returns a status code: zero means success.
                int status = decoder.Decompress(
                    input, chunkSize, output, frameOutputSize);

                if (status != 0)
                {
                    throw new InvalidDataException(
                        $"XEX2 LZX decompression failed in block {blockIndex}, " +
                        $"chunk at offset 0x{blockPosition:X} (status {status}).");
                }

                byte[] frame = output.ToArray();

                if (frame.Length != frameOutputSize)
                {
                    throw new InvalidDataException(
                        $"XEX2 LZX decoder produced {frame.Length} bytes; " +
                        $"expected {frameOutputSize}.");
                }

                Buffer.BlockCopy(
                    frame, 0, image, destinationOffset, frameOutputSize);

                destinationOffset += frameOutputSize;
                blockPosition += chunkSize;
            }

            if (!foundTerminator)
            {
                throw new InvalidDataException(
                    $"XEX2 normal-compression block {blockIndex} has no chunk terminator.");
            }

            sourceOffset += blockSize;
            currentBlockSize = nextBlockSize;
            currentBlockHash = nextBlockHash;
            blockIndex++;
        }

        if (destinationOffset != imageSize)
        {
            throw new InvalidDataException(
                $"XEX2 LZX reconstruction was incomplete: " +
                $"produced {destinationOffset} of {imageSize} bytes.");
        }

        return image;
    }

    private static ushort ReadUInt16BigEndian(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32BigEndian(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }
}
