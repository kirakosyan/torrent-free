using System.Text;

namespace TorrentFree.Services;

/// <summary>Retains the current crash log and one bounded previous log.</summary>
internal sealed class BoundedCrashLog(string path, int maxFileBytes = 256 * 1024)
{
    private static readonly byte[] TruncationMarker = Encoding.UTF8.GetBytes("\n[entry truncated]\n");
    private readonly object _gate = new();

    public void Append(string entry)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, TruncationMarker.Length + 4);
        var bytes = Encoding.UTF8.GetBytes(entry);
        if (bytes.Length > maxFileBytes)
        {
            var contentLength = maxFileBytes - TruncationMarker.Length;
            // Do not split a UTF-8 code point at the size boundary.
            while ((bytes[contentLength] & 0xC0) == 0x80) contentLength--;
            bytes = [.. bytes.AsSpan(0, contentLength), .. TruncationMarker];
        }

        lock (_gate)
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
                Directory.CreateDirectory(directory);

            var current = new FileInfo(path);
            if (current.Exists && current.Length + bytes.Length > maxFileBytes)
            {
                if (current.Length > maxFileBytes)
                {
                    // Bound logs left by older versions too, retaining their newest data.
                    using var source = File.OpenRead(path);
                    source.Seek(-maxFileBytes, SeekOrigin.End);
                    var tail = new byte[maxFileBytes];
                    source.ReadExactly(tail);
                    var start = 0;
                    while (start < tail.Length && (tail[start] & 0xC0) == 0x80) start++;
                    File.WriteAllBytes(path + ".1", tail.AsSpan(start).ToArray());
                }
                else
                {
                    File.Move(path, path + ".1", overwrite: true);
                }

                // FileMode.Create also replaces an oversized legacy log after copying its tail.
                using var replacement = File.Create(path);
                replacement.Write(bytes);
                return;
            }

            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }
}
