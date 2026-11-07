namespace TorrentFree.Services;

public sealed class SavedStateRecoveryException(Exception innerException)
    : IOException("The saved download state could not be read and no valid recovery copy is available. Existing state files were preserved.", innerException);
