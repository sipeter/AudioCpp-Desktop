using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioCppDesktop.Models;
using AudioCppDesktop.Services;
using Forms = System.Windows.Forms;

namespace AudioCppDesktop;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "gui_settings.json");
    private readonly ServerManager _server = new();
    private readonly ModelDownloader _downloader = new();
    private readonly Dictionary<string, CancellationTokenSource> _downloadTokens = new();
    private readonly ObservableCollection<ModelEntry> _catalog = new(ModelCatalog.CreateDefaultCatalog());
    private readonly ObservableCollection<InstalledModelInfo> _installedModels = new();
    private readonly ObservableCollection<VoiceItem> _voices = new();

    private GuiSettings _settings = new();
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayShowItem;
    private Forms.ToolStripMenuItem? _trayWebItem;
    private Forms.ToolStripMenuItem? _trayExitItem;
    private bool _forceExit;
    private bool _isShuttingDown;
    private bool _initializing = true;
    private string _lastDefaultTestText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        ModelsGrid.ItemsSource = _catalog;
        InstalledModelChips.ItemsSource = _installedModels;
        VoicesGrid.ItemsSource = _voices;

        _server.LogReceived += line =>
        {
            if (_isShuttingDown) return;
            Dispatcher.BeginInvoke(() => { if (!_isShuttingDown) AppendLog(line); });
        };
        _server.RunningStateChanged += running =>
        {
            if (_isShuttingDown) return;
            Dispatcher.BeginInvoke(() => { if (!_isShuttingDown) UpdateRunningUi(running); });
        };

        LoadSettings();
        L10n.SetLanguage(_settings.Language);
        InitTrayIcon();
        ApplySettingsToUi();
        ApplyLanguage();
        _initializing = false;

        RefreshWorkspaceState();
        AppendLog(L10n.Get("msg.ready"));
    }

    private void InitTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        _trayShowItem = new Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.BeginInvoke(RestoreFromTray));
        _trayWebItem = new Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.BeginInvoke(() => OpenWebUi_Click(this, new RoutedEventArgs())));
        _trayExitItem = new Forms.ToolStripMenuItem("", null, (_, _) => Dispatcher.BeginInvoke(ExitCompletely));
        menu.Items.Add(_trayShowItem);
        menu.Items.Add(_trayWebItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_trayExitItem);

        Icon trayIcon = SystemIcons.Application;
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            {
                trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? SystemIcons.Application;
            }
        }
        catch
        {
            trayIcon = SystemIcons.Application;
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "AudioCpp Desktop",
            Icon = trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
    }

    private static string AppVersionString
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "v0.1.0" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private void ApplyLanguage()
    {
        var baseName = L10n.Get("app.name");
        Title = $"{baseName} {AppVersionString}";
        TxtAppTitle.Text = baseName;
        TxtVersionBadge.Text = AppVersionString;
        LblRootDir.Text = L10n.Get("header.runtime_dir");
        BtnBrowseRoot.Content = L10n.Get("header.browse");
        BtnRefreshHeader.Content = L10n.Get("header.refresh");
        BtnStart.Content = L10n.Get("header.start");
        BtnStop.Content = L10n.Get("header.stop");
        BtnOpenWebUi.Content = L10n.Get("header.open_webui");

        TabServer.Header = L10n.Get("tab.server");
        TabModels.Header = L10n.Get("tab.models");
        TabVoices.Header = L10n.Get("tab.voices");
        TabAbout.Header = L10n.Get("tab.about");

        AboutTitle.Text = baseName;
        AboutVersionBadge.Text = AppVersionString;
        AboutDesc.Text = L10n.Get("about.desc");
        AboutLinksHeader.Text = L10n.Get("about.links_header");
        LblCardSelf.Text = L10n.Get("about.link_self");
        LblCardEngine.Text = L10n.Get("about.link_engine");
        LblCardHf.Text = L10n.Get("about.link_hf");
        LblCardMs.Text = L10n.Get("about.link_ms");
        AboutLicenseHeader.Text = L10n.Get("about.license_header");
        AboutLicenseBody.Text = L10n.Get("about.license_body");
        AboutClientsLabel.Text = L10n.Get("about.clients_label");

        LblServerSettings.Text = L10n.Get("server.settings_title");
        LblBackend.Text = L10n.Get("server.backend");
        CbiCuda.Content = L10n.Get("server.backend_cuda");
        CbiCpu.Content = L10n.Get("server.backend_cpu");
        LblPort.Text = L10n.Get("server.port");
        LblMaxModels.Text = L10n.Get("server.max_models");
        LblMaxModelsHint.Text = L10n.Get("server.max_models_hint");
        ChkMinimizeToTray.Content = L10n.Get("server.minimize_tray");

        LblPhoneConnect.Text = L10n.Get("api.title");
        LblLanUrl.Text = L10n.Get("api.lan_url");
        BtnCopyLanUrl.Content = L10n.Get("api.copy_url");
        LblModelChipsHint.Text = L10n.Get("api.chips_hint");
        NoModelsHint.Text = L10n.Get("api.no_models_hint");

        LblTestModel.Text = L10n.Get("test.model");
        LblTestVoice.Text = L10n.Get("test.voice");
        var newDefaultTest = L10n.Get("test.default_text");
        if (string.IsNullOrWhiteSpace(TxtTestInput.Text) || TxtTestInput.Text == _lastDefaultTestText || TxtTestInput.Text == "你好，欢迎使用 CloneTTS 电脑端音频合成服务。")
        {
            TxtTestInput.Text = newDefaultTest;
        }
        _lastDefaultTestText = newDefaultTest;
        BtnTestSpeak.Content = L10n.Get("test.speak");

        LblLiveLogs.Text = L10n.Get("logs.title");
        BtnClearLogs.Content = L10n.Get("logs.clear");

        LblDlSource.Text = L10n.Get("models.source");
        CbiMs.Content = L10n.Get("models.source_ms");
        CbiHf.Content = L10n.Get("models.source_hf");
        BtnBrowseModelsDir.Content = L10n.Get("models.change_dir");
        BtnOpenModelsDir.Content = L10n.Get("models.open_dir");
        BtnRefreshModels.Content = L10n.Get("header.refresh");

        ColModelName.Header = L10n.Get("models.col_name");
        ColModelId.Header = L10n.Get("models.col_id");
        ColModelSize.Header = L10n.Get("models.col_size");
        ColModelPromptReq.Header = L10n.Get("models.col_req");
        ColModelDesc.Header = L10n.Get("models.col_desc");
        ColModelStatus.Header = L10n.Get("models.col_status");
        ColModelAction.Header = L10n.Get("models.col_action");

        BtnImportVoice.Content = L10n.Get("voices.import");
        BtnSavePrompts.Content = L10n.Get("voices.save");
        LblAsrModel.Text = L10n.Get("voices.asr_label");
        BtnTranscribeEmpty.Content = L10n.Get("voices.transcribe_empty");
        BtnBrowseVoiceDir.Content = L10n.Get("voices.change_dir");
        BtnOpenVoiceDir.Content = L10n.Get("voices.open_dir");
        BtnRefreshVoices.Content = L10n.Get("header.refresh");

        ColVoiceName.Header = L10n.Get("voices.col_name");
        ColVoiceSize.Header = L10n.Get("voices.col_size");
        ColVoicePrompt.Header = L10n.Get("voices.col_prompt");
        ColVoiceAsr.Header = L10n.Get("voices.col_asr");
        ColVoicePlay.Header = L10n.Get("voices.col_play");

        if (_trayShowItem is not null) _trayShowItem.Text = L10n.Get("tray.show");
        if (_trayWebItem is not null) _trayWebItem.Text = L10n.Get("tray.webui");
        if (_trayExitItem is not null) _trayExitItem.Text = L10n.Get("tray.exit");
        if (_trayIcon is not null) _trayIcon.Text = baseName;

        foreach (var entry in _catalog) entry.NotifyLanguageChanged();
        UpdateRunningUi(_server.IsRunning);
        RefreshWorkspaceState();
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (CmbLanguage.SelectedValue is string lang)
        {
            _settings.Language = lang;
            L10n.SetLanguage(lang);
            SaveSettings();
            ApplyLanguage();
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitCompletely()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        _forceExit = true;

        foreach (var cts in _downloadTokens.Values)
        {
            try { cts.Cancel(); } catch { }
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        try { _server.Dispose(); } catch { }
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_forceExit && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(2000, L10n.Get("app.name"), L10n.Get("tray.minimized_tip"), Forms.ToolTipIcon.Info);
            return;
        }
        ExitCompletely();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var loaded = JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(_settingsPath));
                if (loaded is not null) _settings = loaded;
            }
        }
        catch
        {
            _settings = new GuiSettings();
        }

        if (string.IsNullOrWhiteSpace(_settings.Language))
        {
            _settings.Language = L10n.CurrentLang;
        }

        var detected = ServerManager.DetectPortableRoot(_settings.PortableRootDir);
        if (detected is not null) _settings.PortableRootDir = detected;
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Ignore write errors in read-only locations
        }
    }

    private void ApplySettingsToUi()
    {
        CmbLanguage.ItemsSource = L10n.AvailableLanguages;
        CmbLanguage.SelectedValue = L10n.CurrentLang;
        CmbBackend.SelectedIndex = string.Equals(_settings.Backend, "cpu", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CmbDownloadSource.SelectedIndex = string.Equals(_settings.DownloadSource, "huggingface", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        TxtPort.Text = _settings.Port.ToString();
        TxtMaxModels.Text = _settings.MaxLoadedModels.ToString();
        ChkMinimizeToTray.IsChecked = _settings.MinimizeToTrayOnClose;
        UpdateLanUrlBox();
    }

    private void ReadSettingsFromUi()
    {
        if (_initializing) return;
        if (CmbBackend.SelectedItem is ComboBoxItem bi && bi.Tag is string backend) _settings.Backend = backend;
        if (CmbDownloadSource.SelectedItem is ComboBoxItem di && di.Tag is string dl) _settings.DownloadSource = dl;
        if (int.TryParse(TxtPort.Text.Trim(), out var port) && port is >= 1024 and <= 65535) _settings.Port = port;
        if (int.TryParse(TxtMaxModels.Text.Trim(), out var maxM) && maxM is >= 1 and <= 8) _settings.MaxLoadedModels = maxM;
        _settings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked == true;
        UpdateLanUrlBox();
        SaveSettings();
    }

    private void UpdateLanUrlBox()
    {
        TxtLanUrl.Text = $"http://{ServerManager.GetLanIpAddress()}:{_settings.Port}";
    }

    private string ActiveModelsDir => ServerManager.ResolveModelsDir(_settings.PortableRootDir, _settings.ModelsDir);
    private string ActiveVoiceDir => ServerManager.ResolveVoiceDir(_settings.PortableRootDir, _settings.VoiceDir);

    private void RefreshWorkspaceState()
    {
        var rootValid = ServerManager.IsValidPortableRoot(_settings.PortableRootDir);
        RootPathText.Text = rootValid ? _settings.PortableRootDir : L10n.Get("msg.root_not_found");
        RootPathText.Foreground = rootValid
            ? (System.Windows.Media.Brush)FindResource("TextPrimary")
            : (System.Windows.Media.Brush)FindResource("AccentRed");

        _installedModels.Clear();
        _voices.Clear();

        if (!rootValid)
        {
            NoModelsHint.Visibility = Visibility.Visible;
            ModelsDirText.Text = string.Empty;
            VoiceDirHintText.Text = L10n.Get("msg.select_root_first");
            return;
        }

        var modelsDir = ActiveModelsDir;
        var voiceDir = ActiveVoiceDir;
        ModelsDirText.Text = L10n.Format("models.dir_label", modelsDir);
        ModelsDirText.ToolTip = modelsDir;
        VoiceDirHintText.Text = L10n.Format("voices.dir_hint", voiceDir);

        var installed = ModelCatalog.ScanInstalledModels(modelsDir);
        foreach (var m in installed) _installedModels.Add(m);

        var installedFiles = new HashSet<string>(installed.Select(m => m.FileName), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _catalog)
        {
            if (entry.IsDownloading) continue;
            entry.IsInstalled = installedFiles.Contains(entry.FileName);
            entry.DownloadProgress = entry.IsInstalled ? 100 : 0;
            entry.StatusText = entry.IsInstalled ? L10n.Get("models.status_installed") : L10n.Get("models.status_not_downloaded");
        }

        NoModelsHint.Visibility = _installedModels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var ttsIds = _installedModels.Where(m => !string.Equals(m.Task, "asr", StringComparison.OrdinalIgnoreCase)).Select(m => m.Id).ToList();
        var prevModel = CmbTestModel.SelectedItem as string;
        CmbTestModel.ItemsSource = ttsIds;
        if (prevModel is not null && ttsIds.Contains(prevModel))
            CmbTestModel.SelectedItem = prevModel;
        else if (ttsIds.Count > 0)
            CmbTestModel.SelectedIndex = 0;

        var asrIds = _installedModels.Where(m => string.Equals(m.Task, "asr", StringComparison.OrdinalIgnoreCase)).Select(m => m.Id).ToList();
        var prevAsr = CmbAsrModel.SelectedItem as string;
        CmbAsrModel.ItemsSource = asrIds;
        if (prevAsr is not null && asrIds.Contains(prevAsr))
            CmbAsrModel.SelectedItem = prevAsr;
        else if (asrIds.Count > 0)
            CmbAsrModel.SelectedIndex = 0;

        foreach (var v in VoiceLibraryManager.LoadVoices(voiceDir)) _voices.Add(v);
        var prevVoice = CmbTestVoice.SelectedItem as string;
        CmbTestVoice.ItemsSource = _voices.Select(v => v.Name).ToList();
        if (prevVoice is not null && _voices.Any(v => v.Name == prevVoice))
            CmbTestVoice.SelectedItem = prevVoice;
        else if (_voices.Count > 0)
            CmbTestVoice.SelectedIndex = 0;

        UpdateLanUrlBox();
    }

    private void UpdateRunningUi(bool running)
    {
        BtnStart.IsEnabled = !running;
        BtnStop.IsEnabled = running;
        StatusText.Text = running ? L10n.Get("status.running") : L10n.Get("status.stopped");
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(running ? "AccentGreen" : "AccentRed");
        StatusBadge.Background = new SolidColorBrush(running ? System.Windows.Media.Color.FromRgb(220, 252, 231) : System.Windows.Media.Color.FromRgb(254, 226, 226));
    }

    private void AppendLog(string line)
    {
        TxtLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        if (TxtLogs.Text.Length > 80_000)
        {
            TxtLogs.Text = TxtLogs.Text[^40_000..];
        }
        TxtLogs.ScrollToEnd();
    }

    private void SettingsInput_Changed(object sender, RoutedEventArgs e) => ReadSettingsFromUi();
    private void SettingsInput_LostFocus(object sender, RoutedEventArgs e) => ReadSettingsFromUi();

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new Forms.FolderBrowserDialog
        {
            Description = L10n.Get("msg.browse_root"),
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(_settings.PortableRootDir)) dlg.SelectedPath = _settings.PortableRootDir;
        if (dlg.ShowDialog() == Forms.DialogResult.OK)
        {
            if (!ServerManager.IsValidPortableRoot(dlg.SelectedPath))
            {
                System.Windows.MessageBox.Show(L10n.Get("msg.invalid_root"), L10n.Get("msg.invalid_dir_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.PortableRootDir = dlg.SelectedPath;
            SaveSettings();
            RefreshWorkspaceState();
            AppendLog($"[INFO] Runtime dir: {_settings.PortableRootDir}");
        }
    }

    private void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        L10n.ReloadLocales();
        _initializing = true;
        CmbLanguage.ItemsSource = L10n.AvailableLanguages;
        CmbLanguage.SelectedValue = L10n.CurrentLang;
        _initializing = false;
        RefreshWorkspaceState();
        AppendLog("[INFO] Refreshed.");
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        ReadSettingsFromUi();
        if (!ServerManager.IsValidPortableRoot(_settings.PortableRootDir))
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.select_root_first"), L10n.Get("msg.notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnStart.IsEnabled = false;
        StatusText.Text = L10n.Get("status.starting");
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentAmber");
        StatusBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 243, 199));

        var ok = await _server.StartAsync(_settings.PortableRootDir, ActiveModelsDir, ActiveVoiceDir, _settings);
        UpdateRunningUi(_server.IsRunning);
        if (!ok && !_server.IsRunning)
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.start_failed_body"), L10n.Get("msg.start_failed_title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopServer_Click(object sender, RoutedEventArgs e) => _server.Stop();

    private void OpenWebUi_Click(object sender, RoutedEventArgs e)
    {
        var url = $"http://127.0.0.1:{_settings.Port}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void CopyLanUrl_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(TxtLanUrl.Text);
        AppendLog($"[INFO] Copied URL: {TxtLanUrl.Text}");
    }

    private void CopyModelId_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id })
        {
            System.Windows.Clipboard.SetText(id);
            AppendLog($"[INFO] Copied Model ID: {id}");
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e) => TxtLogs.Clear();

    private async void TestSpeak_Click(object sender, RoutedEventArgs e)
    {
        if (!_server.IsRunning)
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.start_server_first"), L10n.Get("msg.server_stopped_title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var modelId = CmbTestModel.SelectedItem as string;
        var voice = CmbTestVoice.SelectedItem as string ?? "default";
        var text = TxtTestInput.Text.Trim();
        if (string.IsNullOrEmpty(modelId) || string.IsNullOrEmpty(text))
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.select_model_text"), L10n.Get("msg.notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnTestSpeak.IsEnabled = false;
        BtnTestSpeak.Content = L10n.Get("test.synthesizing");
        try
        {
            AppendLog($"[TEST] model={modelId}, voice={voice}");
            var wavBytes = await _server.SynthesizeTestWavAsync(_settings.Port, modelId, voice, text);
            AppendLog($"[TEST] OK ({wavBytes.Length / 1024.0:F1} KB)");
            using var ms = new MemoryStream(wavBytes);
            using var player = new SoundPlayer(ms);
            player.PlaySync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            System.Windows.MessageBox.Show(ex.Message, L10n.Get("msg.synth_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnTestSpeak.IsEnabled = true;
            BtnTestSpeak.Content = L10n.Get("test.speak");
        }
    }

    private async void ModelAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ModelEntry entry }) return;

        if (!ServerManager.IsValidPortableRoot(_settings.PortableRootDir))
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.select_root_first"), L10n.Get("msg.notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (entry.IsDownloading)
        {
            if (_downloadTokens.TryGetValue(entry.FileName, out var existing)) existing.Cancel();
            return;
        }

        if (entry.IsInstalled)
        {
            System.Windows.MessageBox.Show(L10n.Format("msg.already_installed", entry.FileName), L10n.Get("models.status_installed"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReadSettingsFromUi();
        var url = ModelCatalog.BuildDownloadUrl(entry, _settings.DownloadSource);
        var dst = Path.Combine(ActiveModelsDir, entry.FileName);
        var cts = new CancellationTokenSource();
        _downloadTokens[entry.FileName] = cts;
        entry.IsDownloading = true;
        entry.StatusText = L10n.Get("models.status_connecting");
        AppendLog($"[DL] {entry.FileName} <- {url}");

        try
        {
            await _downloader.DownloadAsync(url, dst, (pct, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    entry.DownloadProgress = pct;
                    entry.StatusText = msg;
                });
            }, cts.Token);

            entry.IsInstalled = true;
            entry.StatusText = L10n.Get("models.status_installed");
            AppendLog($"[DL] OK (GGUF verified): {entry.FileName}");
            RefreshWorkspaceState();
        }
        catch (OperationCanceledException)
        {
            entry.StatusText = L10n.Get("models.status_paused");
            AppendLog($"[DL] Paused: {entry.FileName}");
        }
        catch (Exception ex)
        {
            entry.StatusText = L10n.Format("models.status_failed", ex.Message);
            AppendLog($"[ERROR] {entry.FileName}: {ex.Message}");
        }
        finally
        {
            entry.IsDownloading = false;
            _downloadTokens.Remove(entry.FileName);
            cts.Dispose();
        }
    }

    private void BrowseModelsDir_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new Forms.FolderBrowserDialog
        {
            Description = L10n.Get("msg.browse_models"),
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(ActiveModelsDir)) dlg.SelectedPath = ActiveModelsDir;
        if (dlg.ShowDialog() == Forms.DialogResult.OK)
        {
            _settings.ModelsDir = dlg.SelectedPath;
            SaveSettings();
            RefreshWorkspaceState();
            AppendLog($"[INFO] Models dir: {ActiveModelsDir}");
        }
    }

    private void BrowseVoiceDir_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new Forms.FolderBrowserDialog
        {
            Description = L10n.Get("msg.browse_voices"),
            UseDescriptionForTitle = true
        };
        if (Directory.Exists(ActiveVoiceDir)) dlg.SelectedPath = ActiveVoiceDir;
        if (dlg.ShowDialog() == Forms.DialogResult.OK)
        {
            _settings.VoiceDir = dlg.SelectedPath;
            SaveSettings();
            RefreshWorkspaceState();
            AppendLog($"[INFO] Voice dir: {ActiveVoiceDir}");
        }
    }

    private void OpenModelsDir_Click(object sender, RoutedEventArgs e)
    {
        if (!ServerManager.IsValidPortableRoot(_settings.PortableRootDir)) return;
        var dir = ActiveModelsDir;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void OpenVoiceDir_Click(object sender, RoutedEventArgs e)
    {
        if (!ServerManager.IsValidPortableRoot(_settings.PortableRootDir)) return;
        var dir = ActiveVoiceDir;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private async void ImportVoice_Click(object sender, RoutedEventArgs e)
    {
        if (!ServerManager.IsValidPortableRoot(_settings.PortableRootDir))
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.select_root_first"), L10n.Get("msg.notice"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L10n.Get("msg.import_title"),
            Filter = "Audio Files|*.wav;*.mp3;*.m4a;*.flac;*.ogg|All Files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var file in dlg.FileNames)
        {
            try
            {
                var outWav = await VoiceLibraryManager.ImportAudioAsync(_settings.PortableRootDir, ActiveVoiceDir, file);
                AppendLog($"[VOICE] Imported: {Path.GetFileName(outWav)}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] Import failed ({Path.GetFileName(file)}): {ex.Message}");
                System.Windows.MessageBox.Show(ex.Message, L10n.Get("msg.import_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        RefreshWorkspaceState();
    }

    private void SavePrompts_Click(object sender, RoutedEventArgs e)
    {
        if (!ServerManager.IsValidPortableRoot(_settings.PortableRootDir)) return;
        VoicesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        VoicesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        VoiceLibraryManager.SavePromptText(ActiveVoiceDir, _voices);
        AppendLog($"[VOICE] Saved {Path.Combine(ActiveVoiceDir, "prompt_text")} (UTF-8).");
        System.Windows.MessageBox.Show(L10n.Get("msg.saved_prompts"), L10n.Get("msg.saved_title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void TranscribeSingleVoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: VoiceItem item }) return;
        await TranscribeVoicesAsync([item]);
    }

    private async void TranscribeEmpty_Click(object sender, RoutedEventArgs e)
    {
        var targets = _voices.Where(v => string.IsNullOrWhiteSpace(v.PromptText)).ToList();
        if (targets.Count == 0)
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.all_have_prompts"), L10n.Get("msg.notice"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await TranscribeVoicesAsync(targets);
    }

    private async Task TranscribeVoicesAsync(List<VoiceItem> targets)
    {
        var asrModel = CmbAsrModel.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(asrModel))
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.no_asr_body"), L10n.Get("msg.no_asr_title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_server.IsRunning)
        {
            System.Windows.MessageBox.Show(L10n.Get("msg.asr_start_server"), L10n.Get("msg.server_stopped_title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnTranscribeEmpty.IsEnabled = false;
        var okCount = 0;
        try
        {
            foreach (var v in targets)
            {
                AppendLog($"[ASR] Transcribing {v.Name} with {asrModel}...");
                var text = await _server.TranscribeWavAsync(_settings.Port, asrModel, v.FilePath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    v.PromptText = text;
                    okCount++;
                    AppendLog($"[ASR] {v.Name} => {text}");
                }
            }
            if (okCount > 0)
            {
                VoiceLibraryManager.SavePromptText(ActiveVoiceDir, _voices);
                AppendLog($"[ASR] Auto-saved {okCount} transcript(s) to prompt_text.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] ASR failed: {ex.Message}");
            System.Windows.MessageBox.Show(L10n.Format("msg.asr_failed_body", ex.Message), L10n.Get("msg.asr_failed_title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnTranscribeEmpty.IsEnabled = true;
        }
    }

    private void PlayVoiceRef_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: VoiceItem item }) return;
        try
        {
            using var player = new SoundPlayer(item.FilePath);
            player.Play();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, L10n.Get("msg.play_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenExternalLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string url })
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
