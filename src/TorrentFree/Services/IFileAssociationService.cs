namespace TorrentFree.Services;

public interface IFileAssociationService
{
    bool IsSupported { get; }

    Task<bool> IsAssociatedAsync();

    Task<bool> AssociateAsync();

    Task<bool> RemoveAssociationAsync();
}
