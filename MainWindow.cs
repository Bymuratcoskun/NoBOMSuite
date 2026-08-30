using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Gtk;
using Adw;
using Gio;
using SanitizerKit.Core.Backups;
using SanitizerKit.Core.Logging;
using SanitizerKit.Core.IPC;
using SanitizerKit.Core.Config;
using SanitizerKit.Core.Locks;
using SanitizerKit.Core.AI;
using SanitizerKit.Core.Scanners;
using SanitizerKit.Core.Caching;
using SanitizerKit.UI.ViewModels;

using Task = System.Threading.Tasks.Task;
using Action = System.Action;
using File = System.IO.File;
using FileInfo = System.IO.FileInfo;

namespace NoBOMSuite.Desktop;

public class MainWindow : Adw.ApplicationWindow
{
    public static MainWindow? Instance { get; private set; }

    private BackgroundWatcher _watcher;
    private IpcServer _ipcServer;
    private readonly FileLockManager _lockManager = new();
    private readonly AiOrchestrator _aiOrchestrator;
    private DashboardView? _dashboardView;

    private readonly HashSet<string> _ignoredDirectories = new(StringComparer.OrdinalIgnoreCase) 
    { 
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build" 
    };

    private readonly HashSet<string> _pendingFixFiles = new();
    private readonly Dictionary<string, string> _fileErrors = new();
    private readonly Dictionary<string, DateTime> _fileScannedTimes = new();
    private volatile bool _scanCancelled = false;
    
    private readonly ConcurrentQueue<Action> _uiActionQueue = new();
    private readonly DispatcherTimer _uiUpdateTimer;

    // UI elements
    private Gtk.CheckButton? _autoFixCheckBox;
    private Gtk.Frame? _dragDropBorder;
    private Gtk.Button? _btnBrowse;
    private Gtk.Entry? _manualPathInput;
    private Gtk.Button? _btnManualScan;
    private Gtk.Button? _btnFixPending;
    private Gtk.Box? _liveConsolePanel;
    private Gtk.ScrolledWindow? _consoleScroller;
    private Gtk.DropDown? _aiProviderComboBox;
    private Gtk.Entry? _apiKeyTextBox;
    private Gtk.Entry? _ollamaEndpointTextBox;
    private Gtk.Entry? _ollamaModelTextBox;
    private Gtk.Entry? _aiTempTextBox;
    private Gtk.CheckButton? _backupEnabledCheckBox;
    private Gtk.CheckButton? _strictOfflineModeCheckBox;

    // Progress Overlay
    private Gtk.Box? _scanProgressOverlay;
    private Gtk.Label? _scanProgressLabel;
    private Gtk.Label? _scanProgressStats;
    private Gtk.ProgressBar? _scanProgressBar;
    private Gtk.Label? _scanProgressFileName;

    public MainWindow() : base()
    {
        Instance = this;

        SetTitle("NoBOMSuite - Merkezi Yönetim Paneli");
        SetDefaultSize(900, 650);

        BuildUi();

        // System theme sync
        Adw.StyleManager.GetDefault().SetColorScheme(Adw.ColorScheme.Default);

        _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiUpdateTimer.Tick += ProcessUiActionQueue;
        _uiUpdateTimer.Start();

        _aiOrchestrator = new AiOrchestrator();
        _aiOrchestrator.OnLogMessage += (msg) => LogToConsole(msg);
        _aiOrchestrator.OnPatchReady += ShowSolutionReviewDialog;

        // Clipboard paste (simulate Ctrl+V in active view if manual input has focus)
        // In GTK, we can handle key events at window level:
        var keyController = Gtk.EventControllerKey.New();
        keyController.OnKeyPressed += (s, e) =>
        {
            if ((e.State & Gdk.ModifierType.ControlMask) != 0 && e.Keyval == (uint)Gdk.Constants.KEY_v)
            {
                _ = HandleClipboardPaste();
                return true;
            }
            return false;
        };
        AddController(keyController);

        _watcher = new BackgroundWatcher(OnFileChange);
        _watcher.StartWatching(Environment.CurrentDirectory);

        _ipcServer = new IpcServer(msg => 
        {
            GLib.Functions.IdleAdd(0, () => 
            {
                if (msg.StartsWith("LOCK|"))
                {
                    var file = msg.Substring(5);
                    _lockManager.TryLock(file, "IDE");
                    LogToConsole($"🔒 IDE Kilidi (Race Condition Önlemi): {Path.GetFileName(file)}");
                }
                else if (msg.StartsWith("UNLOCK|"))
                {
                    var file = msg.Substring(7);
                    _lockManager.Unlock(file, "IDE");
                    LogToConsole($"🔓 IDE Kilidi Açıldı: {Path.GetFileName(file)}");
                }
                else if (msg.StartsWith("DIAGNOSTICS|"))
                {
                    var parts = msg.Split('|', 3);
                    var fileName = parts.Length > 1 ? Path.GetFileName(parts[1]) : "Bilinmeyen Dosya";
                    var errorMsg = parts.Length > 2 ? parts[2] : "Bilinmeyen Sözdizimi Hatası";
                    LogToConsole($"🐛 IDE TEŞHİSİ: {fileName} -> {errorMsg}");
                }
                else
                {
                    LogToConsole($"🔌 IDE Bağlantısı: {msg}");
                }
                return false;
            });
        });
        _ipcServer.Start();

        LoadSettings();
        CheckVsCodeExtension();
        
        // On closing: hide window and run in tray
        this.OnCloseRequest += (sender, args) =>
        {
            this.Hide();
            LogToConsole("GUI Kapatıldı -> Arka Planda Sessiz İzleme Modu Devrede...");
            return true; // Cancel window destruction
        };

        // Welcome tour trigger
        GLib.Functions.IdleAdd(0, () =>
        {
            ShowWelcomeTourIfNeeded();
            return false;
        });
    }

    private void BuildUi()
    {
        var mainBox = Gtk.Box.New(Gtk.Orientation.Vertical, 15);
        mainBox.SetMarginStart(25);
        mainBox.SetMarginEnd(25);
        mainBox.SetMarginTop(25);
        mainBox.SetMarginBottom(25);

        // --- Header Panel ---
        var headerBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 15);
        
        var titleLabel = Gtk.Label.New("");
        titleLabel.SetMarkup("<span size=\"16000\" weight=\"bold\">🛡️ DevGuard Kumanda Merkezi</span>");
        titleLabel.SetHalign(Gtk.Align.Start);
        headerBox.Append(titleLabel);

        _autoFixCheckBox = Gtk.CheckButton.NewWithLabel("Otomatik Onarım Modu");
        _autoFixCheckBox.OnToggled += AutoFix_Changed;
        headerBox.Append(_autoFixCheckBox);

        var actionBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        actionBox.SetHalign(Gtk.Align.End);
        actionBox.SetHexpand(true);

        var btnHook = Gtk.Button.NewWithLabel("🔗 Git Hook Kur");
        btnHook.OnClicked += InstallGitHook_Click;
        btnHook.AddCssClass("suggested-action");
        actionBox.Append(btnHook);

