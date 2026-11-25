using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioCppDesktop.Services;

namespace AudioCppDesktop.Models;

public sealed class GuiSettings
{
    public string PortableRootDir { get; set; } = string.Empty;
    public string ModelsDir { get; set; } = string.Empty;
    public string VoiceDir { get; set; } = string.Empty;
    public string Backend { get; set; } = "cuda";
    public int Port { get; set; } = 18080;
    public int MaxLoadedModels { get; set; } = 1;
    public int Threads { get; set; } = 4;
    public string DownloadSource { get; set; } = "modelscope";
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public string Language { get; set; } = string.Empty;
}

public sealed class ModelEntry : INotifyPropertyChanged
{
    private bool _isInstalled;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _statusText = string.Empty;

    public required string DisplayName { get; init; }
    public required string ModelId { get; init; }
    public required string FileName { get; init; }
    public required string Family { get; init; }
    public required string Task { get; init; }
    public required string HfSubDir { get; init; }
    public required string SizeLabel { get; init; }
    public required string PromptTextReqKey { get; init; }
    public required string DescriptionKey { get; init; }
    public required string License { get; init; }

    public string PromptTextRequirement => L10n.Get(PromptTextReqKey);
    public string Description => L10n.Get(DescriptionKey);

    public bool IsInstalled
    {
        get => _isInstalled;
        set { if (_isInstalled != value) { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionLabel)); } }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (_isDownloading != value) { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionLabel)); } }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set { if (Math.Abs(_downloadProgress - value) > 0.05) { _downloadProgress = value; OnPropertyChanged(); } }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public string ActionLabel => IsDownloading
        ? L10n.Get("models.action_cancel")
        : (IsInstalled ? L10n.Get("models.action_installed") : L10n.Get("models.action_download"));

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(PromptTextRequirement));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ActionLabel));
        if (!IsDownloading)
        {
            StatusText = IsInstalled ? L10n.Get("models.status_installed") : L10n.Get("models.status_not_downloaded");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class InstalledModelInfo
{
    public required string Id { get; init; }
    public required string Family { get; init; }
    public required string Task { get; init; }
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public Dictionary<string, object> DefaultRequestOptions { get; init; } = new() { ["seed"] = 123 };
}

public sealed class VoiceItem : INotifyPropertyChanged
{
    private string _promptText = string.Empty;

    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string SizeText { get; init; }

    public string PromptText
    {
        get => _promptText;
        set { if (_promptText != value) { _promptText = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
