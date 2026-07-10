namespace TorrentFree.Services;

/// <summary>
/// Reads untrusted .torrent content without allowing an unbounded in-memory copy.
/// </summary>
internal static class TorrentFileContentReader
{
    private const int BufferSize = 64 * 1024;

    public static async Task<byte[]> ReadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fileInfo = new FileInfo(filePath);
        EnsureSupportedLength(fileInfo.Length);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ReadAsync(stream, cancellationToken);
    }

    public static async Task<byte[]> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new InvalidDataException("The selected .torrent file cannot be read.");
        }

        if (stream.CanSeek)
        {
            var remainingLength = stream.Length - stream.Position;
            EnsureSupportedLength(remainingLength);
            return await ReadKnownLengthAsync(stream, (int)remainingLength, cancellationToken);
        }

        using var content = new MemoryStream();
        return await ReadToEndBoundedAsync(stream, content, totalBytes: 0, cancellationToken);
    }

    private static async Task<byte[]> ReadToEndBoundedAsync(
        Stream stream,
        MemoryStream content,
        int totalBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];

        while (true)
        {
            // Once the supported limit has been read, request one more byte so a stream
            // which did not expose its length cannot silently truncate oversized input.
            var bytesToRead = Math.Min(buffer.Length, TorrentFileLimits.MaxFileSizeBytes - totalBytes + 1);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (bytesRead > TorrentFileLimits.MaxFileSizeBytes - totalBytes)
            {
                throw CreateTooLargeException();
            }

            await content.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytes += bytesRead;
        }

        return content.ToArray();
    }

    private static async Task<byte[]> ReadKnownLengthAsync(
        Stream stream,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        var content = new byte[expectedLength];
        var totalBytes = 0;
        while (totalBytes < content.Length)
        {
            var bytesRead = await stream.ReadAsync(content.AsMemory(totalBytes), cancellationToken);
            if (bytesRead == 0)
            {
                Array.Resize(ref content, totalBytes);
                return content;
            }

            totalBytes += bytesRead;
        }

        // A provider can report a stale length. Probe once beyond it so growth cannot bypass
        // the limit or cause us to return silently truncated metadata.
        var probe = new byte[1];
        if (await stream.ReadAsync(probe, cancellationToken) == 0)
        {
            return content;
        }

        if (expectedLength == TorrentFileLimits.MaxFileSizeBytes)
        {
            throw CreateTooLargeException();
        }

        using var expandedContent = new MemoryStream(
            Math.Min(TorrentFileLimits.MaxFileSizeBytes, Math.Max(BufferSize, expectedLength + 1)));
        await expandedContent.WriteAsync(content, cancellationToken);
        expandedContent.WriteByte(probe[0]);
        return await ReadToEndBoundedAsync(
            stream,
            expandedContent,
            expectedLength + 1,
            cancellationToken);
    }

    private static void EnsureSupportedLength(long length)
    {
        if (length < 0 || length > TorrentFileLimits.MaxFileSizeBytes)
        {
            throw CreateTooLargeException();
        }
    }

    private static InvalidDataException CreateTooLargeException()
        => new($"The selected .torrent file exceeds the {TorrentFileLimits.MaxFileSizeBytes / (1024 * 1024)} MB import limit.");
}