        var btnVsCode = Gtk.Button.NewWithLabel("🧩 VS Code");
        btnVsCode.OnClicked += InstallVsCodeExtension_Click;
        actionBox.Append(btnVsCode);

        var btnPortable = Gtk.Button.NewWithLabel("💾 Taşınabilir");
        btnPortable.OnClicked += ExportPortable_Click;
        actionBox.Append(btnPortable);

        var btnAi = Gtk.Button.NewWithLabel("🪄 AI Çözüm");
        btnAi.OnClicked += AiFix_Click;
        btnAi.AddCssClass("suggested-action");
        actionBox.Append(btnAi);

        headerBox.Append(actionBox);
        mainBox.Append(headerBox);

        // --- Drag & Drop Border Frame ---
        _dragDropBorder = Gtk.Frame.New(null);
        _dragDropBorder.SetVexpand(true);

        var dndBox = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        dndBox.SetHalign(Gtk.Align.Center);
        dndBox.SetValign(Gtk.Align.Center);
        dndBox.SetMarginStart(20);
        dndBox.SetMarginEnd(20);
        dndBox.SetMarginTop(20);
        dndBox.SetMarginBottom(20);

        var folderIcon = Gtk.Label.New("📁");
        folderIcon.SetFontSize(48);
        dndBox.Append(folderIcon);

        var dndLabel = Gtk.Label.New("Taranacak Dosya veya Klasörleri Buraya Sürükleyin");
        dndLabel.SetFontSize(14);
        dndBox.Append(dndLabel);

        _btnBrowse = Gtk.Button.NewWithLabel("Veya Bilgisayardan Gözat...");
        _btnBrowse.OnClicked += BrowseFiles_Click;
        _btnBrowse.AddCssClass("suggested-action");
        _btnBrowse.SetHalign(Gtk.Align.Center);
        dndBox.Append(_btnBrowse);

        var manualBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        manualBox.SetHalign(Gtk.Align.Center);
        manualBox.SetMarginTop(10);

        _manualPathInput = Gtk.Entry.New();
        _manualPathInput.SetPlaceholderText("Veya dosya yolunu buraya yapıştırın...");
        _manualPathInput.SetSizeRequest(350, -1);
        _manualPathInput.OnActivate += ManualScan_Click;
        manualBox.Append(_manualPathInput);

        _btnManualScan = Gtk.Button.NewWithLabel("Tara");
        _btnManualScan.OnClicked += ManualScan_Click;
        _btnManualScan.AddCssClass("suggested-action");
        manualBox.Append(_btnManualScan);

        _btnFixPending = Gtk.Button.NewWithLabel("🛠️ Sorunları Onar (0)");
        _btnFixPending.OnClicked += FixPending_Click;
        _btnFixPending.AddCssClass("destructive-action");
        manualBox.Append(_btnFixPending);

        dndBox.Append(manualBox);
        _dragDropBorder.SetChild(dndBox);

        // Native Drag & Drop
        var dropTarget = Gtk.DropTarget.New(Gdk.FileList.GetGType(), Gdk.DragAction.Copy);
        dropTarget.OnDrop += (sender, args) =>
        {
            var value = args.Value;
            var fileListPtr = value.GetBoxed();
            if (fileListPtr != IntPtr.Zero)
            {
                var currentPtr = NativeMethods.gdk_file_list_get_files(fileListPtr);
                while (currentPtr != IntPtr.Zero)
                {
                    var node = Marshal.PtrToStructure<NativeMethods.GSList>(currentPtr);
                    if (node.Data != IntPtr.Zero)
                    {
                        var pathPtr = NativeMethods.g_file_get_path(node.Data);
                        if (pathPtr != IntPtr.Zero)
                        {
                            var path = Marshal.PtrToStringUTF8(pathPtr);
                            if (!string.IsNullOrEmpty(path))
                            {
                                LogToConsole($"🎯 Sürükle-Bırak Algılandı: {path}");
                                ScanPath(path);
                            }
                            NativeMethods.g_free(pathPtr);
                        }
                    }
                    currentPtr = node.Next;
                }
                return true;
            }
            return false;
        };
        _dragDropBorder.AddController(dropTarget);
        mainBox.Append(_dragDropBorder);

        // --- Tabs Container (Notebook) ---
        var notebook = Gtk.Notebook.New();
        notebook.SetSizeRequest(-1, 230);

        // Tab 1: Dashboard
        _dashboardView = new DashboardView();
        notebook.AppendPage(_dashboardView, Gtk.Label.New("📊 Pano"));

        // Tab 2: Console
        var consoleBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
        consoleBox.SetMarginStart(10);
        consoleBox.SetMarginEnd(10);
        consoleBox.SetMarginTop(10);
        consoleBox.SetMarginBottom(10);

        _liveConsolePanel = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
        
        _consoleScroller = Gtk.ScrolledWindow.New();
        _consoleScroller.SetChild(_liveConsolePanel);
        _consoleScroller.SetVexpand(true);
        consoleBox.Append(_consoleScroller);

        var btnClearConsole = Gtk.Button.NewWithLabel("Konsolu Temizle");
        btnClearConsole.OnClicked += ClearConsole_Click;
        btnClearConsole.SetHalign(Gtk.Align.End);
        consoleBox.Append(btnClearConsole);

        notebook.AppendPage(consoleBox, Gtk.Label.New("📟 Canlı Konsol"));

        // Tab 3: Log Database
        var logViewer = new LogViewerView();
        notebook.AppendPage(logViewer, Gtk.Label.New("🗃️ Log Veritabanı"));

        // Tab 4: Settings
        var settingsScroll = Gtk.ScrolledWindow.New();
        var settingsBox = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
        settingsBox.SetMarginStart(15);
        settingsBox.SetMarginEnd(15);
        settingsBox.SetMarginTop(15);
        settingsBox.SetMarginBottom(15);

        var prefGroup = Adw.PreferencesGroup.New();
        prefGroup.SetTitle("Genel Ayarlar");

        var rowProvider = Adw.ActionRow.New();
        rowProvider.SetTitle("AI Sağlayıcı");
        string[] providers = ["OpenAI", "Ollama"];
        _aiProviderComboBox = Gtk.DropDown.NewFromStrings(providers);
        rowProvider.AddSuffix(_aiProviderComboBox);
        prefGroup.Add(rowProvider);

        var rowApiKey = Adw.ActionRow.New();
        rowApiKey.SetTitle("OpenAI API Key");
        _apiKeyTextBox = Gtk.Entry.New();
        _apiKeyTextBox.SetVisibility(false);
        rowApiKey.AddSuffix(_apiKeyTextBox);
        prefGroup.Add(rowApiKey);

        var rowOllamaEnd = Adw.ActionRow.New();
        rowOllamaEnd.SetTitle("Ollama Endpoint");
        _ollamaEndpointTextBox = Gtk.Entry.New();
        rowOllamaEnd.AddSuffix(_ollamaEndpointTextBox);
        prefGroup.Add(rowOllamaEnd);

        var rowOllamaModel = Adw.ActionRow.New();
        rowOllamaModel.SetTitle("Ollama Model");
        _ollamaModelTextBox = Gtk.Entry.New();
        rowOllamaModel.AddSuffix(_ollamaModelTextBox);
        prefGroup.Add(rowOllamaModel);

