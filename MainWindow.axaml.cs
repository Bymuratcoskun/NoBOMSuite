using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Controls.Notifications;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using SanitizerKit.Core.Backups;
using SanitizerKit.Core.Logging;
using SanitizerKit.Core.IPC;
using SanitizerKit.Core.Config;
using SanitizerKit.Core.Locks;
using SanitizerKit.Core.AI;
using SanitizerKit.Core.Scanners;
using SanitizerKit.UI.ViewModels;
using SanitizerKit.Core.Caching;

namespace NoBOMSuite.Desktop;

public partial class MainWindow : Window
{
    private BackgroundWatcher _watcher;
    private IpcServer _ipcServer;
    private readonly FileLockManager _lockManager = new();
    private WindowNotificationManager? _notificationManager;
    private readonly AiOrchestrator _aiOrchestrator;
    private readonly DashboardView? _dashboardView;

    // Taramadan dışlanacak gereksiz/büyük klasörlerin listesi
    private readonly HashSet<string> _ignoredDirectories = new(StringComparer.OrdinalIgnoreCase) 
    { 
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build" 
    };

    // Otomatik onarım kapalıyken bulunan sorunlu dosyaları tutar
    private readonly HashSet<string> _pendingFixFiles = new();
    
    private readonly ConcurrentQueue<Action> _uiActionQueue = new();
    private readonly DispatcherTimer _uiUpdateTimer;

    public MainWindow() 
    {
        InitializeComponent();

        // Sistem teması ile otomatik senkronizasyon (Açılışta varsayılan)
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        }
        
        // Toplu (Batch) UI Güncelleme Zamanlayıcısını Başlat
        _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiUpdateTimer.Tick += ProcessUiActionQueue;
        _uiUpdateTimer.Start();
        
        // AI Orkestratörünü (ViewModel yapısı) başlat ve log event'ini arayüze bağla
        _aiOrchestrator = new AiOrchestrator();
        _aiOrchestrator.OnLogMessage += (msg) => LogToConsole(msg);
        _aiOrchestrator.OnPatchReady += ShowSolutionReviewDialog;

        // Dashboard'u bul ve referansını al
        _dashboardView = this.Find<DashboardView>("DashboardPanel");
        if (_dashboardView == null)
        {
            LogToConsole("❌ HATA: Dashboard paneli bulunamadı. Arayüz tanınmıyor.");
        }

        // Sürükle-Bırak görsel feedback için Border'a pointer event'leri ekle
        var dragDropBorder = this.Find<Border>("DragDropBorder");
        if (dragDropBorder != null)
        {
            dragDropBorder.PointerEntered += (s, e) => dragDropBorder.BorderBrush = new SolidColorBrush(Color.Parse("#89B4FA"));
            dragDropBorder.PointerExited += (s, e) => dragDropBorder.BorderBrush = new SolidColorBrush(Color.Parse("#313244"));
        }
        
        // Görünmez TextBox'ın görsel sınır efektlerini tetiklemesi için (İşletim sisteminin sürükle-bırak mekanizmasını bozmadan)
        var hiddenDropZone = this.Find<TextBox>("HiddenWaylandDropZone");
        if (hiddenDropZone != null && dragDropBorder != null)
        {
            hiddenDropZone.AddHandler(DragDrop.DragEnterEvent, (s, e) => dragDropBorder.BorderBrush = new SolidColorBrush(Color.Parse("#89B4FA")), handledEventsToo: true);
            hiddenDropZone.AddHandler(DragDrop.DragLeaveEvent, (s, e) => dragDropBorder.BorderBrush = new SolidColorBrush(Color.Parse("#313244")), handledEventsToo: true);
            hiddenDropZone.AddHandler(DragDrop.DropEvent, (s, e) => dragDropBorder.BorderBrush = new SolidColorBrush(Color.Parse("#313244")), handledEventsToo: true);
        }
        
