#nullable enable

namespace YMusic.WinUI.Services.Interfaces;

public interface IFckCensorService
{
    Task InitializeAsync();

    bool IsReplaced(string trackId);
    string? GetReplacementUrl(string trackId);

    Task<bool> ReplaceTrackWithFileAsync(string trackId, string sourceFilePath);
    Task<bool> RemoveReplacementAsync(string trackId);
    Task<bool> ReportCensorshipAsync(string trackId, bool replaced = false);
    bool IsReported(string trackId);

    event EventHandler<string>? TrackReplacementChanged;
}