        var rowTemp = Adw.ActionRow.New();
        rowTemp.SetTitle("AI Sıcaklık (0.0 - 1.0)");
        _aiTempTextBox = Gtk.Entry.New();
        rowTemp.AddSuffix(_aiTempTextBox);
        prefGroup.Add(rowTemp);

        var checkRowBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 20);
        _backupEnabledCheckBox = Gtk.CheckButton.NewWithLabel("Onarım Öncesi Yedek Al");
        _strictOfflineModeCheckBox = Gtk.CheckButton.NewWithLabel("Katı Çevrimdışı Mod");
        checkRowBox.Append(_backupEnabledCheckBox);
        checkRowBox.Append(_strictOfflineModeCheckBox);

        var rowChecks = Adw.ActionRow.New();
        rowChecks.SetTitle("Güvenlik ve Yedekleme");
        rowChecks.AddSuffix(checkRowBox);
        prefGroup.Add(rowChecks);

        var colorRowBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        AddColorButton(colorRowBox, "#89B4FA", "Safir Mavisi");
        AddColorButton(colorRowBox, "#A6E3A1", "Zümrüt Yeşili");
        AddColorButton(colorRowBox, "#CBA6F7", "Kraliyet Moru");
        AddColorButton(colorRowBox, "#F38BA8", "Gül Pembesi");
        AddColorButton(colorRowBox, "#F9E2AF", "Altın Sarısı");
        AddColorButton(colorRowBox, "#89DCEB", "Buz Mavisi");

        var rowColors = Adw.ActionRow.New();
        rowColors.SetTitle("Vurgu Rengi");
        rowColors.AddSuffix(colorRowBox);
        prefGroup.Add(rowColors);

        settingsBox.Append(prefGroup);

        var btnSaveSettings = Gtk.Button.NewWithLabel("⚙️ Ayarları Kaydet");
        btnSaveSettings.OnClicked += SaveSettings_Click;
        btnSaveSettings.AddCssClass("suggested-action");
        settingsBox.Append(btnSaveSettings);

        settingsScroll.SetChild(settingsBox);
        notebook.AppendPage(settingsScroll, Gtk.Label.New("⚙️ Ayarlar"));

        mainBox.Append(notebook);

        // --- Bottom Progress Overlay ---
        _scanProgressOverlay = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        _scanProgressOverlay.SetMarginTop(10);
        _scanProgressOverlay.SetVisible(false);

        var progressDetails = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        progressDetails.SetHexpand(true);

        var progressHeader = Gtk.Box.New(Gtk.Orientation.Horizontal, 10);
        _scanProgressLabel = Gtk.Label.New("Taranıyor...");
        _scanProgressLabel.SetFontWeight(Pango.Weight.Medium);
        _scanProgressStats = Gtk.Label.New("0 / 0 dosya");
        _scanProgressStats.AddCssClass("dim-label");

        progressHeader.Append(_scanProgressLabel);
        progressHeader.Append(_scanProgressStats);
        progressDetails.Append(progressHeader);

        _scanProgressBar = Gtk.ProgressBar.New();
        _scanProgressBar.SetFraction(0.0);
        progressDetails.Append(_scanProgressBar);

        _scanProgressFileName = Gtk.Label.New("");
        _scanProgressFileName.SetHalign(Gtk.Align.Start);
        _scanProgressFileName.AddCssClass("dim-label");
        _scanProgressFileName.SetFontSize(10);
        progressDetails.Append(_scanProgressFileName);

        _scanProgressOverlay.Append(progressDetails);

        var btnCancelScan = Gtk.Button.NewWithLabel("✕");
        btnCancelScan.OnClicked += CancelScan_Click;
        _scanProgressOverlay.Append(btnCancelScan);

        mainBox.Append(_scanProgressOverlay);

        SetContent(mainBox);
    }

    private void AddColorButton(Gtk.Box box, string colorHex, string tooltip)
    {
        var btn = Gtk.Button.New();
        btn.SetSizeRequest(32, 32);
        btn.SetTooltipText(tooltip);
        
        // CSS specific to color hex
        var provider = Gtk.CssProvider.New();
        provider.LoadFromString($@"
            button {{
                background-color: {colorHex};
                border-radius: 6px;
            }}
        ");
        btn.GetStyleContext().AddProvider(provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);

        btn.OnClicked += (s, e) =>
        {
            ApplyAccentColor(colorHex);
            var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
            var config = BomConfigManager.LoadConfig(configPath);
            config.AccentColor = colorHex;
            BomConfigManager.SaveConfig(configPath, config);
            LogToConsole($"🎨 Vurgu rengi değiştirildi: {colorHex}");
        };

        box.Append(btn);
    }

    private void ApplyAccentColor(string colorHex)
    {
        try
        {
            var provider = Gtk.CssProvider.New();
            provider.LoadFromString($@"
                .suggested-action {{
                    background-color: {colorHex};
                    color: #11111B;
                }}
            ");
            Gtk.StyleContext.AddProviderForDisplay(Gdk.Display.GetDefault()!, provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
        }
        catch (Exception ex)
        {
            LogToConsole($"⚠️ Renk uygulanırken hata: {ex.Message}");
        }
    }

    private void ShowScanProgress(int current, int total, string fileName)
    {
        _uiActionQueue.Enqueue(() =>
        {
            if (_scanProgressOverlay != null) _scanProgressOverlay.SetVisible(true);
            if (_scanProgressLabel != null) _scanProgressLabel.SetText("Taranıyor...");
            if (_scanProgressStats != null) _scanProgressStats.SetText($"{current} / {total} dosya");
            if (_scanProgressBar != null)
            {
                double fraction = total > 0 ? (double)current / total : 0.0;
                _scanProgressBar.SetFraction(fraction);
            }
            if (_scanProgressFileName != null) _scanProgressFileName.SetText(fileName);
        });
    }

    private void HideScanProgress()
    {
        _uiActionQueue.Enqueue(() =>
        {
            if (_scanProgressOverlay != null) _scanProgressOverlay.SetVisible(false);
        });
    }

    public void CancelScan_Click(object? sender, EventArgs e)
    {
        _scanCancelled = true;
        HideScanProgress();
        LogToConsole("⏹️ Tarama iptal edildi.");
    }

    private List<string> CollectFilesRecursive(string directoryPath)
    {
        var files = new List<string>();
        try
        {
            files.AddRange(Directory.GetFiles(directoryPath));
            foreach (var dir in Directory.GetDirectories(directoryPath))
            {
                string dirName = Path.GetFileName(dir);
                if (!_ignoredDirectories.Contains(dirName))
                    files.AddRange(CollectFilesRecursive(dir));
            }
        }
        catch { }
        return files;
    }

    private void ScanPath(string path) => _ = ScanPathAsync(path);

    private async Task ScanPathAsync(string path)
    {
        if (File.Exists(path))
        {
            ScanFile(path);
        }
        else if (Directory.Exists(path))
        {
            LogToConsole($"📁 Klasör Tarama Başlatıldı: {path}");
            _scanCancelled = false;

            var files = await Task.Run(() => CollectFilesRecursive(path));
            int total = files.Count;

            if (total == 0)
            {
                LogToConsole("⚠️ Taranacak dosya bulunamadı.");
                return;
            }

            ShowScanProgress(0, total, "");
            int processed = 0;

            foreach (var file in files)
            {
                if (_scanCancelled) break;

                ScanFile(file);
                processed++;

                if (processed % 5 == 0 || processed == total)
                    ShowScanProgress(processed, total, Path.GetFileName(file));

                if (processed % 15 == 0)
                    await Task.Delay(1);
            }

            HideScanProgress();
            LogToConsole($"✅ Klasör Tarama Tamamlandı: {processed} dosya tarandı.");
        }
    }

    private void CheckVsCodeExtension()
    {
        Task.Run(() => 
        {
            try
            {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string vscodeExtDir = Path.Combine(userHome, ".vscode", "extensions");
                bool isInstalled = Directory.Exists(vscodeExtDir) && Directory.GetDirectories(vscodeExtDir, "*devguard*").Length > 0;

                if (!isInstalled)
                {
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        LogToConsole("🔔 [BİLDİRİM] VS Code eklentisi algılanmadı. Üst menüden 'VS Code' butonunu kullanarak kurabilirsiniz.");
                        return false;
                    });
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"⚠️ [SİSTEM] Eklenti kontrolü sırasında hata: {ex.Message}");
            }
        });
    }

    private void LoadSettings()
    {
        var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));

        if (!string.IsNullOrEmpty(config.AccentColor))
            ApplyAccentColor(config.AccentColor);

        if (_autoFixCheckBox != null)
        {
            _autoFixCheckBox.SetActive(config.AutoFix);
        }

        if (_aiProviderComboBox != null)
        {
            _aiProviderComboBox.SetSelected(config.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ? 1u : 0u);
        }

        if (_apiKeyTextBox != null)
        {
            _apiKeyTextBox.SetText(string.Empty);
            if (!string.IsNullOrEmpty(config.EncryptedApiKey))
            {
                _apiKeyTextBox.SetPlaceholderText("Kayıtlı (Şifrelenmiş API Anahtarı)");
            }
        }

        if (_ollamaEndpointTextBox != null)
        {
            _ollamaEndpointTextBox.SetText(config.OllamaEndpoint);
        }

        if (_ollamaModelTextBox != null)
        {
            _ollamaModelTextBox.SetText(config.OllamaModel);
        }

        if (_aiTempTextBox != null)
        {
            _aiTempTextBox.SetText(config.AiTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (_backupEnabledCheckBox != null)
        {
            _backupEnabledCheckBox.SetActive(config.BackupEnabled);
        }

        if (_strictOfflineModeCheckBox != null)
        {
            _strictOfflineModeCheckBox.SetActive(config.StrictOfflineMode);
        }
    }

    public void SaveSettings_Click(object? sender, EventArgs e)
    {
        try
        {
            var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
            var config = BomConfigManager.LoadConfig(configPath);

            if (_aiProviderComboBox != null)
            {
                config.AiProvider = _aiProviderComboBox.GetSelected() == 1 ? "Ollama" : "OpenAI";
            }

            if (_apiKeyTextBox != null && !string.IsNullOrEmpty(_apiKeyTextBox.GetText()))
            {
                config.EncryptedApiKey = LocalAiFirewall.EncryptApiKey(_apiKeyTextBox.GetText());
                _apiKeyTextBox.SetText(string.Empty);
                _apiKeyTextBox.SetPlaceholderText("Kayıtlı (Şifrelenmiş API Anahtarı)");
            }

            if (_ollamaEndpointTextBox != null)
            {
                config.OllamaEndpoint = _ollamaEndpointTextBox.GetText() ?? "";
            }

            if (_ollamaModelTextBox != null)
            {
                config.OllamaModel = _ollamaModelTextBox.GetText() ?? "";
            }

            if (_aiTempTextBox != null && double.TryParse(_aiTempTextBox.GetText(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double newTemp))
            {
                config.AiTemperature = newTemp;
            }

            if (_backupEnabledCheckBox != null)
            {
                config.BackupEnabled = _backupEnabledCheckBox.GetActive();
            }

            if (_strictOfflineModeCheckBox != null)
            {
                config.StrictOfflineMode = _strictOfflineModeCheckBox.GetActive();
            }

            BomConfigManager.SaveConfig(configPath, config);
            LogToConsole("✅ Ayarlar başarıyla kaydedildi.");
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: Ayarlar kaydedilemedi -> {ex.Message}");
        }
    }

    private async Task HandleClipboardPaste()
    {
        try
        {
            var clipboard = Gdk.Display.GetDefault()!.GetClipboard();
            var text = await Task.Run(() =>
            {
                // Simple clip text retrieval or wait
                return ""; // stubbed clipboard fallback
            });

            // Since GTK clipboard fetching requires async promise wrappers, 
            // if we need clipboard text, we can read it. But for the basic logic,
            // we will provide a warning that clipboard path paste is supported via text box.
            LogToConsole("📋 Clipboard okuma tetiklendi. Metin kutusunu kullanarak yapıştırabilirsiniz.");
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ Clipboard hatası: {ex.Message}");
        }
    }

    private void ProcessUiActionQueue(object? sender, EventArgs e)
    {
        int processedCount = 0;
        while (processedCount < 100 && _uiActionQueue.TryDequeue(out var action))
        {
            action();
            processedCount++;
        }
    }

    public void AutoFix_Changed(object? sender, EventArgs e)
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var config = BomConfigManager.LoadConfig(configPath);
        var cb = sender as CheckButton;
        if (cb != null && config.AutoFix != cb.GetActive())
        {
            config.AutoFix = cb.GetActive();
            BomConfigManager.SaveConfig(configPath, config);
            LogToConsole(config.AutoFix ? "⚙️ Otomatik Onarım Modu AÇIK." : "⚙️ Otomatik Onarım Modu KAPALI.");
        }
    }

    private void UpdateFixButton()
    {
        _uiActionQueue.Enqueue(() =>
        {
            if (_btnFixPending != null)
            {
                _btnFixPending.SetVisible(_pendingFixFiles.Count > 0);
                _btnFixPending.SetLabel($"🛠️ Sorunları Onar ({_pendingFixFiles.Count})");
            }
        });
    }

    public async void FixPending_Click(object? sender, EventArgs e)
    {
        if (_pendingFixFiles.Count == 0) return;

        LogToConsole($"🛠️ Bekleyen {_pendingFixFiles.Count} dosya için toplu onarım başlatılıyor...");

        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var config = BomConfigManager.LoadConfig(configPath);
        bool originalAutoFix = config.AutoFix;
        config.AutoFix = true;
        BomConfigManager.SaveConfig(configPath, config);

        var filesToFix = _pendingFixFiles.ToList();
        _pendingFixFiles.Clear();
        UpdateFixButton();

        foreach (var file in filesToFix)
        {
            if (_fileScannedTimes.TryGetValue(file, out DateTime scannedAt) && File.Exists(file))
            {
                DateTime modifiedAt = File.GetLastWriteTimeUtc(file);
                if (Math.Abs((modifiedAt - scannedAt).TotalSeconds) > 1)
                {
                    var dialog = new ConflictResolutionWindow(file, scannedAt, modifiedAt, this);
                    bool fixAnyway = await dialog.ShowAsync();
                    if (!fixAnyway)
                    {
                        LogToConsole($"⏭️ Çakışma: {Path.GetFileName(file)} atlandı.");
                        continue;
                    }
                    LogToConsole($"⚡ Çakışma: {Path.GetFileName(file)} yeni içerikle onarılıyor.");
                }
            }

            ScanFile(file);
            _fileScannedTimes.Remove(file);
        }

        config.AutoFix = originalAutoFix;
        BomConfigManager.SaveConfig(configPath, config);

        LogToConsole("✅ Toplu onarım tamamlandı.");
    }

    private void ScanFile(string filePath, bool allowAutoFix = true)
    {
        try
        {
            var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (config.ExcludedExtensions.Contains(ext))
            {
                LogToConsole($"⏭️ [ATLANDI] Dışlanan dosya türü: {Path.GetFileName(filePath)}", filePath);
                return;
            }

            if (FileCacheManager.IsCacheValid(filePath, out bool cachedHasIssues))
            {
                if (!cachedHasIssues)
                {
                    LogToConsole($"⚡ [ÖNBELLEK] Değişiklik yok (Temiz): {Path.GetFileName(filePath)}", filePath);
                    _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Temiz (Cache)", "#A6E3A1"));
                    _pendingFixFiles.Remove(filePath);
                    UpdateFixButton();
                    return;
                }
            }

            byte[] content = File.ReadAllBytes(filePath);
            ReadOnlySpan<byte> span = content;
            bool hasIssue = false;
            bool fixBom = false;
            bool fixCrlf = false;
            bool fixGhost = false;
            bool fixNewline = false;
            bool fixTab = false;
            bool fixPassword = false;
            bool fixCustomRule = false;
            List<string> issuesList = new();

            if (new BomScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] UTF-8 BOM Karakteri Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixBom = true;
                issuesList.Add("UTF-8 BOM Karakteri");
            }
            if (new LineEndingScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] CRLF (Windows) Satır Sonu Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixCrlf = true;
                issuesList.Add("CRLF Satır Sonları");
            }
            if (new GhostCharScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] Görünmez Hayalet Karakter Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixGhost = true;
                issuesList.Add("Görünmez Hayalet Karakterler");
            }
            if (new NewlineScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] POSIX Uyumsuz (EOF Newline Yok) Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixNewline = true;
                issuesList.Add("POSIX Uyumsuz Satır Sonu");
            }
            if (new TabScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] Sekme (Tab) Kullanımı Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixTab = true;
                issuesList.Add("Tab Karakteri");
            }
            if (new HardcodedPasswordScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] Doğrudan Yazılmış Şifre (Hardcoded Password) Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixPassword = true;
                issuesList.Add("Koda Gömülü Parola");
            }

            if (config.EnabledModules.TryGetValue("EntropyScanner", out bool isEntropyEnabled) && isEntropyEnabled)
            {
                if (new EntropyScanner().HasIssue(span))
                {
                    LogToConsole($"⚠️ [UYARI] Yüksek Entropili Gizli Veri / API Anahtarı Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                    hasIssue = true;
                    issuesList.Add("Yüksek Entropili API Key / Gizli Veri");
                }
            }

            var regexScanner = new RegexScanner(config.CustomRules);
            string textContentForRegex = Encoding.UTF8.GetString(span.ToArray());
            string? violatedRule = regexScanner.GetFirstViolation(textContentForRegex);
            if (violatedRule != null)
            {
                LogToConsole($"⚠️ [UYARI] Özel Kural (Regex) İhlali Tespit Edildi: {violatedRule}", filePath);
                hasIssue = true;
                fixCustomRule = true;
                issuesList.Add($"Özel Kural İhlali ({violatedRule})");
            }

            if (!hasIssue)
            {
                LogToConsole($"✅ [TEMİZ] Dosya sorunsuz: {Path.GetFileName(filePath)}", filePath);
                _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Temiz", "#A6E3A1"));
                _pendingFixFiles.Remove(filePath);
                _fileErrors.Remove(filePath);
                UpdateFixButton();
                FileCacheManager.UpdateCache(filePath, hasIssues: false);
            }
            else
            {
                _fileErrors[filePath] = string.Join(", ", issuesList);

                if (config.AutoFix && allowAutoFix)
                {
                    _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Onarıldı", "#89B4FA"));
                    LogToConsole($"🛠️ [AUTO-FIX] Otomatik onarım başlatılıyor: {Path.GetFileName(filePath)}", filePath);
                    
                    if (config.BackupEnabled)
                    {
                        var backupMgr = new BackupManager(Environment.CurrentDirectory);
                        backupMgr.BackupFile(filePath);
                        LogToConsole($"🛡️ [YEDEKLEME] Orijinal dosya onarım öncesi güvenliğe alındı.", filePath);
                    }

                    List<byte> outputBytes = new List<byte>(content);
                    
                    if (fixBom) outputBytes.RemoveRange(0, 3);
                    
                    if (fixCrlf || fixGhost || fixTab || fixPassword || fixCustomRule)
                    {
                        string tempText = Encoding.UTF8.GetString(outputBytes.ToArray());
                        if (fixCrlf) tempText = tempText.Replace("\r\n", "\n");
                        if (fixGhost)
                        {
                            tempText = tempText
                                .Replace("\u200B", "")
                                .Replace("\u200C", "")
                                .Replace("\u200D", "")
                                .Replace("\u2060", "")
                                .Replace("\u00AD", "");
                        }
                        if (fixTab) tempText = tempText.Replace("\t", new string(' ', config.TabSize));
                        if (fixPassword)
                        {
                            var regex = new System.Text.RegularExpressions.Regex(@"((password|passwd|pass|secret)\s*[:=]\s*)(['""])(.*?)\3", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            tempText = regex.Replace(tempText, "$1$3[MASKED_BY_DEVGUARD]$3");
                        }
                        if (fixCustomRule)
                        {
                            tempText = regexScanner.ApplyFixes(tempText);
                        }
                        outputBytes = new List<byte>(new UTF8Encoding(false).GetBytes(tempText));
                    }
                    
                    if (fixNewline)
                    {
                        if (outputBytes.Count > 0 && outputBytes[outputBytes.Count - 1] != 0x0A)
                            outputBytes.Add(0x0A);
                    }
                    
                    File.WriteAllBytes(filePath, outputBytes.ToArray());
                    LogToConsole($"✨ [BAŞARILI] Dosya onarıldı ve kaydedildi: {Path.GetFileName(filePath)}", filePath);
                    _pendingFixFiles.Remove(filePath);
                    _fileErrors.Remove(filePath);
                    UpdateFixButton();
                    FileCacheManager.UpdateCache(filePath, hasIssues: false);
                }
                else
                {
                    _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Sorunlu", "#F38BA8"));
                    _pendingFixFiles.Add(filePath);
                    _fileScannedTimes[filePath] = File.GetLastWriteTimeUtc(filePath);
                    UpdateFixButton();
                    FileCacheManager.UpdateCache(filePath, hasIssues: true);
                }
            }
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: {Path.GetFileName(filePath)} okunamadı -> {ex.Message}", filePath);
        }
    }

    public void LogToConsole(string message, string? sourceFile = null)
    {
        Task.Run(() =>
        {
            try
            {
                var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
                
                if (config.EnabledModules.TryGetValue("AutoLogger", out bool isAutoLogEnabled) && isAutoLogEnabled)
                {
                    string logFile = Path.Combine(Environment.CurrentDirectory, "devguard_auto.log");
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(logFile, logEntry);
                }
                
                if (config.EnabledModules.TryGetValue("SqliteLogger", out bool isDbLogEnabled) && isDbLogEnabled)
                {
                    SqliteLogger.Log("INFO", message, sourceFile);
                }
            }
            catch { }
        });

        _uiActionQueue.Enqueue(() =>
        {
            if (_liveConsolePanel != null)
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                string fullMessage = $"[{time}] {message}";
                
                var label = Gtk.Label.New(fullMessage);
                label.SetHalign(Gtk.Align.Start);
                label.SetWrap(true);
                label.AddCssClass("monospace");

                if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                {
                    var gesture = Gtk.GestureClick.New();
                    gesture.OnReleased += (s, e) =>
                    {
                        if (e.NPress == 2)
                        {
                            OpenFileInVsCode(sourceFile);
                        }
                    };
                    label.AddController(gesture);
                    label.SetTooltipText("VS Code ile açmak için çift tıklayın");
                }

                if (message.Contains("❌ HATA") || message.Contains("REDDEDİLDİ") || message.Contains("API BAĞLANTI HATASI"))
                {
                    label.SetMarkup($"<span foreground=\"#F38BA8\">{GLib.Markup.EscapeText(fullMessage)}</span>");
                }
                else if (message.Contains("⚠️") || message.Contains("[UYARI]"))
                {
                    label.SetMarkup($"<span foreground=\"#F9E2AF\">{GLib.Markup.EscapeText(fullMessage)}</span>");
                }
                else
                {
                    label.SetMarkup($"<span foreground=\"#A6E3A1\">{GLib.Markup.EscapeText(fullMessage)}</span>");
                }

                var childToRemove = _liveConsolePanel.GetFirstChild();
                while (childToRemove != null && GetChildrenCount(_liveConsolePanel) >= 500)
                {
                    _liveConsolePanel.Remove(childToRemove);
                    childToRemove = _liveConsolePanel.GetFirstChild();
                }

                _liveConsolePanel.Append(label);

                GLib.Functions.IdleAdd(0, () =>
                {
                    var adj = _consoleScroller?.GetVadjustment();
                    if (adj != null)
                    {
                        adj.SetValue(adj.GetUpper() - adj.GetPageSize());
                    }
                    return false;
                });
            }
        });
    }

    private void OpenFileInVsCode(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "code",
                    Arguments = OperatingSystem.IsWindows() ? $"/c code \"{filePath}\"" : $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ HATA: VS Code açılamadı: {ex.Message}");
            }
        }
    }

    private void OnFileChange(string filePath)
    {
        if (_lockManager.IsLocked(filePath))
        {
            LogToConsole($"⏳ İPTAL EDİLDİ: {Path.GetFileName(filePath)} şu an IDE tarafından işleniyor. Çakışma önlendi.", filePath);
            return;
        }

        LogToConsole($"🔔 (SİSTEM DAEMON) Arka planda dosya değişikliği yakalandı: {Path.GetFileName(filePath)}", filePath);
        
        GLib.Functions.IdleAdd(0, () =>
        {
            ScanFile(filePath, allowAutoFix: false);
            return false;
        });
    }

    public void ClearConsole_Click(object? sender, EventArgs e)
    {
        if (_liveConsolePanel != null)
        {
            var child = _liveConsolePanel.GetFirstChild();
            while (child != null)
            {
                _liveConsolePanel.Remove(child);
                child = _liveConsolePanel.GetFirstChild();
            }
            LogToConsole("🧹 Konsol temizlendi.");
        }
    }

    public void InstallGitHook_Click(object? sender, EventArgs e)
    {
        try
        {
            string targetDir = Environment.CurrentDirectory;
            string gitHooksDir = Path.Combine(targetDir, ".git", "hooks");

            if (!Directory.Exists(gitHooksDir))
            {
                LogToConsole("❌ HATA: Mevcut dizinde '.git' klasörü bulunamadı. Lütfen bir git reposunda çalıştırın.");
                return;
            }

            string preCommitPath = Path.Combine(gitHooksDir, "pre-commit");
            string hookScript = "#!/bin/sh\n" +
                                "echo \"[DevGuard] Pre-commit hook devrede. Kodlar taranıyor...\"\n" +
                                "STAGED_FILES=$(git diff --cached --name-only)\n" +
                                "if [ -z \"$STAGED_FILES\" ]; then\n" +
                                "    echo \"[DevGuard] Taranacak staged dosya bulunamadı.\"\n" +
                                "    exit 0\n" +
                                "fi\n" +
                                "HAS_ERRORS=0\n" +
                                "for file in $STAGED_FILES; do\n" +
                                "    if [ -f \"$file\" ]; then\n" +
                                "        dotnet run --project SanitizerKit.CLI.csproj -- \"$file\"\n" +
                                "        if [ $? -ne 0 ]; then\n" +
                                "            HAS_ERRORS=1\n" +
                                "        fi\n" +
                                "    fi\n" +
                                "done\n" +
                                "if [ $HAS_ERRORS -ne 0 ]; then\n" +
                                "    echo \"❌ [DevGuard] HATA: Commit engellendi! Sorunlu dosyaları düzeltin.\"\n" +
                                "    exit 1\n" +
                                "fi\n" +
                                "echo \"✅ [DevGuard] Tarama başarılı. Her şey temiz, commit'e izin verildi!\"\n" +
                                "exit 0\n";

            File.WriteAllText(preCommitPath, hookScript);

            if (!OperatingSystem.IsWindows())
            {
                var fileInfo = new FileInfo(preCommitPath);
                fileInfo.UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            }

            LogToConsole($"✅ BAŞARILI: Git pre-commit hook oluşturuldu -> {preCommitPath}");
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: Git hook oluşturulamadı -> {ex.Message}");
        }
    }

    public void InstallVsCodeExtension_Click(object? sender, EventArgs e)
    {
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] localVsixCandidates =
        {
            Path.Combine(Environment.CurrentDirectory, "devguard.vsix"),
            Path.Combine(AppContext.BaseDirectory, "devguard.vsix"),
            Path.Combine(userHome, "devguard.vsix"),
        };

        string? localVsixPath = null;
        foreach (var candidate in localVsixCandidates)
        {
            if (File.Exists(candidate)) { localVsixPath = candidate; break; }
        }

        if (localVsixPath != null)
        {
            LogToConsole($"📦 Yerel .vsix dosyası bulundu: {localVsixPath}");
            _ = InstallVsixFromPathAsync(localVsixPath);
        }
        else
        {
            LogToConsole("⚠️ [BİLGİ] Otomatik kurulum için proje dizinine bir 'devguard.vsix' dosyası koyun.");
            LogToConsole("   Alternatif olarak VS Code Marketplace'ten 'DevGuard' aratarak manuel kurabilirsiniz.");
            LogToConsole("   Veya: code --install-extension <vsix-dosya-yolu>");
        }
    }

    private async Task InstallVsixFromPathAsync(string vsixPath)
    {
        try
        {
            LogToConsole("⏳ VS Code eklentisi kuruluyor...");

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "code",
                Arguments = OperatingSystem.IsWindows()
                    ? $"/c code --install-extension \"{vsixPath}\""
                    : $"--install-extension \"{vsixPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                if (process.ExitCode == 0)
                {
                    LogToConsole("✅ BAŞARILI: DevGuard VS Code eklentisi kuruldu.");
                }
                else
                {
                    LogToConsole("❌ HATA: Eklenti kurulurken sorun oluştu.");
                    if (!string.IsNullOrWhiteSpace(output)) LogToConsole($"   Çıktı: {output.Trim()}");
                    if (!string.IsNullOrWhiteSpace(error)) LogToConsole($"   Hata: {error.Trim()}");
                }
            }
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: Kurulum başlatılamadı (Sisteminizde VS Code yüklü mü?) -> {ex.Message}");
        }
    }

    public async void ExportPortable_Click(object? sender, EventArgs e)
    {
        try
        {
            LogToConsole("🚀 Taşınabilir Sürüm Sihirbazı başlatıldı. Hedef klasörü seçin...");
            
            var dialog = new Gtk.FileDialog();
            dialog.SetTitle("Taşınabilir Sürüm Nereye Kaydedilsin?");
            var folder = await dialog.SelectFolderAsync(this);
            string? selectedPath = folder?.GetPath();
            
            if (string.IsNullOrEmpty(selectedPath))
            {
                selectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                LogToConsole($"⚠️ Klasör seçilmedi. Masaüstü varsayılan olarak kullanılıyor.");
            }

            string exportDir = Path.Combine(selectedPath, "NoBOMSuite_Portable");

            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, true);
            }
            Directory.CreateDirectory(exportDir);

            string currentConfigPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
            BomConfigManager.ExportPortableConfig(exportDir, currentConfigPath);
            LogToConsole("  - Ayar dosyası (.bomconfig) ihraç edildi.");

            string desktopExeName = OperatingSystem.IsWindows() ? "NoBOMSuite.Desktop.exe" : "NoBOMSuite.Desktop";
            string[] possibleDesktopPaths = {
                Path.Combine(AppContext.BaseDirectory, desktopExeName),
                Path.Combine(Environment.CurrentDirectory, "NoBOMSuite.Desktop", "bin", "Release", "net10.0", "linux-x64", "publish", desktopExeName),
                Path.Combine(Environment.CurrentDirectory, "artifacts", "desktop", desktopExeName),
                Path.Combine(Environment.CurrentDirectory, "publish-desktop", desktopExeName)
            };

            bool desktopFound = false;
            foreach (var path in possibleDesktopPaths)
            {
                if (File.Exists(path))
                {
                    File.Copy(path, Path.Combine(exportDir, desktopExeName), true);
                    LogToConsole($"  - Masaüstü Aracı ({desktopExeName}) ihraç edildi.");
                    desktopFound = true;
                    break;
                }
            }

            if (!desktopFound)
            {
                LogToConsole("⚠️ [UYARI] Masaüstü aracı bulunamadı. Lütfen önce projeyi 'dotnet publish' ile derleyin.");
            }

            string cliExeName = OperatingSystem.IsWindows() ? "SanitizerKit.CLI.exe" : "SanitizerKit.CLI";
            string[] possibleCliPaths = {
                Path.Combine(AppContext.BaseDirectory, "cli", cliExeName),
                Path.Combine(Environment.CurrentDirectory, "SanitizerKit.CLI", "bin", "Release", "net8.0", "linux-x64", "publish", cliExeName),
                Path.Combine(Environment.CurrentDirectory, "artifacts", "cli", cliExeName),
                Path.Combine(Environment.CurrentDirectory, "publish-cli", cliExeName)
            };

            bool cliFound = false;
            foreach (var path in possibleCliPaths)
            {
                if (File.Exists(path))
                {
                    File.Copy(path, Path.Combine(exportDir, cliExeName), true);
                    LogToConsole($"  - Komut Satırı Aracı ({cliExeName}) ihraç edildi.");
                    cliFound = true;
                    break;
                }
            }

            if (!cliFound)
            {
                LogToConsole("⚠️ [UYARI] CLI aracı bulunamadı. Lütfen önce projeyi 'dotnet publish' ile derleyin.");
            }

            string wasmSourceDir = Path.Combine(Environment.CurrentDirectory, "SanitizerKit.Wasm", "bin", "Release", "net8.0", "browser-wasm", "AppBundle");
            if (Directory.Exists(wasmSourceDir))
            {
                string wasmExportDir = Path.Combine(exportDir, "wasm");
                CopyDirectory(wasmSourceDir, wasmExportDir);
                LogToConsole("  - WebAssembly (Wasm) test arayüzü ihraç edildi.");
            }
            else
            {
                LogToConsole($"⚠️ [UYARI] Wasm dosyaları bulunamadı: {wasmSourceDir}");
            }

            LogDatabaseManager.InitializeDatabase(Path.Combine(exportDir, "devguard_logs.db"));
            LogToConsole("  - SQLite Log Veritabanı (devguard_logs.db) altyapısı oluşturuldu.");

            LogToConsole($"✅ BAŞARILI: Taşınabilir sürüm başarıyla oluşturuldu -> {exportDir}");
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: Taşınabilir sürüm ihraç edilemedi -> {ex.Message}");
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    private void ShowSolutionReviewDialog(string patchJson, string filePath, string originalCode)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            _ = ShowSolutionReviewDialogAsync(patchJson, filePath, originalCode);
            return false;
        });
    }

    private async Task ShowSolutionReviewDialogAsync(string patchJson, string filePath, string originalCode)
    {
        var window = new SolutionReviewWindow(patchJson, filePath, originalCode, this);
        var result = await window.ShowAsync();
        if (result)
        {
            LogToConsole("✅ [SİSTEM] Kullanıcı yamayı onayladı ve kod başarıyla uygulandı.");
            
            try
            {
                using var doc = JsonDocument.Parse(patchJson);
                if (doc.RootElement.TryGetProperty("suggestedRecipe", out var recipe) && recipe.ValueKind == JsonValueKind.Object)
                {
                    string ruleName = recipe.TryGetProperty("ruleName", out var rn) ? rn.GetString() ?? "AI_Rule" : "AI_Rule";
                    string pattern = recipe.TryGetProperty("regexPattern", out var rp) ? rp.GetString() ?? "" : "";
                    string replacement = recipe.TryGetProperty("replacement", out var rep) ? rep.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(pattern))
                    {
                        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
                        var config = BomConfigManager.LoadConfig(configPath);
                        config.CustomRules[pattern] = replacement;
                        BomConfigManager.SaveConfig(configPath, config);
                        
                        LogToConsole($"🧠 [SİSTEM] AI öğrenmesi başarılı! Yeni kural kaydedildi: {ruleName}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"⚠️ [SİSTEM] AI Kuralı kaydedilirken uyarı: {ex.Message}");
            }
        }
        else
        {
            LogToConsole("❌ [SİSTEM] Kullanıcı yamayı reddetti/iptal etti.");
        }
    }

    public async void AiFix_Click(object? sender, EventArgs e)
    {
        string? targetFilePath = _dashboardView?.SelectedFilePath;
        if (string.IsNullOrEmpty(targetFilePath))
        {
            targetFilePath = _pendingFixFiles.FirstOrDefault();
        }

        if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
        {
            LogToConsole($"🤖 [DevGuard AI] Canlı AI Çözüm Asistanı Başlatılıyor... Dosya: {Path.GetFileName(targetFilePath)}");
            try
            {
                var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
                var config = BomConfigManager.LoadConfig(configPath);
                
                string encryptedKey = config.EncryptedApiKey;
                if (string.IsNullOrEmpty(encryptedKey))
                {
                    string? envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    if (!string.IsNullOrEmpty(envKey))
                    {
                        encryptedKey = LocalAiFirewall.EncryptApiKey(envKey);
                        LogToConsole("ℹ️ [SİSTEM] .bomconfig'de API anahtarı bulunamadı, ortam değişkeni (OPENAI_API_KEY) kullanılıyor.");
                    }
                    else if (!config.AiProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        LogToConsole("⚠️ [SİSTEM] UYARI: Ayarlarda API anahtarı tanımlanmamış ve OPENAI_API_KEY ortam değişkeni boş!");
                    }
                }

                string rawCode = await File.ReadAllTextAsync(targetFilePath);
                
                if (!_fileErrors.TryGetValue(targetFilePath, out string? errorMessage) || string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = "Dosya üzerinde genel biçimlendirme veya güvenlik ihlali tespit edildi.";
                }

                LogToConsole($"📂 Gerçek dosya okunuyor: {targetFilePath}");
                LogToConsole($"⚠️ Tespit edilen hata(lar): {errorMessage}");

                await _aiOrchestrator.ProcessErrorLiveAsync(targetFilePath, rawCode, errorMessage, encryptedKey);
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ HATA: AI onarımı sırasında beklenmeyen bir sorun oluştu -> {ex.Message}");
            }
        }
        else
        {
            LogToConsole("🔌 [IPC SİMÜLASYONU] IDE'den teşhis mesajı gönderiliyor...");
            await IpcClient.SendDiagnosticsAsync("c:/project/auth.js", "SyntaxError: Unexpected token '=='");

            LogToConsole("🤖 [DevGuard AI] Canlı AI Çözüm Asistanı Başlatılıyor (Demo/Simülasyon)...");

            try
            {
                string encryptedKey = LocalAiFirewall.EncryptApiKey("sk-TEST_KEY_12345");
                string dummyFilePath = "c:/project/auth.js";
                string dummyCode = "def secure_login(password):\n    # TODO: remove hardcoded pass\n    return password == '123'";
                string errorMessage = "Güvenlik İhlali: Koda doğrudan parola (hardcoded password) yazılmış.";

                await _aiOrchestrator.ProcessErrorLiveAsync(dummyFilePath, dummyCode, errorMessage, encryptedKey);
            }
            catch (Exception ex)
            {
                LogToConsole($"❌ HATA: AI Testi sırasında beklenmeyen bir sorun oluştu -> {ex.Message}");
            }
        }
    }

    private async void ShowWelcomeTourIfNeeded()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var config = BomConfigManager.LoadConfig(configPath);

        if (!config.HasSeenWelcomeTour)
        {
            var welcomeWindow = new WelcomeWindow(this);
            welcomeWindow.Present();

            config.HasSeenWelcomeTour = true;
            BomConfigManager.SaveConfig(configPath, config);
        }
    }

    public async void BrowseFiles_Click(object? sender, EventArgs e)
    {
        LogToConsole("🔘 [DEBUG] BrowseFiles_Click tetiklendi!");
        try
        {
            LogToConsole("🔍 [DEBUG] Dosya seçici açılıyor...");
            var dialog = new Gtk.FileDialog();
            dialog.SetTitle("Taranacak Dosyaları Seçin");
            var filesListModel = await dialog.OpenMultipleAsync(this);
            if (filesListModel != null)
            {
                var count = filesListModel.GetNItems();
                for (uint i = 0; i < count; i++)
                {
                    var fileObjPtr = filesListModel.GetItem(i);
                    if (fileObjPtr != IntPtr.Zero)
                    {
                        var fileObj = GObject.Internal.InstanceWrapper.WrapHandle<GObject.Object>(fileObjPtr, true);
                        if (fileObj is Gio.File file)
                        {
                            var path = file.GetPath();
                            if (!string.IsNullOrEmpty(path))
                            {
                                LogToConsole($"📂 Seçildi: {path}");
                                ScanPath(path);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA (Dosya Seçici): {ex.Message}");
        }
    }

    public void ManualScan_Click(object? sender, EventArgs e)
    {
        LogToConsole("🖱️ [DEBUG] ManualScan_Click tetiklendi!");
        if (_manualPathInput != null && !string.IsNullOrWhiteSpace(_manualPathInput.GetText()))
        {
            string path = _manualPathInput.GetText().Trim().Trim('\"', '\'');

            if (File.Exists(path) || Directory.Exists(path))
            {
                LogToConsole($"🎯 Manuel Giriş Algılandı: {path}");
                ScanPath(path);
                _manualPathInput.SetText(string.Empty);
            }
            else
            {
                LogToConsole($"❌ HATA: Geçersiz dosya veya klasör yolu (Bulunamadı) -> {path}");
            }
        }
    }

    private int GetChildrenCount(Gtk.Widget widget)
    {
        int count = 0;
        var child = widget.GetFirstChild();
        while (child != null)
        {
            count++;
            child = child.GetNextSibling();
        }
        return count;
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct GSList
        {
            public IntPtr Data;
            public IntPtr Next;
        }

        [DllImport("libgtk-4.so.1", EntryPoint = "gdk_file_list_get_files")]
        public static extern IntPtr gdk_file_list_get_files(IntPtr fileList);

        [DllImport("libgio-2.0.so.0", EntryPoint = "g_file_get_path")]
        public static extern IntPtr g_file_get_path(IntPtr file);

        [DllImport("libglib-2.0.so.0", EntryPoint = "g_free")]
        public static extern void g_free(IntPtr mem);
    }
}

// Helper Widget Extensions
public static class WidgetExtensions
{
    public static void SetFontSize(this Gtk.Label label, int size)
    {
        label.SetMarkup($"<span size=\"{size * 1000}\">{GLib.Markup.EscapeText(label.GetText() ?? string.Empty)}</span>");
    }

    public static void SetFontWeight(this Gtk.Label label, Pango.Weight weight)
    {
        // Pango weight markup
    }
}
