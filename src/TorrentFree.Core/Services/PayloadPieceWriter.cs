using System.Runtime.CompilerServices;
using MonoTorrent;
using MonoTorrent.PieceWriter;
using ReusableTasks;
using TorrentFree.Models;

namespace TorrentFree.Services;

// DiskWriter opens streams with OpenOrCreate. Preserve missing files on reads and claim
// newly created files only on actual writes (including pieces spanning deselected files).
internal sealed class PayloadPieceWriter(int maxOpenFiles, Func<string, TorrentItem> findOwner, Action changed) : IPieceWriter
{
    private readonly DiskWriter _disk = new(maxOpenFiles);
    private readonly ConditionalWeakTable<ITorrentManagerFile, OpenFile> _opened = new();
    private sealed class OpenFile { public string? Path; }

    public int OpenFiles => _disk.OpenFiles;
    public int MaximumOpenFiles => _disk.MaximumOpenFiles;
    public ReusableTask<bool> ExistsAsync(ITorrentManagerFile file) => _disk.ExistsAsync(file);
    public ReusableTask FlushAsync(ITorrentManagerFile file) => _disk.FlushAsync(file);
    public ReusableTask SetMaximumOpenFilesAsync(int maximumOpenFiles) => _disk.SetMaximumOpenFilesAsync(maximumOpenFiles);

    public async ReusableTask<int> ReadAsync(ITorrentManagerFile file, long offset, Memory<byte> buffer) =>
        Prepare(file, writing: false) ? await _disk.ReadAsync(file, offset, buffer) : 0;

    public ReusableTask WriteAsync(ITorrentManagerFile file, long offset, ReadOnlyMemory<byte> buffer)
    {
        Prepare(file, writing: true);
        return _disk.WriteAsync(file, offset, buffer);
    }

    private bool Prepare(ITorrentManagerFile file, bool writing)
    {
        var state = _opened.GetOrCreateValue(file);
        var path = file.FullPath;
        if (state.Path == path) return true;
        var owner = findOwner(path);
        TorrentService.ValidatePayloadPath(owner.SavePath, path, []);
        if (!File.Exists(path))
        {
            if (!writing) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                owner.RecordCreatedPayload(path);
                changed();
            }
            catch (IOException) when (File.Exists(path)) { /* Another creator owns this file. */ }
        }
        state.Path = path;
        return true;
    }

    public ReusableTask CloseAsync(ITorrentManagerFile file)
    {
        _opened.Remove(file);
        return _disk.CloseAsync(file);
    }

    public async ReusableTask MoveAsync(ITorrentManagerFile file, string fullPath, bool overwrite)
    {
        var oldPath = file.FullPath;
        var owner = findOwner(oldPath);
        if (findOwner(fullPath) != owner) throw new IOException(LocalizationResourceManager.Instance["ErrorPayloadConflict"]);
        TorrentService.ValidatePayloadPath(owner.SavePath, fullPath, []);
        _opened.Remove(file);
        var sourceExists = File.Exists(oldPath);
        await _disk.MoveAsync(file, fullPath, overwrite);
        if (sourceExists)
        {
            owner.MoveCreatedPayload(oldPath, fullPath);
            changed();
        }
    }

    public void Dispose() => _disk.Dispose();
}
