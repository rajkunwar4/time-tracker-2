namespace TimeTrack.Core.Sync;

/// <summary>
/// Opens and closes a time-boxed internet window so the occasionally-connected client can
/// reach the cloud API only when it needs to sync. The Windows implementation toggles the
/// system proxy on/off; a no-op implementation is used in dev or when disabled.
///
/// <para>Open/Close are idempotent. The implementation is responsible for a failsafe
/// (auto-close after a timeout / on process exit) so a crash can't leave internet open.</para>
/// </summary>
public interface IInternetWindow
{
    /// <summary>Grant internet (open the window).</summary>
    Task OpenAsync(CancellationToken ct = default);

    /// <summary>Revoke internet (close the window).</summary>
    Task CloseAsync(CancellationToken ct = default);
}

/// <summary>A window that does nothing — used in dev or when the proxy feature is disabled.</summary>
public sealed class NoOpInternetWindow : IInternetWindow
{
    public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
}
