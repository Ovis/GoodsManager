using System.Buffers.Binary;
namespace GoodsManager.Web.Backups;

/// <summary>ZipArchiveが一覧を確保する前に中央ディレクトリを走査し、件数とメタデータの上限を適用</summary>
public static class BackupZipDirectory
{
    /// <summary>通常ZIPとZIP64を同じ上限で扱い、分割・暗号化・過大な名前を拒否</summary>
    public static void Check(Stream stream, BackupOptions options, CancellationToken token)
    {
        if (!stream.CanSeek || stream.Length < 22 || stream.Length > options.MaximumArchiveBytes) throw new InvalidDataException("ZIP容量が不正");
        var tail = new byte[(int)Math.Min(stream.Length, 65557)]; stream.Position = stream.Length - tail.Length; stream.ReadExactly(tail);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; --i)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50 && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tail.Length) { end = i; break; }
        if (end < 0) throw new InvalidDataException("ZIPの終端が不正");
        var record = tail.AsSpan(end);
        if (BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) != 0)
            throw new InvalidDataException("分割ZIPは未対応");
        long count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        long length = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        var boundary = stream.Length - tail.Length + end;
        if (count == ushort.MaxValue || length == uint.MaxValue || offset == uint.MaxValue)
        {
            if (boundary < 20) throw new InvalidDataException("ZIP64の終端が不正");
            Span<byte> locator = stackalloc byte[20]; stream.Position = boundary - 20; stream.ReadExactly(locator);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064b50 || BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]) != 1)
                throw new InvalidDataException("ZIP64の位置情報が不正");
            var position = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]));
            if (position < 0 || position > boundary - 76) throw new InvalidDataException("ZIP64の位置が領域外");
            Span<byte> zip64 = stackalloc byte[56]; stream.Position = position; stream.ReadExactly(zip64);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != 0x06064b50 || BinaryPrimitives.ReadUInt64LittleEndian(zip64[4..]) < 44 ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64[4..]) > (ulong)(boundary - 20 - position - 12) ||
                BinaryPrimitives.ReadUInt32LittleEndian(zip64[16..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(zip64[20..]) != 0 ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64[24..]) != BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]))
                throw new InvalidDataException("ZIP64の構造が不正");
            count = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..])); length = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(zip64[40..])); offset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(zip64[48..]));
            boundary = position;
        }
        else if (BinaryPrimitives.ReadUInt16LittleEndian(record[8..]) != count) throw new InvalidDataException("ZIPの件数が不一致");
        if (count < 1 || count > options.MaximumFiles || offset < 0 || length < 0 || length > (long)options.MaximumManifestBytes + options.MaximumFiles * 256L ||
            offset > boundary || length > boundary - offset) throw new InvalidDataException("ZIPのメタデータ上限を超過");
        var finish = checked(offset + length); stream.Position = offset; var actual = 0;
        Span<byte> header = stackalloc byte[46];
        while (stream.Position < finish)
        {
            token.ThrowIfCancellationRequested();
            if (++actual > options.MaximumFiles || finish - stream.Position < 46) throw new InvalidDataException("ZIP件数または構造が不正");
            stream.ReadExactly(header);
            var name = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]); var extra = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]); var comment = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50 || (BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) & 1) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(header[34..]) != 0 || name is 0 or > 256 || (long)name + extra + comment > finish - stream.Position)
                throw new InvalidDataException("ZIPの名前・暗号化・構造が不正");
            stream.Position += name + extra + comment;
        }
        if (stream.Position != finish || actual != count) throw new InvalidDataException("ZIPの実際の件数が不一致");
        stream.Position = 0;
    }
}