        // Clipboard paste desteği (Ctrl+V) - Linux'ta drag-drop desteğine alternatif
        KeyDown += async (s, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.V)
            {
                await HandleClipboardPaste();
            }
        };
        
        _watcher = new BackgroundWatcher(OnFileChange);
        
        // Uygulama açılır açılmaz bulunulan klasörü izlemeye başla (Laboratuvar Test Ortamımız için)
        _watcher.StartWatching(Environment.CurrentDirectory);

        // IDE Haberleşme köprüsünü (IPC Server) ayağa kaldır
        _ipcServer = new IpcServer(msg => 
        {
            Dispatcher.UIThread.Post(() => 
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
                    // Format Örneği: DIAGNOSTICS|/proje/script.js|Missing semicolon ';'
                    var parts = msg.Split('|', 3);
                    var fileName = parts.Length > 1 ? Path.GetFileName(parts[1]) : "Bilinmeyen Dosya";
                    var errorMsg = parts.Length > 2 ? parts[2] : "Bilinmeyen Sözdizimi Hatası";
                    LogToConsole($"🐛 IDE TEŞHİSİ: {fileName} -> {errorMsg}");
                }
                else
                {
                    LogToConsole($"🔌 IDE Bağlantısı: {msg}");
                }
            });
        });
        _ipcServer.Start();

        // Bildirim (Toast) Yöneticisini başlat
        _notificationManager = new WindowNotificationManager(this) 
        { 
            Position = NotificationPosition.BottomRight, 
            MaxItems = 3 
        };

        // Uygulama başlatıldığında güncel ayarları yükle
        LoadSettings();
        CheckVsCodeExtension();
        this.Loaded += (s, e) => ShowWelcomeTourIfNeeded();
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
                    Dispatcher.UIThread.Post(() =>
                    {
                        _notificationManager?.Show(new Notification(
                            "VS Code Eklentisi Eksik",
                            "Sisteminizde DevGuard IDE eklentisi bulunamadı. Tam koruma için kurmanızı öneririz.",
                            NotificationType.Warning,
                            TimeSpan.FromSeconds(8)));
                            
                        LogToConsole("🔔 [BİLDİRİM] VS Code eklentisi algılanmadı. Üst menüden 'VS Code Eklentisi Kur' butonunu kullanabilirsiniz.");
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
        var tabSizeInput = this.Find<TextBox>("TabSizeInput");
        if (tabSizeInput != null)
        {
            tabSizeInput.Text = config.TabSize.ToString();
        }
        
        var autoFixCb = this.Find<CheckBox>("AutoFixCheckBox");
        if (autoFixCb != null)
        {
            autoFixCb.IsChecked = config.AutoFix;
        }

        var tempInput = this.Find<TextBox>("AiTemperatureInput");
        if (tempInput != null)
        {
            tempInput.Text = config.AiTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Dinamik Modül Yükleme (.bomconfig EnabledModules)
        var modulesPanel = this.Find<StackPanel>("ModulesPanel");
        if (modulesPanel != null)
        {
            modulesPanel.Children.Clear();
            foreach (var module in config.EnabledModules)
            {
                var cb = new CheckBox
                {
                    Content = $"{module.Key} Modülünü Aktifleştir",
                    IsChecked = module.Value,
                    Tag = module.Key // Hangi modül olduğunu bulmak için Tag kullanıyoruz
                };
                modulesPanel.Children.Add(cb);
            }
        }
    }

    public void SaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
            var config = BomConfigManager.LoadConfig(configPath);
            
            var tabSizeInput = this.Find<TextBox>("TabSizeInput");
            if (tabSizeInput != null && int.TryParse(tabSizeInput.Text, out int newSize))
            {
                config.TabSize = newSize;
            }

            var tempInput = this.Find<TextBox>("AiTemperatureInput");
            if (tempInput != null && double.TryParse(tempInput.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double newTemp))
            {
                config.AiTemperature = newTemp;
            }

            // Dinamik Modül Kaydetme
            var modulesPanel = this.Find<StackPanel>("ModulesPanel");
            if (modulesPanel != null)
            {
                foreach (var child in modulesPanel.Children)
                {
                    if (child is CheckBox cb && cb.Tag is string moduleName)
                    {
                        config.EnabledModules[moduleName] = cb.IsChecked ?? false;
                    }
                }
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
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                LogToConsole("⚠️ Clipboard erişimi yok");
                return;
            }
            
            var formats = await topLevel.Clipboard.GetFormatsAsync();
            var formatList = formats?.ToList() ?? new List<string>();
            LogToConsole($"[DEBUG] Clipboard formatları: {string.Join(", ", formatList)}");

            string? text = null;

            // 1. Durum: Panoda Dosya var (Standart Dosya kopyalaması)
            if (formatList.Contains(DataFormats.Files))
            {
                var files = await topLevel.Clipboard.GetDataAsync(DataFormats.Files) as IEnumerable<IStorageItem>;
                if (files != null && files.Any())
                {
                    foreach (var file in files)
                    {
                        string? filePath = file.TryGetLocalPath();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            LogToConsole($"📋 Clipboard (Dosya) yapıştırıldı: {filePath}");
                            ScanPath(filePath);
                        }
                    }
                    return;
                }
            }
            
            // 2. Durum: GNOME Özgü Dosya Kopyalama Formatı (Ubuntu/Nautilus)
            if (formatList.Contains("x-special/gnome-copied-files"))
            {
                var gnomeData = await topLevel.Clipboard.GetDataAsync("x-special/gnome-copied-files");
                if (gnomeData is byte[] gnomeBytes)
                {
                    text = Encoding.UTF8.GetString(gnomeBytes);
                    // Format "copy\nfile:///yol..." şeklindedir, ilk satırı atlıyoruz.
                    var splitLines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    text = string.Join("\n", splitLines.Skip(1)); 
                }
            }
            // 3. Durum: Diğer Linux Dağıtımları (Thunar vb.) URI List formatı
            else if (formatList.Contains("text/uri-list"))
            {
                var uriData = await topLevel.Clipboard.GetDataAsync("text/uri-list");
                if (uriData is string uriText) text = uriText;
                else if (uriData is byte[] uriBytes) text = Encoding.UTF8.GetString(uriBytes);
            }
            // 4. Durum: Standart Metin Kopyalaması
            else if (formatList.Contains(DataFormats.Text))
            {
                text = await topLevel.Clipboard.GetTextAsync();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                LogToConsole("⚠️ Clipboard boş veya desteklenmeyen format. Lütfen dosya yolunu metin olarak kopyalamayı deneyin.");
                return;
            }
            
            // Çoklu kopyalama (Alt alta yapıştırma) ihtimaline karşı satırları ayır
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool isValid = false;

            foreach (var line in lines)
            {
                string path = line.Trim().Trim('\"', '\'');
                
                // Linux "file://" başlığını temizle
                if (path.StartsWith("file://"))
                {
                    path = Uri.UnescapeDataString(path.Substring(7));
                }

                if (File.Exists(path) || Directory.Exists(path))
                {
                    LogToConsole($"📋 Clipboard'dan yapıştırıldı: {path}");
                    ScanPath(path);
                    isValid = true;
                }
            }

            if (!isValid)
            {
                LogToConsole($"⚠️ Geçersiz yol: {text}");
            }
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ Clipboard hatası: {ex.Message}");
        }
    }

    private void ScanPath(string path)
    {
        if (File.Exists(path))
        {
            ScanFile(path); // Eğer tek bir dosyaysa doğrudan tara
        }
        else if (Directory.Exists(path))
        {
            LogToConsole($"📁 Klasör Tarama Başlatıldı: {path}");
            int scannedCount = 0;
            ScanDirectoryRecursive(path, ref scannedCount);
            LogToConsole($"✅ Klasör Tarama Tamamlandı: {scannedCount} dosya tarandı.");
        }
    }

    private void ScanDirectoryRecursive(string directoryPath, ref int scannedCount)
    {
        try
        {
            // Bulunulan dizindeki dosyaları tara
            foreach (var file in Directory.GetFiles(directoryPath))
            {
                ScanFile(file);
                scannedCount++;
            }

            // Alt klasörlere girerken filtremize takılanları es geç
            foreach (var dir in Directory.GetDirectories(directoryPath))
            {
                string dirName = Path.GetFileName(dir);
                if (!_ignoredDirectories.Contains(dirName))
                {
                    ScanDirectoryRecursive(dir, ref scannedCount);
                }
            }
        }
        catch (Exception ex)
        {
            // Erişim izni olmayan sistem klasörlerinde programın çökmemesi için sadece uyarı fırlatıyoruz
            LogToConsole($"⚠️ UYARI: Klasör atlandı ({directoryPath}) -> {ex.Message}", directoryPath);
        }
    }

    private void ProcessUiActionQueue(object? sender, EventArgs e)
    {
        // Saniyede çok fazla log/UI işlemi gelirse UI Thread'i kilitlememek için tek seferde en fazla 100 işlem işlenir.
        int processedCount = 0;
        while (processedCount < 100 && _uiActionQueue.TryDequeue(out var action))
        {
            action();
            processedCount++;
        }
    }

    public void AutoFix_Changed(object? sender, RoutedEventArgs e)
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var config = BomConfigManager.LoadConfig(configPath);
        var cb = sender as CheckBox;
        if (cb != null && config.AutoFix != cb.IsChecked)
        {
            config.AutoFix = cb.IsChecked ?? false;
            BomConfigManager.SaveConfig(configPath, config);
            LogToConsole(config.AutoFix ? "⚙️ Otomatik Onarım Modu AÇIK." : "⚙️ Otomatik Onarım Modu KAPALI.");
        }
    }

    private void UpdateFixButton()
    {
        _uiActionQueue.Enqueue(() =>
        {
            var fixBtn = this.Find<Button>("FixPendingButton");
            if (fixBtn != null)
            {
                fixBtn.IsVisible = _pendingFixFiles.Count > 0;
                fixBtn.Content = $"🛠️ Sorunları Onar ({_pendingFixFiles.Count})";
            }
        });
    }

    public void FixPending_Click(object? sender, RoutedEventArgs e)
    {
        if (_pendingFixFiles.Count == 0) return;
        
        LogToConsole($"🛠️ Bekleyen {_pendingFixFiles.Count} dosya için toplu onarım başlatılıyor...");
        
        // Geçici olarak AutoFix'i açıp tekrar taratıyoruz ki onarsın
        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var config = BomConfigManager.LoadConfig(configPath);
        bool originalAutoFix = config.AutoFix;
        config.AutoFix = true;
        BomConfigManager.SaveConfig(configPath, config);
        
        var filesToFix = _pendingFixFiles.ToList();
        _pendingFixFiles.Clear();
        UpdateFixButton();
        
        foreach(var file in filesToFix)
        {
            ScanFile(file); 
        }
        
        // Eski ayara geri dön
        config.AutoFix = originalAutoFix;
        BomConfigManager.SaveConfig(configPath, config);
        
        LogToConsole("✅ Toplu onarım tamamlandı.");
    }

    private void ScanFile(string filePath, bool allowAutoFix = true)
    {
        try
        {
            // .bomconfig ayarlarını yükle ve dışlanan uzantı kontrolü yap
            var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (config.ExcludedExtensions.Contains(ext))
            {
                LogToConsole($"⏭️ [ATLANDI] Dışlanan dosya türü: {Path.GetFileName(filePath)}", filePath);
                return;
            }

            // Önbellek kontrolü yap (Incremental Scan)
            if (FileCacheManager.IsCacheValid(filePath, out bool cachedHasIssues))
            {
                if (!cachedHasIssues)
                {
                    LogToConsole($"⚡ [ÖNBELLEK] Değişiklik yok (Temiz): {Path.GetFileName(filePath)}", filePath);
                    _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Temiz (Cache)", new SolidColorBrush(Color.Parse("#A6E3A1"))));
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

            if (new BomScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] UTF-8 BOM Karakteri Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixBom = true;
            }
            if (new LineEndingScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] CRLF (Windows) Satır Sonu Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixCrlf = true;
            }
            if (new GhostCharScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] Görünmez Hayalet Karakter Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixGhost = true;
            }
            if (new NewlineScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] POSIX Uyumsuz (EOF Newline Yok) Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixNewline = true;
            }
            if (new TabScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] Sekme (Tab) Kullanımı Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixTab = true;
            }
            if (new HardcodedPasswordScanner().HasIssue(span))
            {
                LogToConsole($"⚠️ [UYARI] Doğrudan Yazılmış Şifre (Hardcoded Password) Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                hasIssue = true;
                fixPassword = true;
            }

            if (config.EnabledModules.TryGetValue("EntropyScanner", out bool isEntropyEnabled) && isEntropyEnabled)
            {
                if (new EntropyScanner().HasIssue(span))
                {
                    LogToConsole($"⚠️ [UYARI] Yüksek Entropili Gizli Veri / API Anahtarı Tespit Edildi: {Path.GetFileName(filePath)}", filePath);
                    hasIssue = true;
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
            }

            if (!hasIssue)
            {
                LogToConsole($"✅ [TEMİZ] Dosya sorunsuz: {Path.GetFileName(filePath)}", filePath);
            _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Temiz", new SolidColorBrush(Color.Parse("#A6E3A1"))));
                _pendingFixFiles.Remove(filePath);
                UpdateFixButton();
                FileCacheManager.UpdateCache(filePath, hasIssues: false);
            }
            else
            {
                // Hata var, AutoFix (Otomatik Onarım) ayarını kontrol et
                if (config.AutoFix && allowAutoFix)
                {
                _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Onarıldı", new SolidColorBrush(Color.Parse("#89B4FA"))));
                    LogToConsole($"🛠️ [AUTO-FIX] Otomatik onarım başlatılıyor: {Path.GetFileName(filePath)}", filePath);
                    
                    if (config.BackupEnabled)
                    {
                        var backupMgr = new BackupManager(Environment.CurrentDirectory);
                        backupMgr.BackupFile(filePath);
                        LogToConsole($"🛡️ [YEDEKLEME] Orijinal dosya onarım öncesi güvenliğe alındı.", filePath);
                    }

                    List<byte> outputBytes = new List<byte>(content);
                    
                    // 1. BOM Temizliği (İlk 3 baytı siler)
                    if (fixBom) outputBytes.RemoveRange(0, 3);
                    
                    // 2. CRLF ve Hayalet Karakter Temizliği (String üzerinden güvenli silme)
                    if (fixCrlf || fixGhost || fixTab || fixPassword || fixCustomRule)
                    {
                        string tempText = Encoding.UTF8.GetString(outputBytes.ToArray());
                        if (fixCrlf) tempText = tempText.Replace("\r\n", "\n");
                        if (fixGhost) tempText = tempText.Replace("\u200B", "");
                        if (fixTab) tempText = tempText.Replace("\t", new string(' ', config.TabSize)); // Dinamik sekme genişliği
                        if (fixPassword)
                        {
                            // Şifre tanımını yakala ve sadece değer kısmını maskele (örn: password: "[MASKED_BY_DEVGUARD]" -> password: "[MASKED_BY_DEVGUARD]")
                            var regex = new System.Text.RegularExpressions.Regex(@"((password|passwd|pass|secret)\s*[:=]\s*)(['""])(.*?)\3", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            tempText = regex.Replace(tempText, "$1$3[MASKED_BY_DEVGUARD]$3");
                        }
                        if (fixCustomRule)
                        {
                            // RegexScanner'ı tekrar kullan
                            tempText = regexScanner.ApplyFixes(tempText);
                        }
                        outputBytes = new List<byte>(new UTF8Encoding(false).GetBytes(tempText));
                    }
                    
                    // 3. POSIX Newline Ekleme (Eğer son bayt '0x0A' değilse ekler)
                    if (fixNewline)
                    {
                        if (outputBytes.Count > 0 && outputBytes[outputBytes.Count - 1] != 0x0A)
                            outputBytes.Add(0x0A);
                    }
                    
                    File.WriteAllBytes(filePath, outputBytes.ToArray());
                    LogToConsole($"✨ [BAŞARILI] Dosya onarıldı ve kaydedildi: {Path.GetFileName(filePath)}", filePath);
                    _pendingFixFiles.Remove(filePath);
                    UpdateFixButton();
                    FileCacheManager.UpdateCache(filePath, hasIssues: false);
                }
                else
                {
                _uiActionQueue.Enqueue(() => _dashboardView?.UpdateDashboard(filePath, "Sorunlu", new SolidColorBrush(Color.Parse("#F38BA8"))));
                    _pendingFixFiles.Add(filePath);
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
        // Arka planda çalışan modülleri tetikle (UI'ı kilitlemeden)
        Task.Run(() =>
        {
            try
            {
                var config = BomConfigManager.LoadConfig(Path.Combine(Environment.CurrentDirectory, ".bomconfig"));
                
                // Modül 1: AutoLogger (Metin dosyasına yazar)
                if (config.EnabledModules.TryGetValue("AutoLogger", out bool isAutoLogEnabled) && isAutoLogEnabled)
                {
                    string logFile = Path.Combine(Environment.CurrentDirectory, "devguard_auto.log");
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(logFile, logEntry);
                }
                
                // Modül 2: SqliteLogger (Veritabanına yazar)
                if (config.EnabledModules.TryGetValue("SqliteLogger", out bool isDbLogEnabled) && isDbLogEnabled)
                {
                    SqliteLogger.Log("INFO", message, sourceFile);
                }
            }
            catch { /* Dosya kilitlenmesi veya diğer hatalarda uygulamanın çökmesini engelle */ }
        });

        _uiActionQueue.Enqueue(() =>
        {
            var consolePanel = this.Find<StackPanel>("LiveConsolePanel");
            var scroller = this.Find<ScrollViewer>("ConsoleScroller");

            if (consolePanel != null)
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                string fullMessage = $"[{time}] {message}";
                
                var textBlock = new TextBlock 
                { 
                    Text = fullMessage, 
                    FontFamily = FontFamily.Parse("Consolas, Courier New"),
                    TextWrapping = TextWrapping.Wrap
                };

                // Eğer log bir dosyayla ilişkiliyse, çift tıklama ile açılabilmesi için Tag'e yolu ekle
                if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                {
                    textBlock.Tag = sourceFile;
                    textBlock.Cursor = new Cursor(StandardCursorType.Hand);
                    textBlock.DoubleTapped += ConsoleLog_DoubleTapped;
                }

                if (message.Contains("❌ HATA") || message.Contains("REDDEDİLDİ") || message.Contains("API BAĞLANTI HATASI"))
                {
                    textBlock.Foreground = Brushes.Red;
                }
                else if (message.Contains("⚠️") || message.Contains("[UYARI]"))
                {
                    textBlock.Foreground = Brushes.Yellow;
                }
                else
                {
                    textBlock.Foreground = new SolidColorBrush(Color.Parse("#A6E3A1"));
                }

                consolePanel.Children.Add(textBlock);
                
                // Kullanıcı her zaman en güncel logları görsün diye otomatik en aşağı kaydırır
                scroller?.ScrollToEnd();
            }
        });
    }

    private void ConsoleLog_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.Tag is string filePath)
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
    }

    private void OnFileChange(string filePath)
    {
        if (_lockManager.IsLocked(filePath))
        {
            LogToConsole($"⏳ İPTAL EDİLDİ: {Path.GetFileName(filePath)} şu an IDE tarafından işleniyor. Çakışma önlendi.", filePath);
            return; // Dosya IDE tarafından onarılıyor, arka plan ajanı koda dokunamaz!
        }

        LogToConsole($"🔔 (SİSTEM DAEMON) Arka planda dosya değişikliği yakalandı: {Path.GetFileName(filePath)}", filePath);
        
        // Arka planda taramayı başlat (Geliştiricinin editörüne müdahale etmemek için otomatik onarımı zorla kapat)
        Dispatcher.UIThread.Post(() =>
        {
            ScanFile(filePath, allowAutoFix: false);
            
            // Eğer pencere kapalıysa (System Tray'deyse) kullanıcıyı Toast Notification ile bilgilendir
            if (!this.IsVisible && _notificationManager != null)
            {
                _notificationManager.Show(new Notification(
                    "DevGuard İzleme Servisi",
                    $"{Path.GetFileName(filePath)} dosyası arka planda denetlendi.",
                    NotificationType.Information,
                    TimeSpan.FromSeconds(3)));
            }
        });
    }

    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        // Pencerenin "X" (kapat) tuşuna basıldığında programı öldürme, sadece gizle (Tray'a düşsün)
        e.Cancel = true;
        this.Hide();
        
        // Konsola bilgi notu düş (kullanıcı arayüzü geri açtığında görebilsin)
        LogToConsole("GUI Kapatıldı -> Arka Planda Sessiz İzleme Modu Devrede...");
    }

    public void ClearAutoLog_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string logFile = Path.Combine(Environment.CurrentDirectory, "devguard_auto.log");
            if (File.Exists(logFile))
            {
                // Dosyayı tamamen boşaltır (silmez, içini temizler)
                File.WriteAllText(logFile, string.Empty);
                LogToConsole("🗑️ Otomatik kayıt dosyası (devguard_auto.log) başarıyla temizlendi.");
            }
            else
            {
                LogToConsole("⚠️ Temizlenecek log dosyası bulunamadı (devguard_auto.log).");
            }
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: Log dosyası temizlenirken hata oluştu -> {ex.Message}");
        }
    }

    public void InstallGitHook_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string targetDir = Environment.CurrentDirectory; // Proje ana dizini
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

            // Unix tabanlı (Mac/Linux) sistemler için çalıştırılabilir (executable) izni (chmod +x) veriyoruz
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

    public async void InstallVsCodeExtension_Click(object? sender, RoutedEventArgs e)
    {
        // GitHub'daki yayınlanmış son sürümden .vsix dosyasını çeken URL (placeholder)
        const string vsixUrl = "https://github.com/your-repo-owner/NoBOMSuite/releases/latest/download/devguard-0.0.1.vsix";
        string tempVsixPath = Path.Combine(Path.GetTempPath(), "devguard.vsix");

        try
        {
            LogToConsole($"⏳ VS Code Eklentisi indiriliyor: {vsixUrl}");
            
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(vsixUrl);
                response.EnsureSuccessStatusCode();
                using (var fs = new FileStream(tempVsixPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }
            
            LogToConsole("✅ İndirme tamamlandı. Eklenti kuruluyor...");

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "code",
                Arguments = OperatingSystem.IsWindows() ? $"/c code --install-extension \"{tempVsixPath}\"" : $"--install-extension \"{tempVsixPath}\"",
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

                if (process.ExitCode == 0 && (output.Contains("successfully") || output.Contains("başarıyla")))
                {
                    LogToConsole($"✅ BAŞARILI: DevGuard VS Code eklentisi kuruldu.");
                }
                else
                {
                    LogToConsole($"❌ HATA: Eklenti kurulurken sorun oluştu.");
                    if (!string.IsNullOrWhiteSpace(output)) LogToConsole($"   Çıktı: {output}");
                    if (!string.IsNullOrWhiteSpace(error)) LogToConsole($"   Hata: {error}");
                }
            }
        }
        catch (HttpRequestException httpEx)
        {
            LogToConsole($"❌ HATA: Eklenti indirilemedi. URL'yi kontrol edin. -> {httpEx.Message}");
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: Kurulum sihirbazı başlatılamadı (Sisteminizde VS Code yüklü mü?) -> {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempVsixPath))
            {
                try { File.Delete(tempVsixPath); }
                catch (Exception ex) { LogToConsole($"⚠️ UYARI: Geçici eklenti dosyası silinemedi. -> {ex.Message}"); }
            }
        }
    }
    public async void ExportPortable_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            LogToConsole("🚀 Taşınabilir Sürüm Sihirbazı başlatıldı. Hedef klasörü seçin...");
            
            var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Taşınabilir Sürüm Nereye Kaydedilsin?",
                AllowMultiple = false
            });

            string? selectedPath = folders?.FirstOrDefault()?.TryGetLocalPath();
            
            // Eğer kullanıcı iptal derse veya Linux/Wayland seçici açamazsa Masaüstünü kullan
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

            // 1. Ayar dosyasını kopyala
            BomConfigManager.ExportPortableConfig(exportDir);
            LogToConsole("  - Ayar dosyası (.bomconfig) ihraç edildi.");

            // 2. Masaüstü uygulamasını kopyala
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

            // 3. CLI aracını kopyala (Geliştirme dizini ve Publish dizinlerine bak)
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

            // 4. Wasm dosyalarını kopyala
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

            // 5. SQLite Log Veritabanı Altyapısını Hazırla
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

    public void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current != null)
        {
            var currentTheme = Application.Current.RequestedThemeVariant;
            
            if (currentTheme == Avalonia.Styling.ThemeVariant.Dark)
                Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            else if (currentTheme == Avalonia.Styling.ThemeVariant.Light)
                Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
            else
                Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

            string themeName = Application.Current.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Default ? "Sistem (Otomatik)" : Application.Current.RequestedThemeVariant?.ToString() ?? "Bilinmiyor";
            LogToConsole($"🎨 Tema değiştirildi: {themeName}");
        }
    }

    private void ShowSolutionReviewDialog(string patchJson, string filePath, string originalCode)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var window = new SolutionReviewWindow(patchJson, filePath, originalCode);
            var result = await window.ShowDialog<bool>(this);
            if (result)
            {
                LogToConsole("✅ [SİSTEM] Kullanıcı yamayı onayladı ve kod başarıyla uygulandı.");
                
                _notificationManager?.Show(new Notification(
                    "AI Onarımı Başarılı",
                    $"✨ {Path.GetFileName(filePath)} dosyası yapay zeka tarafından onarıldı ve güvenlik onayı aldı.",
                    NotificationType.Success,
                    TimeSpan.FromSeconds(5)));

                // AI Tarafından Üretilen Reçeteyi .bomconfig'e Otomatik Kaydet
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
        });
    }

    public async void AiFix_Click(object? sender, RoutedEventArgs e)
    {
        // Adım 4.4 Simülasyonu: IDE'den gelen teşhis mesajını dinleyiciye gönder
        LogToConsole("🔌 [IPC SİMÜLASYONU] IDE'den teşhis mesajı gönderiliyor...");
        await IpcClient.SendDiagnosticsAsync("c:/project/auth.js", "SyntaxError: Unexpected token '=='");

        LogToConsole("🤖 [DevGuard AI] Canlı AI Çözüm Asistanı Başlatılıyor...");

        try
        {
            // Test amaçlı temsili bir kural ihlali senaryosu oluşturuyoruz
            string encryptedKey = LocalAiFirewall.EncryptApiKey("sk-TEST_KEY_12345");
            string dummyFilePath = "c:/project/auth.js";
            string dummyCode = "def secure_login(password):\n    # TODO: remove hardcoded pass\n    return password == '123'";
            string errorMessage = "Güvenlik İhlali: Koda doğrudan parola (hardcoded password) yazılmış.";

            // AI Orkestratörünü tetikle, loglar event üzerinden otomatik olarak 'LiveConsolePanel'e akacak
            await _aiOrchestrator.ProcessErrorLiveAsync(dummyFilePath, dummyCode, errorMessage, encryptedKey);
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA: AI Testi sırasında beklenmeyen bir sorun oluştu -> {ex.Message}");
        }
    }

    private async void ShowWelcomeTourIfNeeded()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, ".bomconfig");
        var config = BomConfigManager.LoadConfig(configPath);

        if (!config.HasSeenWelcomeTour)
        {
            // Yeni pencereyi UI thread üzerinde oluşturup göster
            var welcomeWindow = new WelcomeWindow();
            await welcomeWindow.ShowDialog(this);

            config.HasSeenWelcomeTour = true;
            BomConfigManager.SaveConfig(configPath, config);
        }
    }

    public async void BrowseFiles_Click(object? sender, RoutedEventArgs e)
    {
        LogToConsole("🔘 [DEBUG] BrowseFiles_Click tetiklendi!");
        try
        {
            LogToConsole("🔍 [DEBUG] Dosya seçici açılıyor...");
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Taranacak Dosyaları Seçin",
                AllowMultiple = true
            });

            foreach (var file in files)
            {
                string? path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    LogToConsole($"📂 Gözat ile Seçildi: {path}");
                    ScanPath(path);
                }
            }
        }
        catch (Exception ex)
        {
            LogToConsole($"❌ HATA (Dosya Seçici): {ex.GetType().Name}");
            LogToConsole($"   Detay: {ex.Message}");
            if (ex.Message.Contains("DBus") || ex.Message.Contains("GTK") || ex.Message.Contains("D-Bus"))
            {
                LogToConsole($"   💡 İpucu: Linux'ta görsel dosya seçici (DBus/GTK) yok. Manuel yolu yapıştır veya 'Tara' tıkla.");
            }
        }
    }

    public void ManualScan_Click(object? sender, RoutedEventArgs e)
    {
        LogToConsole("🖱️ [DEBUG] ManualScan_Click tetiklendi!");
        var inputControl = this.Find<TextBox>("ManualPathInput");
        if (inputControl != null && !string.IsNullOrWhiteSpace(inputControl.Text))
        {
            string path = inputControl.Text.Trim();
            
            // Yoldaki olası çift tırnakları (Örn: "C:\path" -> C:\path) temizliyoruz
            path = path.Trim('\"', '\'');

            if (File.Exists(path) || Directory.Exists(path))
            {
                LogToConsole($"🎯 Manuel Giriş Algılandı: {path}");
                ScanPath(path);
                inputControl.Text = string.Empty; // İşlem başarılıysa kutuyu temizle
            }
            else
            {
                LogToConsole($"❌ HATA: Geçersiz dosya veya klasör yolu (Bulunamadı) -> {path}");
            }
        }
    }

    public void ManualPathInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ManualScan_Click(sender, new RoutedEventArgs());
        }
    }

    public void HiddenWaylandDropZone_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var tb = sender as TextBox;
        if (tb != null && !string.IsNullOrWhiteSpace(tb.Text))
        {
            string droppedText = tb.Text;
            
            // TextBox'ı kilitlememesi için metni asenkron olarak hemen temizle
            Dispatcher.UIThread.Post(() => 
            {
                tb.Text = string.Empty;
            });

            var lines = droppedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string path = line.Trim().Trim('\"', '\'');
                if (path.StartsWith("file://")) path = Uri.UnescapeDataString(path.Substring(7));

                if (File.Exists(path) || Directory.Exists(path))
                {
                    LogToConsole($"🎯 Sürükle-Bırak (Wayland Hack) Algılandı: {path}");
                    ScanPath(path);
                }
            }
        }
    }
}
