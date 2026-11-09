// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.IO;
using Path = System.IO.Path;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SmartNetworkOptimizer;

public partial class MainWindow : Window
{
    private const int MaxLiveSamples = 60;
    private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "SmartNetworkOptimizer";
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<LivePingSample> _liveSamples = new();
    private bool _livePingInFlight;
    private bool _loadingStartupPreference;
    private readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartNetworkOptimizer");
    private string BackupPath => Path.Combine(_appDataDir, "dns-backup.json");
    private string NicBackupPath => Path.Combine(_appDataDir, "nic-backup.json");
    private string AbBenchmarkPath => Path.Combine(_appDataDir, "ab-benchmark.json");
    private string PerformanceHistoryPath => Path.Combine(_appDataDir, "performance-history.json");
    private NetworkInterface? _activeAdapter;
    private readonly Dictionary<string, PingResult> _lastPingResults = new(StringComparer.OrdinalIgnoreCase);
    private string _lastSystemReport = string.Empty;
    private readonly List<DnsResolverBenchmarkResult> _resolverBenchmarks = new();
    private readonly List<DnsResolverBenchmarkSession> _dnsBenchmarkSessions = new();
    private string _lastGameDiscoveryReport = string.Empty;
    private IReadOnlyList<NicOptimizationAdvisor.DriverProperty> _lastNicProperties = Array.Empty<NicOptimizationAdvisor.DriverProperty>();
    private string? _lastNicAdapterId;
    private bool _lastNicIsPhysical;

    private static MessageBoxResult ShowBAYATMessageBox(string message)
        => MessageBox.Show(message, "BAYAT | Smart Network Optimizer");

    private static MessageBoxResult ShowBAYATMessageBox(string message, string caption)
        => MessageBox.Show(message, "BAYAT | " + caption);

    private static MessageBoxResult ShowBAYATMessageBox(
        string message, string caption, MessageBoxButton buttons)
        => MessageBox.Show(message, "BAYAT | " + caption, buttons);

    private static MessageBoxResult ShowBAYATMessageBox(
        string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
        => MessageBox.Show(message, "BAYAT | " + caption, buttons, image);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            RefreshDashboardConnection();
            await ScanSystemAsync();
            RenderLiveChart();
        };
        _liveTimer.Tick += LiveTimer_Tick;
        Closed += (_, _) => _liveTimer.Stop();
        LoadStartupPreference();
        LiveStopButton.IsEnabled = false;
        LiveClearButton.IsEnabled = false;
        LiveExportButton.IsEnabled = false;
        UpdateRestoreButton();
        UpdateNicRestoreButton();
        UpdateAbButtons();
    }

    private async void LiveStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null || GetDefaultGateway(_activeAdapter) is null)
        {
            ShowBAYATMessageBox("Canlı izleme için varsayılan ağ geçidi bulunan aktif bir bağlantı gerekli. Önce Sistem Taraması yapın.",
                "Canlı ağ izleme", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _liveSamples.Clear();
        RenderLiveChart();
        _liveTimer.Start();
        LiveStartButton.IsEnabled = false;
        LiveStopButton.IsEnabled = true;
        StatusText.Text = "Canlı ağ geçidi izlemesi başladı. Her saniye bir ICMP örneği alınacak.";
        await SampleLiveGatewayAsync();
    }

    private void LiveStopButton_Click(object sender, RoutedEventArgs e)
    {
        _liveTimer.Stop();
        LiveStartButton.IsEnabled = _activeAdapter is not null && GetDefaultGateway(_activeAdapter) is not null;
        LiveStopButton.IsEnabled = false;
        StatusText.Text = "Canlı ağ geçidi izlemesi durduruldu.";
    }

    private void LiveClearButton_Click(object sender, RoutedEventArgs e)
    {
        _liveSamples.Clear();
        RenderLiveChart();
        UpdateLiveMetrics();
        StatusText.Text = "Canlı ölçüm grafiği temizlendi.";
    }

    private void LiveChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderLiveChart();

    private async void LiveTimer_Tick(object? sender, EventArgs e) => await SampleLiveGatewayAsync();

    private async Task SampleLiveGatewayAsync()
    {
        if (_livePingInFlight) return;
        _livePingInFlight = true;
        long? roundtripMs = null;
        var status = "NoGateway";
        var started = DateTimeOffset.Now;
        try
        {
            var gateway = _activeAdapter is null ? null : GetDefaultGateway(_activeAdapter);
            if (gateway is not null)
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(gateway, 900);
                if (reply.Status == IPStatus.Success)
                {
                    roundtripMs = reply.RoundtripTime;
                    status = "Success";
                }
                else status = reply.Status.ToString();
            }
        }
        catch (PingException ex)
        {
            status = ex.InnerException?.GetType().Name ?? "PingException";
        }
        catch (SocketException ex)
        {
            status = ex.SocketErrorCode.ToString();
        }
        catch (NetworkInformationException ex)
        {
            status = ex.ErrorCode.ToString(CultureInfo.InvariantCulture);
        }
        catch (ObjectDisposedException)
        {
            status = "Disposed";
        }
        catch (InvalidOperationException)
        {
            status = "InvalidOperation";
        }
        finally
        {
            _livePingInFlight = false;
        }

        _liveSamples.Enqueue(new LivePingSample(started, roundtripMs, status));
        while (_liveSamples.Count > MaxLiveSamples) _liveSamples.Dequeue();
        UpdateLiveMetrics();
        RenderLiveChart();
    }

    private void RefreshDashboardConnection()
    {
        var adapter = _activeAdapter;
        if (adapter is null)
        {
            ActiveAdapterText.Text = "Aktif ağ geçidi olan bağlantı bulunamadı.";
            LiveTargetInfoText.Text = "Varsayılan ağ geçidi henüz belirlenemedi.";
            LiveStartButton.IsEnabled = false;
            return;
        }

        var gateway = GetDefaultGateway(adapter);
        ActiveAdapterText.Text = $"{adapter.Name}\n{adapter.NetworkInterfaceType} • {FormatLinkSpeed(adapter.Speed)}\nDurum: {adapter.OperationalStatus}";
        LiveTargetInfoText.Text = gateway is null
            ? $"{adapter.Name} • Varsayılan ağ geçidi bulunamadı"
            : $"{adapter.Name} • Yerel ağ geçidi: {gateway}";
        LiveStartButton.IsEnabled = gateway is not null && !_liveTimer.IsEnabled;
    }

    private static IPAddress? GetDefaultGateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(address => !IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address));
        }
        catch (NetworkInformationException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    private void UpdateLiveMetrics()
    {
        var samples = _liveSamples.ToArray();
        var successes = samples.Where(s => s.RoundtripMs.HasValue).Select(s => s.RoundtripMs!.Value).ToArray();
        var last = samples.LastOrDefault();
        LiveLastText.Text = last?.RoundtripMs is long lastMs ? $"{lastMs} ms" : (last is null ? "— ms" : "Zaman aşımı");
        LiveAverageText.Text = successes.Length == 0 ? "— ms" : $"{successes.Average():0.0} ms";
        var loss = samples.Length == 0 ? (double?)null : (samples.Length - successes.Length) * 100d / samples.Length;
        LiveLossText.Text = loss.HasValue ? $"{loss.Value:0.0} %" : "— %";
        LiveSamplesText.Text = $"{samples.Length} / {MaxLiveSamples} örnek";
        var jitterDeltas = samples.Zip(samples.Skip(1))
            .Where(pair => pair.First.RoundtripMs.HasValue && pair.Second.RoundtripMs.HasValue)
            .Select(pair => Math.Abs(pair.Second.RoundtripMs!.Value - pair.First.RoundtripMs!.Value))
            .ToArray();
        LiveJitterText.Text = jitterDeltas.Length == 0 ? "— ms" : $"{jitterDeltas.Average():0.0} ms";
        LiveClearButton.IsEnabled = samples.Length > 0;
        LiveExportButton.IsEnabled = samples.Length > 0;
    }

    private void RenderLiveChart()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RenderLiveChart));
            return;
        }

        var canvas = LiveChartCanvas;
        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 30 || height < 30) return;

        canvas.Children.Clear();
        const double left = 42;
        const double right = 12;
        const double top = 12;
        const double bottom = 22;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);
        var samples = _liveSamples.ToArray();
        var values = samples.Where(s => s.RoundtripMs.HasValue).Select(s => (double)s.RoundtripMs!.Value).ToArray();
        var maxRtt = Math.Max(20d, values.Length == 0 ? 20d : values.Max() * 1.25d);
        maxRtt = Math.Ceiling(maxRtt / 10d) * 10d;

        for (var i = 0; i <= 4; i++)
        {
            var y = top + plotHeight * i / 4d;
            canvas.Children.Add(new Line
            {
                X1 = left, X2 = width - right, Y1 = y, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                StrokeThickness = 1, Opacity = 0.75
            });
            var label = new TextBlock
            {
                Text = $"{maxRtt * (4 - i) / 4d:0} ms",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9
            };
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, y - 7);
            canvas.Children.Add(label);
        }

        if (samples.Length == 0)
        {
            var empty = new TextBlock
            {
                Text = "Canlı izlemeyi başlatın — henüz ölçüm yok",
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 13
            };
            Canvas.SetLeft(empty, left + 20);
            Canvas.SetTop(empty, top + plotHeight / 2 - 10);
            canvas.Children.Add(empty);
            return;
        }

        var series = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round
        };
        var xDenominator = Math.Max(1, MaxLiveSamples - 1);
        for (var i = 0; i < samples.Length; i++)
        {
            var offset = MaxLiveSamples - samples.Length;
            var x = left + plotWidth * (offset + i) / xDenominator;
            if (samples[i].RoundtripMs is long roundtripMs)
            {
                var y = top + plotHeight - (Math.Clamp(roundtripMs, 0, maxRtt) / maxRtt * plotHeight);
                series.Points.Add(new Point(x, y));
            }
            else
            {
                if (series.Points.Count >= 2) canvas.Children.Add(series);
                series = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    StrokeThickness = 2.2,
                    StrokeLineJoin = PenLineJoin.Round
                };
                var marker = new Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = new SolidColorBrush(Color.FromRgb(248, 113, 113))
                };
                Canvas.SetLeft(marker, x - 3);
                Canvas.SetTop(marker, top + plotHeight - 3);
                canvas.Children.Add(marker);
            }
        }
        if (series.Points.Count >= 2) canvas.Children.Add(series);
    }

    private void LiveExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_liveSamples.Count == 0)
        {
            ShowBAYATMessageBox("Dışa aktarılacak canlı ölçüm yok. Önce izlemeyi başlatın.", "CSV dışa aktarımı");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Canlı ağ ölçümlerini kaydet",
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"network-live-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Success,RTT_ms,Status");
            foreach (var sample in _liveSamples)
            {
                var success = sample.RoundtripMs.HasValue ? "1" : "0";
                var rtt = sample.RoundtripMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                var safeStatus = sample.Status.Replace("\"", "\"\"", StringComparison.Ordinal);
                sb.AppendLine($"\"{sample.Timestamp:O}\",{success},{rtt},\"{safeStatus}\"");
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            StatusText.Text = "Canlı ölçümler CSV olarak kaydedildi: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("CSV kaydedilemedi: " + ex.Message, "Dışa aktarma hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadStartupPreference()
    {
        _loadingStartupPreference = true;
        try
        {
            var exePath = GetPublishedExecutablePath();
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: false);
            var value = key?.GetValue(StartupValueName) as string;
            StartupCheckBox.IsChecked = exePath is not null && IsSameStartupCommand(value, exePath);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Windows açılış tercihi okunamadı: " + ex.Message;
            StartupCheckBox.IsChecked = false;
        }
        finally { _loadingStartupPreference = false; }
    }

    private void StartupCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupPreference) return;
        var exePath = GetPublishedExecutablePath();
        if (exePath is null)
        {
            ShowBAYATMessageBox("Windows açılışına eklemek için yayınlanmış SmartNetworkOptimizer.exe dosyasını çalıştırın. Geliştirme ortamında bu seçenek etkinleştirilemez.",
                "Başlangıç ayarı", MessageBoxButton.OK, MessageBoxImage.Information);
            _loadingStartupPreference = true;
            StartupCheckBox.IsChecked = false;
            _loadingStartupPreference = false;
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupRunKey, writable: true);
            if (key is null) throw new InvalidOperationException("Windows başlangıç kayıt anahtarı açılamadı.");
            key.SetValue(StartupValueName, $"\"{exePath}\"", RegistryValueKind.String);
            StatusText.Text = "Program bu Windows kullanıcı hesabının açılışına eklendi.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("Başlangıç tercihi kaydedilemedi: " + ex.Message, "Başlangıç ayarı", MessageBoxButton.OK, MessageBoxImage.Error);
            LoadStartupPreference();
        }
    }

    private void StartupCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupPreference) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: true);
            key?.DeleteValue(StartupValueName, throwOnMissingValue: false);
            StatusText.Text = "Windows açılışında otomatik başlatma kapatıldı.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("Başlangıç tercihi kaldırılamadı: " + ex.Message, "Başlangıç ayarı", MessageBoxButton.OK, MessageBoxImage.Error);
            LoadStartupPreference();
        }
    }

    private static string? GetPublishedExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.GetFileName(path).Equals("SmartNetworkOptimizer.exe", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.GetFullPath(path);
    }

    private static bool IsSameStartupCommand(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var normalized = command.Trim().Trim('"');
        return normalized.Equals(executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanSystemAsync();

    private async Task ScanSystemAsync()
    {
        SetBusy(true, "Donanım ve ağ bilgileri okunuyor...");
        try
        {
            var report = await Task.Run(BuildSystemReport);
            OutputText.Text = report.Text;
            _lastSystemReport = report.Text;
            var previousAdapterId = _activeAdapter?.Id;
            _activeAdapter = report.ActiveAdapter;
            RefreshDashboardConnection();
            if (!string.Equals(previousAdapterId, _activeAdapter?.Id, StringComparison.OrdinalIgnoreCase))
            {
                _lastNicProperties = Array.Empty<NicOptimizationAdvisor.DriverProperty>();
                _lastNicAdapterId = null;
                _lastNicIsPhysical = false;
            }
            _lastPingResults.Clear();
            var compatibilityNotice = PlatformCompatibility.GetWindows10Notice();
            StatusText.Text = compatibilityNotice is not null
                ? report.ActiveAdapter is null
                    ? "Tarama tamamlandı. Windows uyumluluk notunu raporda inceleyin; aktif bağlantı bulunamadı."
                    : $"Tarama tamamlandı. Windows uyumluluk notunu raporda inceleyin. Aktif bağlantı: {report.ActiveAdapter.Name}"
                : report.ActiveAdapter is null
                    ? "Tarama tamamlandı. Aktif ağ bağlantısı bulunamadı."
                    : $"Tarama tamamlandı. Aktif bağlantı: {report.ActiveAdapter.Name}";
        }
        catch (Exception ex)
        {
            AppendLine("\n[TARAMA HATASI] " + ex.Message);
            StatusText.Text = "Tarama tamamlanamadı.";
        }
        finally { SetBusy(false); UpdateRestoreButton(); }
    }

    private SystemReport BuildSystemReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("BAYAT | SMART NETWORK OPTIMIZER — SİSTEM RAPORU");
        sb.AppendLine(new string('=', 58));
        sb.AppendLine($"Tarih: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("BAYAT | [İŞLETİM SİSTEMİ]");
        QueryWmi(sb, "SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem",
            m => $"{Val(m, "Caption")} | Sürüm: {Val(m, "Version")} | Build: {Val(m, "BuildNumber")} | {Val(m, "OSArchitecture")}");
        var compatibilityNotice = PlatformCompatibility.GetWindows10Notice();
        if (!string.IsNullOrWhiteSpace(compatibilityNotice))
            sb.AppendLine("Uyumluluk notu: " + compatibilityNotice);

        sb.AppendLine("\nBAYAT | [İŞLEMCİ]");
        QueryWmi(sb, "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            m => $"{Val(m, "Name")} | {Val(m, "Manufacturer")} | Çekirdek: {Val(m, "NumberOfCores")} | Mantıksal işlemci: {Val(m, "NumberOfLogicalProcessors")} | Azami saat: {Val(m, "MaxClockSpeed")} MHz");

        sb.AppendLine("\nBAYAT | [BELLEK]");
        try
        {
            ulong total = 0;
            using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory");
            foreach (ManagementObject m in searcher.Get())
                total += Convert.ToUInt64(m["Capacity"] ?? 0, CultureInfo.InvariantCulture);
            sb.AppendLine($"Takılı RAM: {total / 1024d / 1024d / 1024d:0.0} GB");
        }
        catch (Exception ex) { sb.AppendLine("RAM bilgisi okunamadı: " + ex.Message); }

        sb.AppendLine("\nBAYAT | [EKRAN KARTI]");
        QueryWmi(sb, "SELECT Name, DriverVersion FROM Win32_VideoController",
            m => $"{Val(m, "Name")} | Sürücü: {Val(m, "DriverVersion")}");

        sb.AppendLine("\nBAYAT | [DEPOLAMA]");
        QueryWmi(sb, "SELECT Model, Size, MediaType FROM Win32_DiskDrive",
            m => $"{Val(m, "Model")} | {FormatBytes(ParseUlong(m["Size"]))} | {Val(m, "MediaType")}");

        sb.AppendLine("\nBAYAT | [AĞ BAĞDAŞTIRICILARI]");
        NetworkInterface? active = null;
        var physicalAdapterIds = new HashSet<Guid>();
        var physicalMapAvailable = false;
        try
        {
            using var adapterSearcher = new ManagementObjectSearcher("SELECT GUID, PhysicalAdapter FROM Win32_NetworkAdapter WHERE GUID IS NOT NULL");
            foreach (ManagementObject adapter in adapterSearcher.Get())
            {
                var id = Convert.ToString(adapter["GUID"], CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(id) && Guid.TryParse(id, out var adapterGuid) &&
                    Convert.ToBoolean(adapter["PhysicalAdapter"] ?? false, CultureInfo.InvariantCulture))
                    physicalAdapterIds.Add(adapterGuid);
            }
            physicalMapAvailable = physicalAdapterIds.Count > 0;
        }
        catch { }
        var filterMarkers = new[] { "WFP", "QoS", "Native WiFi Filter", "Native Wi-Fi Filter", "NDIS Filter", "Filter Driver" };
        sb.AppendLine("Not: Filtre bileşenleri, döngüsel arabirimler ve kullanılabilir bağlantısı olmayan sanal kayıtlar listelenmez.");
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            var identity = ni.Name + " " + ni.Description;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || filterMarkers.Any(marker => identity.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                continue;
            var props = ni.GetIPProperties();
            var gateways = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Select(g => g.Address.ToString()).ToArray();
            var dns = props.DnsAddresses.Where(d => { var b = d.GetAddressBytes(); return b.Length != 16 || b[0] != 0xFE || (b[1] & 0xC0) != 0xC0; }).Select(d => d.ToString()).ToArray();
            var ipv4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal) && !IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).ToArray();
            var isPhysical = Guid.TryParse(ni.Id, out var interfaceGuid) && physicalAdapterIds.Contains(interfaceGuid);
            var fallbackPhysical = !physicalMapAvailable && (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            var isUp = ni.OperationalStatus == OperationalStatus.Up;
            if (!isPhysical && !fallbackPhysical && !(isUp && (ipv4.Length > 0 || gateways.Length > 0)))
                continue;
            sb.AppendLine($"• {ni.Name} ({ni.NetworkInterfaceType}) — {ni.OperationalStatus} — {FormatLinkSpeed(ni.Speed)}");
            if (ipv4.Length > 0) sb.AppendLine("  IPv4: " + string.Join(", ", ipv4));
            else sb.AppendLine("  IPv4: Kullanılabilir adres yok");
            if (gateways.Length > 0) sb.AppendLine("  Ağ geçidi: " + string.Join(", ", gateways));
            if (dns.Length > 0) sb.AppendLine("  DNS: " + string.Join(", ", dns));
            if (active is null && isUp && ipv4.Length > 0 && gateways.Length > 0)
                active = ni;
        }
        if (active is not null)
        {
            var gateway = active.GetIPProperties().GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            sb.AppendLine($"\nSeçilen aktif bağdaştırıcı: {active.Name}");
            sb.AppendLine("Ağ geçidi: " + (gateway ?? "Bulunamadı"));
            sb.AppendLine("\nBAYAT | [İLK DEĞERLENDİRME]");
            sb.AppendLine("• Bu rapor donanımı ve yerel ağ yapılandırmasını tanımlar; henüz kalıcı ayar değiştirmez.");
            sb.AppendLine("• DNS sunucusu değiştirmek oyun sunucusuna giden mevcut bağlantının ping değerini genellikle doğrudan düşürmez.");
            sb.AppendLine("• Ping testi sonucu hedef sunucuya, ISS'ye, Wi-Fi koşullarına ve yönlendirmeye göre değişir.");
            if (active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                sb.AppendLine("• Wi-Fi bağlantısı tespit edildi: modem yakınlığı, 5/6 GHz bandı ve kanal kalabalığı gecikmeyi DNS ayarından daha fazla etkileyebilir.");
            else if (active.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                sb.AppendLine("• Ethernet bağlantısı tespit edildi: kablo ve modem/router portunun bağlantı hızını kontrol edin.");
            else
                sb.AppendLine("• Sanal/VPN veya farklı türde bir ağ arabirimi olabilir; kalıcı ağ ayarı uygulamadan önce aktif arabirimi doğrulayın.");
        }
        else sb.AppendLine("Aktif ağ geçidi olan bir bağdaştırıcı tespit edilemedi.");

        sb.AppendLine("\n" + new string('=', 58));
        sb.AppendLine("Güvenlik: Hiçbir Registry/TCP gelişmiş ayarı otomatik ve körlemesine değiştirilmedi.");
        return new SystemReport(sb.ToString(), active);
    }

    private async void PingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taraması yapın ve aktif internet bağlantısı olduğundan emin olun.", "Ağ testi");
            return;
        }

        SetBusy(true, "Ping testi çalışıyor. Her hedef için 15 paket gönderilecek...");
        try
        {
            var gateway = _activeAdapter.GetIPProperties().GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var targets = new List<(string Label, string Host)>();
            if (!string.IsNullOrWhiteSpace(gateway)) targets.Add(("Yerel ağ geçidi", gateway));
            targets.Add(("Cloudflare", "1.1.1.1"));
            targets.Add(("Google DNS", "8.8.8.8"));

            var sb = new StringBuilder();
            sb.AppendLine("\nBAYAT | PING / JITTER / PAKET KAYBI TESTİ");
            sb.AppendLine(new string('-', 58));
            _lastPingResults.Clear();
            foreach (var target in targets)
            {
                StatusText.Text = $"Test ediliyor: {target.Label} ({target.Host})";
                var result = await MeasureHostAsync(target.Host, 15);
                _lastPingResults[target.Label] = result;
                sb.AppendLine($"{target.Label} — {target.Host}");
                sb.AppendLine($"  Başarılı: {result.Success}/15 | Paket kaybı: {result.Loss:0.0}%");
                sb.AppendLine($"  Ortalama: {(result.AverageMs.HasValue ? result.AverageMs.Value.ToString("0.0") + " ms" : "Ölçülemedi")}");
                sb.AppendLine($"  Jitter (ardışık başarılı yanıt farklarının ortalaması): {(result.JitterMs.HasValue ? result.JitterMs.Value.ToString("0.0") + " ms" : "Ölçülemedi")}");
                sb.AppendLine();
            }
            sb.AppendLine("Not: Bu testler genel internet hedeflerine yapılan örnek ölçümlerdir; oyun sunucusuna giden gerçek rotayı garanti etmez.");
            AppendLine(sb.ToString());
            StatusText.Text = "Ağ testi tamamlandı.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[PİNG TESTİ HATASI] " + ex.Message);
            StatusText.Text = "Ping testi tamamlanamadı.";
        }
        finally { SetBusy(false); }
    }

    private static async Task<PingResult> MeasureHostAsync(string host, int count)
    {
        var samples = new List<long>();
        int success = 0;
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 1400);
                if (reply.Status == IPStatus.Success)
                {
                    success++;
                    samples.Add(reply.RoundtripTime);
                }
            }
            catch { /* Timeout / unreachable counts as packet loss for this sample. */ }
            await Task.Delay(120);
        }
        double? avg = samples.Count == 0 ? null : samples.Average();
        double? jitter = samples.Count < 2 ? null : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new PingResult(success, (count - success) * 100d / count, avg, jitter);
    }

    private async void GameTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taraması yapın ve aktif bağlantıyı doğrulayın.", "Oyun sunucusu testi");
            return;
        }

        var host = GameHostText.Text.Trim();
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace) || host.Contains("://", StringComparison.Ordinal))
        {
            ShowBAYATMessageBox("Oyunun gösterdiği geçerli bir sunucu IP adresi veya alan adı girin. URL (https://...) girmeyin.", "Sunucu adresi gerekli");
            return;
        }

        int? tcpPort = null;
        var portText = GamePortText.Text.Trim();
        if (portText.Length > 0)
        {
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort is < 1 or > 65535)
            {
                ShowBAYATMessageBox("TCP portu 1 ile 65535 arasında bir sayı olmalıdır.", "Geçersiz port");
                return;
            }
            tcpPort = parsedPort;
        }

        SetBusy(true, "Oyun sunucusu çözümleniyor ve ağ rotası ölçülüyor...");
        try
        {
            IPAddress[] addresses;
            if (IPAddress.TryParse(host, out var literalAddress))
                addresses = new[] { literalAddress };
            else
                addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(8));

            var target = addresses
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                    ? _activeAdapter.Supports(NetworkInterfaceComponent.IPv4)
                    : _activeAdapter.Supports(NetworkInterfaceComponent.IPv6));
            if (target is null)
                throw new InvalidOperationException("Sunucu çözümlendi ancak aktif bağdaştırıcıyla uyumlu bir IP adresi bulunamadı.");

            var profile = CurrentGameProfile;
            var sb = new StringBuilder();
            sb.AppendLine($"\n{profile.ToUpperInvariant()} — HEDEFE ÖZEL AĞ TESTİ");
            sb.AppendLine(new string('=', 64));
            sb.AppendLine($"Girilen hedef: {host}");
            sb.AppendLine($"Test edilen IP: {target}");
            sb.AppendLine($"Aktif bağdaştırıcı: {_activeAdapter.Name} ({_activeAdapter.NetworkInterfaceType})");
            sb.AppendLine("Not: Bu test yalnızca girilen hedefi ölçer. Oyunun kullandığı gerçek sunucu adresi farklıysa sonuç oyun pingini temsil etmez.");
            sb.AppendLine();

            StatusText.Text = "Hedefe 15 ICMP ping gönderiliyor...";
            var pingResult = await MeasureHostAsync(target.ToString(), 15);
            _lastPingResults[$"Oyun sunucusu ({profile})"] = pingResult;
            sb.AppendLine("BAYAT | [ICMP PING]");
            sb.AppendLine($"Başarılı: {pingResult.Success}/15 | Paket kaybı: %{pingResult.Loss:0.0}");
            sb.AppendLine($"Ortalama RTT: {(pingResult.AverageMs.HasValue ? pingResult.AverageMs.Value.ToString("0.0") + " ms" : "ölçülemedi")}");
            sb.AppendLine($"Jitter tahmini: {(pingResult.JitterMs.HasValue ? pingResult.JitterMs.Value.ToString("0.0") + " ms" : "ölçülemedi")}");
            sb.AppendLine("Jitter, ardışık başarılı ICMP yanıt süreleri arasındaki mutlak farkların ortalamasıdır; oyunun kendi jitter metriğiyle aynı olmak zorunda değildir.");

            StatusText.Text = "Ağ rotası izleniyor (ICMP TTL yöntemi)...";
            sb.AppendLine("\nBAYAT | [ROTA / TRACEROUTE]");
            var hops = target.AddressFamily == AddressFamily.InterNetwork
                ? await TraceRouteAsync(target, 18, 850)
                : new List<string> { "IPv6 hedefi seçildi. Bu prototipte TTL tabanlı rota izi yalnızca IPv4 için etkin." };
            foreach (var hop in hops) sb.AppendLine(hop);
            sb.AppendLine("Not: Ara yönlendiriciler ICMP yanıtlarını engelleyebilir veya düşük önceliklendirebilir. '*' işareti tek başına arıza kanıtı değildir.");

            if (tcpPort.HasValue)
            {
                StatusText.Text = $"TCP {tcpPort.Value} bağlantı kontrolü yapılıyor...";
                sb.AppendLine($"\n[TCP BAĞLANTI KONTROLÜ — PORT {tcpPort.Value}]");
                var tcpResult = await MeasureTcpConnectAsync(target, tcpPort.Value, TimeSpan.FromSeconds(3));
                sb.AppendLine(tcpResult);
                sb.AppendLine("Önemli: Birçok oyun UDP kullanır. TCP portunun kapalı olması oyunun erişilemez olduğu anlamına gelmez; TCP bağlantı süresi oyun ping'i değildir.");
            }
            else
            {
                sb.AppendLine("\n[TCP KONTROLÜ] Port girilmedi; TCP bağlantı denemesi yapılmadı.");
            }

            sb.AppendLine("\nBAYAT | [PROFİL NOTU]");
            sb.AppendLine(GetGameProfileGuidance(profile));
            sb.AppendLine("Bu işlem yalnızca tanılama yaptı; ağ, DNS, Registry veya sürücü ayarı değiştirilmedi.");
            AppendLine(sb.ToString());
            StatusText.Text = $"{profile} hedef testi tamamlandı: {target}. Hiçbir ayar değiştirilmedi.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[OYUN SUNUCUSU TEST HATASI] " + ex.Message);
            StatusText.Text = "Hedef testi tamamlanamadı.";
        }
        finally { SetBusy(false); }
    }

    private async void NicPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taraması yapın.", "Ağ kartı analizi");
            return;
        }

        var adapterName = _activeAdapter.Name;
        var adapterIsWifi = _activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        var adapterIsEthernet = _activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet;
        SetBusy(true, "Ağ kartı sürücüsü, desteklenen seçenekler ve donanım uyumluluğu analiz ediliyor...");
        try
        {
            var script = "$ErrorActionPreference = 'Stop'\n" +
                         "$ProgressPreference = 'SilentlyContinue'\n" +
                         "Import-Module NetAdapter -ErrorAction Stop\n" +
                         "$adapter = Get-NetAdapter -Name " + PsQuote(adapterName) + " -ErrorAction Stop | Select-Object -First 1 Name, InterfaceDescription, Status, LinkSpeed, MediaType, PhysicalMediaType, DriverInformation, DriverFileName\n" +
                         "$physical = @(Get-NetAdapter -Name " + PsQuote(adapterName) + " -Physical -ErrorAction SilentlyContinue); $adapter | Add-Member -NotePropertyName IsPhysical -NotePropertyValue ($physical.Count -gt 0) -Force\n" +
                         "$wmiAdapter = Get-CimInstance -ClassName Win32_NetworkAdapter -ErrorAction SilentlyContinue | Where-Object { $_.NetConnectionID -eq $adapter.Name -or $_.Name -eq $adapter.InterfaceDescription } | Select-Object -First 1\n" +
                         "$signedDriver = $null; if ($null -ne $wmiAdapter -and -not [string]::IsNullOrWhiteSpace($wmiAdapter.PNPDeviceID)) { $signedDriver = Get-CimInstance -ClassName Win32_PnPSignedDriver -ErrorAction SilentlyContinue | Where-Object { $_.DeviceID -eq $wmiAdapter.PNPDeviceID } | Select-Object -First 1 }\n" +
                         "$adapter | Add-Member -NotePropertyName DriverVersion -NotePropertyValue $signedDriver.DriverVersion -Force; $adapter | Add-Member -NotePropertyName DriverDate -NotePropertyValue $signedDriver.DriverDate -Force; $adapter | Add-Member -NotePropertyName DriverProvider -NotePropertyValue $signedDriver.DriverProviderName -Force\n" +
                         "$properties = @(Get-NetAdapterAdvancedProperty -Name " + PsQuote(adapterName) + " -AllProperties -ErrorAction Stop | Select-Object DisplayName, DisplayValue, RegistryKeyword, RegistryValue, ValidDisplayValues, ValidRegistryValues)\n" +
                         "[pscustomobject]@{ Adapter = $adapter; Properties = $properties } | ConvertTo-Json -Depth 6 -Compress\n";
            var json = await RunPowerShellCaptureAsync(script, TimeSpan.FromSeconds(20));
            using var document = JsonDocument.Parse(json.Trim());
            var root = document.RootElement;
            if (!root.TryGetProperty("Adapter", out var adapter) || adapter.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Windows NetAdapter modülü seçilen bağdaştırıcıyı döndürmedi.");

            var propertyRoot = root.TryGetProperty("Properties", out var props) ? props : default;
            var rows = propertyRoot.ValueKind == JsonValueKind.Array
                ? propertyRoot.EnumerateArray().ToArray()
                : propertyRoot.ValueKind == JsonValueKind.Object ? new[] { propertyRoot } : Array.Empty<JsonElement>();
            var inventory = rows.Select(row => new NicOptimizationAdvisor.DriverProperty(
                JsonString(row, "DisplayName"),
                JsonString(row, "DisplayValue"),
                JsonString(row, "RegistryKeyword"),
                JsonString(row, "RegistryValue"),
                JsonStringArray(row, "ValidDisplayValues"),
                JsonStringArray(row, "ValidRegistryValues"))).ToArray();

            var hardwareAdapter = JsonBoolean(adapter, "IsPhysical");
            _lastNicProperties = inventory;
            _lastNicAdapterId = _activeAdapter?.Id;
            _lastNicIsPhysical = hardwareAdapter;
            var advice = NicOptimizationAdvisor.Evaluate(adapterIsWifi, adapterIsEthernet, hardwareAdapter, Environment.ProcessorCount, inventory);
            var nicCandidate = NicOptimizationAdvisor.CreateSafeRssCandidate(adapterIsWifi, adapterIsEthernet, hardwareAdapter, Environment.ProcessorCount, inventory);
            var sb = new StringBuilder();
            sb.AppendLine("\nAKILLI AĞ KARTI UYUMLULUK VE OPTİMİZASYON RAPORU — v0.9.0");
            sb.AppendLine(new string('=', 72));
            sb.AppendLine("Bağdaştırıcı: " + JsonString(adapter, "Name"));
            sb.AppendLine("Donanım: " + JsonString(adapter, "InterfaceDescription"));
            sb.AppendLine("Durum / link: " + JsonString(adapter, "Status") + " / " + JsonString(adapter, "LinkSpeed"));
            sb.AppendLine("Medya: " + JsonString(adapter, "MediaType") + " | Fiziksel medya: " + JsonString(adapter, "PhysicalMediaType"));
            sb.AppendLine("Windows NetAdapter -Physical doğrulaması: " + (hardwareAdapter ? "Evet" : "Hayır / doğrulanamadı"));
            sb.AppendLine("Sürücü: " + JsonString(adapter, "DriverVersion") + " | Tarih: " + JsonString(adapter, "DriverDate"));
            sb.AppendLine("Sürücü sağlayıcısı: " + JsonString(adapter, "DriverProvider"));
            sb.AppendLine("İşlemci mantıksal sayısı: " + Environment.ProcessorCount);
            var driverInfo = JsonString(adapter, "DriverInformation");
            if (!string.Equals(driverInfo, "Bilinmiyor", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("Sürücü bilgisi: " + driverInfo);

            sb.AppendLine("\nBAYAT | [ÖZELLİK BAZLI DEĞERLENDİRME]");
            foreach (var item in advice)
            {
                sb.AppendLine($"\n• {item.Feature}");
                sb.AppendLine("  Mevcut değer: " + item.CurrentValue);
                sb.AppendLine("  Değerlendirme: " + item.Assessment);
                sb.AppendLine("  Gerekçe: " + item.Reason);
            }

            sb.AppendLine("\nBAYAT | [GERİ ALINABİLİR OPTİMİZASYON ADAYI]");
            if (nicCandidate is null)
            {
                sb.AppendLine("Bu donanım/sürücü için güvenli RSS değişiklik adayı üretmedi. Hiçbir ayar değiştirilmeyecek.");
                sb.AppendLine("Nedenler arasında fiziksel donanım bağdaştırıcısının doğrulanamaması, Wi-Fi bağlantısı, 4'ten az mantıksal işlemci, RSS'nin zaten açık olması, standart *RSS anahtarının bulunmaması veya sürücünün Enabled/RegistryValue eşlemesini açıkça sunmaması bulunabilir.");
            }
            else
            {
                sb.AppendLine($"Aday: {nicCandidate.DisplayName}");
                sb.AppendLine($"Mevcut: {nicCandidate.OriginalDisplayValue} (Registry={nicCandidate.OriginalRegistryValue})");
                sb.AppendLine($"Test hedefi: {nicCandidate.TargetDisplayValue} (Registry={nicCandidate.TargetRegistryValue})");
                sb.AppendLine("Adayı uygulamak için 'Uygun RSS Testini Uygula' düğmesini kullanın. Önce yedek alınır ve UAC onayı istenir.");
            }

            sb.AppendLine("\nBAYAT | [SÜRÜCÜNÜN YAYINLADIĞI GELİŞMİŞ ÖZELLİKLER]");
            if (inventory.Length == 0) sb.AppendLine("Sürücü listelenebilir özellik döndürmedi.");
            foreach (var item in inventory.Take(120))
            {
                sb.AppendLine($"• {item.DisplayName}: {item.DisplayValue}");
                if (!string.IsNullOrWhiteSpace(item.RegistryKeyword)) sb.AppendLine("  Anahtar: " + item.RegistryKeyword);
                if (item.ValidDisplayValues.Count > 0)
                    sb.AppendLine("  Sürücünün bildirdiği seçenekler: " + string.Join(", ", item.ValidDisplayValues));
            }
            if (inventory.Length > 120) sb.AppendLine($"... {inventory.Length - 120} özellik daha listelenmedi.");

            sb.AppendLine("\nBAYAT | [GÜVENLİK VE SONRAKİ ADIM]");
            sb.AppendLine("Analiz düğmesi salt okunurdur. Yalnızca ayrıca onaylanan, desteklenen Ethernet RSS adayı yedekle uygulanabilir; adapter otomatik yeniden başlatılmaz. Önce/sonra performans karşılaştırması ayrıca alınır.");
            sb.AppendLine("Bir özelliğin seçenekler arasında görünmesi, her sistemde aynı değerin en iyi olduğu anlamına gelmez. Değişiklikler için sürücüye özgü yedek, tek tek onay, uygulama sonrası doğrulama ve geri alma gerekir.");
            sb.AppendLine("Önerileri oyun ping'iyle doğrulamak için aynı sunucuya, aynı bağlantı türünde, benzer ağ yükü ve benzer süre koşullarında önce/sonra ölçüm alın.");
            AppendLine(sb.ToString());
            StatusText.Text = nicCandidate is null
                ? $"{inventory.Length} sürücü özelliği analiz edildi. Güvenli RSS adayı bulunmadı."
                : $"{inventory.Length} sürücü özelliği analiz edildi. Kontrollü RSS adayı hazır; uygulama için ayrı onay gerekir.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[AĞ KARTI ANALİZİ HATASI] " + ex.Message);
            StatusText.Text = "Ağ kartı analizi tamamlanamadı. NetAdapter modülünü ve sürücü desteğini kontrol edin.";
        }
        finally { SetBusy(false); }
    }

    private async Task<NicSnapshot> ReadNicSnapshotAsync(string adapterName)
    {
        var json = await RunPowerShellCaptureAsync(NicSettingsWorkflow.BuildSnapshotScript(adapterName), TimeSpan.FromSeconds(20));
        using var document = JsonDocument.Parse(json.Trim());
        var root = document.RootElement;
        if (!root.TryGetProperty("Adapter", out var adapter) || adapter.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Windows NetAdapter modülü ağ bağdaştırıcısı bilgisi döndürmedi.");

        var interfaceGuid = JsonString(adapter, "InterfaceGuid");
        if (!Guid.TryParse(interfaceGuid, out _))
            throw new InvalidOperationException("Windows ağ bağdaştırıcısı için benzersiz GUID okunamadı.");

        var propertyRoot = root.TryGetProperty("Properties", out var props) ? props : default;
        var rows = propertyRoot.ValueKind == JsonValueKind.Array
            ? propertyRoot.EnumerateArray().ToArray()
            : propertyRoot.ValueKind == JsonValueKind.Object ? new[] { propertyRoot } : Array.Empty<JsonElement>();
        var inventory = rows.Select(row => new NicOptimizationAdvisor.DriverProperty(
            JsonString(row, "DisplayName"),
            JsonString(row, "DisplayValue"),
            JsonString(row, "RegistryKeyword"),
            JsonString(row, "RegistryValue"),
            JsonStringArray(row, "ValidDisplayValues"),
            JsonStringArray(row, "ValidRegistryValues"))).ToArray();

        return new NicSnapshot(
            interfaceGuid,
            JsonString(adapter, "Name"),
            JsonString(adapter, "InterfaceDescription"),
            JsonBoolean(adapter, "IsPhysical"),
            inventory);
    }

    private async void ApplyNicSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockPrivilegedNetworkChanges()) return;
        var activeAdapter = _activeAdapter;
        if (activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistemi tarayın ve aktif ağ bağdaştırıcısını doğrulayın.", "NIC optimizasyonu");
            return;
        }
        if (File.Exists(NicBackupPath))
        {
            ShowBAYATMessageBox("Bir NIC ayar yedeği zaten var. İlk yedeği korumak için önce 'Önceki Ağ Kartı Ayarını Geri Yükle' işlemini tamamlayın.", "NIC yedeği mevcut");
            return;
        }
        try
        {
            var abSession = await LoadAbSessionAsync();
            var beforeRun = abSession.Before!;
            if (abSession.After is not null || !abSession.AdapterId.Equals(activeAdapter.Id, StringComparison.OrdinalIgnoreCase) ||
                beforeRun.SuccessCount < Math.Ceiling(beforeRun.RequestedSamples * 0.75))
            {
                ShowBAYATMessageBox("RSS A/B testinden önce aynı bağdaştırıcı için en az %75 başarılı yanıt içeren yeni bir Aşama A ön ölçümü alın. Mevcut oturum farklı adaptöre ait, yetersiz veya zaten tamamlanmış.", "Ön ölçüm gerekli");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("RSS testi için geçerli bir A/B ön ölçümü gerekli. Önce Aşama A ölçümünü alın.\n\nAyrıntı: " + ex.Message, "Ön ölçüm gerekli");
            return;
        }

        SetBusy(true, "Sürücünün gerçek desteklediği değerler yeniden okunuyor...");
        NicSettingsBackup? backup = null;
        try
        {
            var snapshot = await ReadNicSnapshotAsync(activeAdapter.Name);
            var isWifi = activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            var isEthernet = activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet;
            var candidate = NicOptimizationAdvisor.CreateSafeRssCandidate(isWifi, isEthernet, snapshot.IsHardwareInterface,
                Environment.ProcessorCount, snapshot.Properties);
            if (candidate is null)
            {
                ShowBAYATMessageBox(
                    "Bu bağdaştırıcı için güvenli RSS değişiklik adayı bulunamadı.\n\nProgram yalnızca fiziksel Ethernet bağdaştırıcısı, en az 4 mantıksal işlemci, RSS kapalı ve standart *RSS anahtarı için sürücünün açıkça sunduğu Enabled/RegistryValue eşlemesi koşullarında değişiklik önerir. Başka ayarlara dokunulmadı.",
                    "Değişiklik uygulanmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Uygun RSS adayı bulunmadı; hiçbir ayar değiştirilmedi.";
                return;
            }

            backup = new NicSettingsBackup(1, snapshot.InterfaceGuid, snapshot.InterfaceName,
                snapshot.InterfaceDescription, DateTimeOffset.Now, new List<NicPropertyBackup> { candidate });
            NicSettingsWorkflow.ValidateBackup(backup);

            var confirm = ShowBAYATMessageBox(
                $"Ağ kartı: {snapshot.InterfaceName}\nDonanım: {snapshot.InterfaceDescription}\n\nÖzellik: {candidate.DisplayName}\nMevcut değer: {candidate.OriginalDisplayValue}\nYeni test değeri: {candidate.TargetDisplayValue}\n\nÖnceki Registry değeri yedeklenecek. Windows yönetici izni isteyecek. Adaptör otomatik yeniden başlatılmayacak; sürücü yeni değerin etkinleşmesi için daha sonra adaptör yeniden başlatması veya Windows yeniden başlatması isteyebilir. Bu sırada bağlantıda kesinti yaşanabilir.\n\nBu değişiklik ping düşüşü garanti etmez; ölçümlü A/B testi içindir. Devam edilsin mi?",
                "NIC A/B testini onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            Directory.CreateDirectory(_appDataDir);
            var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
            var tempBackup = NicBackupPath + ".tmp";
            await File.WriteAllTextAsync(tempBackup, json, new UTF8Encoding(false));
            File.Move(tempBackup, NicBackupPath, overwrite: false);

            StatusText.Text = "NIC ayarı değiştiriliyor. Yönetici izni isteği açılacak...";
            await RunElevatedPowerShellAsync(NicSettingsWorkflow.BuildApplyScript(backup));

            // Verify the driver-reported registry value after the elevated operation.
            var after = await ReadNicSnapshotAsync(snapshot.InterfaceName);
            var actual = after.Properties.SingleOrDefault(p => p.RegistryKeyword.Equals(candidate.RegistryKeyword, StringComparison.OrdinalIgnoreCase));
            if (actual is null || !actual.RegistryValue.Equals(candidate.TargetRegistryValue, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sürücü, uygulanan Registry değerini yeniden okumada doğrulamadı.");

            AppendLine($"\n[NIC A/B TESTİ] {snapshot.InterfaceName} — {candidate.DisplayName}: {candidate.OriginalDisplayValue} → {candidate.TargetDisplayValue}. Önceki değer nic-backup.json dosyasına kaydedildi.");
            AppendLine("Adapter otomatik yeniden başlatılmadı. Ayarın etkinleşmesi için Windows veya bağdaştırıcı yeniden başlatması gerekebilir. Önce/sonra testini aynı oyun sunucusunda yapın.");
            StatusText.Text = "RSS test değeri uygulandı ve sürücüden doğrulandı. Geri alma yedeği hazır.";
        }
        catch (OperationCanceledException)
        {
            // UAC cancellation means the elevated process did not start.
            try { if (File.Exists(NicBackupPath)) File.Delete(NicBackupPath); } catch { }
            try { if (File.Exists(NicBackupPath + ".tmp")) File.Delete(NicBackupPath + ".tmp"); } catch { }
            StatusText.Text = "Yönetici izni verilmedi; NIC ayarı değiştirilmedi.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[NIC OPTİMİZASYON HATASI] " + ex.Message);
            if (backup is not null && File.Exists(NicBackupPath))
            {
                try
                {
                    StatusText.Text = "İşlem doğrulanamadı; önceki NIC değeri geri yükleniyor...";
                    await RunElevatedPowerShellAsync(NicSettingsWorkflow.BuildRestoreScript(backup, requireTargetCurrent: false));
                    File.Delete(NicBackupPath);
                    AppendLine("[NIC OTOMATİK GERİ ALMA] Önceki Registry değeri sürücüye geri yazıldı.");
                    StatusText.Text = "NIC işlemi başarısız oldu; önceki ayar geri yüklendi.";
                }
                catch (Exception rollbackError)
                {
                    AppendLine("[NIC GERİ ALMA UYARISI] " + rollbackError.Message + " Yedek korundu; geri yükleme düğmesini kullanın.");
                    StatusText.Text = "NIC işlemi tamamlanamadı. Geri alma yedeği korundu.";
                }
            }
            else StatusText.Text = "NIC ayarı uygulanamadı; yedek bulunmuyor veya işlem başlamadı.";
        }
        finally { SetBusy(false); UpdateNicRestoreButton(); }
    }

    private async void RestoreNicSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockPrivilegedNetworkChanges()) return;
        if (!File.Exists(NicBackupPath))
        {
            ShowBAYATMessageBox("Geri yüklenecek NIC ayar yedeği bulunamadı.", "NIC geri yükleme");
            UpdateNicRestoreButton();
            return;
        }

        NicSettingsBackup? backup = null;
        try
        {
            backup = JsonSerializer.Deserialize<NicSettingsBackup>(await File.ReadAllTextAsync(NicBackupPath));
            if (backup is null) throw new InvalidDataException("NIC yedeği okunamadı.");
            NicSettingsWorkflow.ValidateBackup(backup);
            var details = string.Join(Environment.NewLine, backup.Properties.Select(p =>
                $"{p.DisplayName}: {p.TargetDisplayValue} → {p.OriginalDisplayValue}"));
            var confirm = ShowBAYATMessageBox(
                $"'{backup.InterfaceName}' bağdaştırıcısında Smart Network Optimizer tarafından değiştirilen ayarlar geri yüklenecek:\n\n{details}\n\nÖzellik uygulamadan sonra dışarıdan değiştirilmişse işlem durdurulur ve yedek korunur. Adaptör otomatik yeniden başlatılmaz. Devam edilsin mi?",
                "NIC ayarlarını geri yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true, "Önceki ağ kartı değerleri geri yükleniyor...");
            await RunElevatedPowerShellAsync(NicSettingsWorkflow.BuildRestoreScript(backup));
            File.Delete(NicBackupPath);
            AppendLine($"\n[NIC GERİ YÜKLEME] '{backup.InterfaceName}' için önceki sürücü değerleri geri yüklendi ve doğrulandı.");
            StatusText.Text = "Önceki NIC ayarları geri yüklendi. Sürücü gerek görürse adaptör/Windows yeniden başlatması isteyebilir.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Yönetici izni verilmedi; NIC yedeği korundu ve ayar değişmedi.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[NIC GERİ YÜKLEME HATASI] " + ex.Message);
            StatusText.Text = "NIC geri yükleme tamamlanamadı; yedek dosyası korundu.";
            ShowBAYATMessageBox(ex.Message + "\n\nYedek dosyası silinmedi.", "NIC geri yükleme hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); UpdateNicRestoreButton(); }
    }

    private async void CaptureBaselineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taramasını tamamlayın.", "A/B ön ölçümü");
            return;
        }
        var host = GameHostText.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowBAYATMessageBox("Aynı oyun oturumunda kullandığınız gerçek sunucu IP adresini veya alan adını üstteki sunucu alanına yazın. Ardından Aşama A'yı alın.", "Hedef gerekli");
            GameHostText.Focus();
            return;
        }
        if (File.Exists(AbBenchmarkPath))
        {
            var replace = ShowBAYATMessageBox("Yeni A/B oturumu mevcut önce/sonra ölçüm dosyasının yerini alacak. Eski raporu önce dışa aktardıysanız devam edin. Yeni oturum oluşturulsun mu?",
                "A/B oturumunu yenile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (replace != MessageBoxResult.Yes) return;
        }

        SetBusy(true, "Aşama A: hedef çözümleniyor ve 20 ICMP örneği ölçülüyor...");
        try
        {
            var target = await PerformanceBenchmark.ResolveTargetAsync(host);
            var run = await PerformanceBenchmark.CaptureAsync(target, PerformanceBenchmark.DefaultSampleCount,
                (current, total) => StatusText.Text = $"Aşama A ölçülüyor: {current}/{total} — {target}");
            var session = new PerformanceBenchmarkSession(
                PerformanceBenchmark.SchemaVersion,
                Guid.NewGuid().ToString("D"),
                host,
                target.ToString(),
                CurrentGameProfile,
                _activeAdapter.Id,
                _activeAdapter.Name,
                _activeAdapter.Description,
                _activeAdapter.NetworkInterfaceType.ToString(),
                _activeAdapter.Speed,
                Environment.ProcessorCount,
                DateTimeOffset.Now,
                run,
                null);
            PerformanceBenchmark.ValidateSession(session);
            await SaveAbSessionAsync(session);

            var report = new StringBuilder();
            report.AppendLine("\nBAYAT | [A/B AŞAMA A — ÖN ÖLÇÜM KAYDEDİLDİ]");
            report.AppendLine($"Hedef: {host} → {target} (Aşama B'de aynı IP kullanılacak)");
            report.AppendLine($"Bağdaştırıcı: {_activeAdapter.Name} ({_activeAdapter.NetworkInterfaceType})");
            report.AppendLine($"Yanıt: {run.SuccessCount}/{run.RequestedSamples} | Kayıp: %{run.LossPercent:0.0} | Medyan: {run.MedianMs?.ToString("0.0") ?? "—"} ms | P95: {run.P95Ms?.ToString("0.0") ?? "—"} ms | Jitter: {run.JitterMs?.ToString("0.0") ?? "—"} ms");
            report.AppendLine("Şimdi isterseniz desteklenen RSS değişikliğini uygulayın. Sürücü yeniden başlatma isterse B ölçümünden önce bu gereksinimi tamamlayın.");
            AppendLine(report.ToString());
            StatusText.Text = "Aşama A kaydedildi. RSS testi veya diğer tekil değişiklikten sonra Aşama B'yi alın.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[A/B AŞAMA A HATASI] " + ex.Message);
            StatusText.Text = "Ön ölçüm kaydedilemedi.";
            ShowBAYATMessageBox(ex.Message, "A/B ön ölçüm hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); UpdateAbButtons(); UpdateNicRestoreButton(); }
    }

    private async void CaptureAfterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taramasını tamamlayın.", "A/B son ölçümü");
            return;
        }

        PerformanceBenchmarkSession session;
        try { session = await LoadAbSessionAsync(); }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("Önce Aşama A ön ölçümünü alın.\n\n" + ex.Message, "A/B oturumu bulunamadı");
            return;
        }

        if (!session.AdapterId.Equals(_activeAdapter.Id, StringComparison.OrdinalIgnoreCase))
        {
            ShowBAYATMessageBox("Aktif bağdaştırıcı Aşama A ölçümündeki bağdaştırıcıyla aynı değil. Aynı adaptörü seçip yeniden tarayın; ölçümler farklı arayüzlerden karşılaştırılmamalı.", "Bağdaştırıcı değişti");
            return;
        }
        if (!GameHostText.Text.Trim().Equals(session.RequestedHost, StringComparison.OrdinalIgnoreCase))
        {
            ShowBAYATMessageBox($"Aşama A hedefi '{session.RequestedHost}' idi. Aynı oturumu karşılaştırmak için sunucu alanını tekrar bu değere getirin.", "Hedef değişti");
            return;
        }
        if (session.After is not null)
        {
            var replace = ShowBAYATMessageBox("Mevcut Aşama B ölçümünün üzerine yeni ölçüm yazılacak. Devam edilsin mi?",
                "Aşama B'yi yenile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (replace != MessageBoxResult.Yes) return;
        }

        var nicBackupExists = File.Exists(NicBackupPath);
        if (nicBackupExists)
        {
            var confirm = ShowBAYATMessageBox("NIC ayar yedeği mevcut. Aşama B'yi almadan önce sürücü değişikliğinin gerçekten etkinleştiğini ve gerekiyorsa adaptör/Windows yeniden başlatmasının tamamlandığını doğrulayın. Devam edilsin mi?",
                "Değişikliğin etkinliği", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        SetBusy(true, "Aşama B: Aşama A ile aynı hedef IP üzerinde ölçüm yapılıyor...");
        try
        {
            var target = IPAddress.Parse(session.ResolvedAddress);
            var run = await PerformanceBenchmark.CaptureAsync(target, session.Before!.RequestedSamples,
                (current, total) => StatusText.Text = $"Aşama B ölçülüyor: {current}/{total} — {target}");
            session = session with { After = run };
            PerformanceBenchmark.ValidateSession(session);
            await SaveAbSessionAsync(session);
            AppendLine($"\n[A/B AŞAMA B — SON ÖLÇÜM KAYDEDİLDİ]\nHedef: {target} | Yanıt: {run.SuccessCount}/{run.RequestedSamples} | Kayıp: %{run.LossPercent:0.0} | Medyan: {run.MedianMs?.ToString("0.0") ?? "—"} ms | P95: {run.P95Ms?.ToString("0.0") ?? "—"} ms | Jitter: {run.JitterMs?.ToString("0.0") ?? "—"} ms");
            StatusText.Text = "Aşama B kaydedildi. A/B karşılaştırmasını çalıştırabilirsiniz.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[A/B AŞAMA B HATASI] " + ex.Message);
            StatusText.Text = "Son ölçüm kaydedilemedi.";
            ShowBAYATMessageBox(ex.Message, "A/B son ölçüm hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); UpdateAbButtons(); }
    }

    private async void CompareAbButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var session = await LoadAbSessionAsync();
            var report = PerformanceBenchmark.BuildComparisonReport(session);
            var historyEntry = LocalPerformanceHistory.FromSession(session);
            var added = await LocalPerformanceHistory.AddIfMissingAsync(PerformanceHistoryPath, historyEntry);
            ClearHistoryButton.IsEnabled = File.Exists(PerformanceHistoryPath);
            OutputText.Text = report + Environment.NewLine;
            AppendLine(added
                ? "[YEREL GEÇMİŞ] Bu A/B sonucu gizlilik dostu özet olarak yerel geçmişe kaydedildi."
                : "[YEREL GEÇMİŞ] Bu A/B oturumu daha önce kaydedilmiş; yinelenen kayıt oluşturulmadı.");
            OutputText.ScrollToEnd();
            StatusText.Text = "A/B karşılaştırması oluşturuldu ve yerel geçmiş güncellendi. Raporu TXT olarak kaydedebilirsiniz.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox(ex.Message, "A/B karşılaştırması", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await LocalPerformanceHistory.LoadAsync(PerformanceHistoryPath);
            var report = LocalPerformanceHistory.BuildReport(history);
            OutputText.Text = report;
            OutputText.ScrollToEnd();
            StatusText.Text = $"Yerel performans geçmişi gösterildi: {history.Entries.Count} kayıt.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("Yerel performans geçmişi okunamadı: " + ex.Message, "Performans geçmişi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(PerformanceHistoryPath))
        {
            ShowBAYATMessageBox("Silinecek yerel performans geçmişi bulunamadı.", "Performans geçmişi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = ShowBAYATMessageBox(
            "Smart Network Optimizer tarafından bu bilgisayarda tutulan özet A/B performans geçmişi silinecek. DNS ve NIC geri yükleme yedekleri etkilenmez. Devam edilsin mi?",
            "Yerel geçmişi sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            await LocalPerformanceHistory.ClearAsync(PerformanceHistoryPath);
            ClearHistoryButton.IsEnabled = false;
            AppendLine("\n[YEREL GEÇMİŞ] Performans geçmişi kullanıcı isteğiyle silindi.");
            StatusText.Text = "Yerel performans geçmişi silindi.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("Performans geçmişi silinemedi: " + ex.Message, "Dosya hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetAbButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AbBenchmarkPath))
        {
            UpdateAbButtons();
            return;
        }
        var confirm = ShowBAYATMessageBox("Kaydedilmiş A/B ölçüm oturumu silinecek. Önce raporu TXT olarak dışa aktardığınızdan emin olun. Devam edilsin mi?",
            "A/B oturumunu sıfırla", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(AbBenchmarkPath);
            try { File.Delete(AbBenchmarkPath + ".tmp"); } catch { }
            AppendLine("\n[A/B OTURUMU] Ölçüm dosyası kullanıcı isteğiyle silindi.");
            StatusText.Text = "A/B oturumu sıfırlandı.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("A/B oturumu silinemedi: " + ex.Message, "Dosya hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateAbButtons();
    }

    private async Task<PerformanceBenchmarkSession> LoadAbSessionAsync()
    {
        if (!File.Exists(AbBenchmarkPath)) throw new FileNotFoundException("Kaydedilmiş A/B oturumu bulunamadı.");
        var json = await File.ReadAllTextAsync(AbBenchmarkPath);
        var session = JsonSerializer.Deserialize<PerformanceBenchmarkSession>(json)
            ?? throw new InvalidDataException("A/B oturum dosyası okunamadı.");
        PerformanceBenchmark.ValidateSession(session);
        return session;
    }

    private async Task SaveAbSessionAsync(PerformanceBenchmarkSession session)
    {
        Directory.CreateDirectory(_appDataDir);
        var temporary = AbBenchmarkPath + ".tmp";
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, AbBenchmarkPath, overwrite: true);
    }

    private void UpdateAbButtons()
    {
        var hasSession = File.Exists(AbBenchmarkPath);
        CaptureAfterButton.IsEnabled = hasSession;
        CompareAbButton.IsEnabled = hasSession;
        ResetAbButton.IsEnabled = hasSession;
    }

    private async void DiscoverGameConnectionsButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Çalışan oyun süreçleri ve bunlara ait bağlantı meta verileri okunuyor...");
        try
        {
            var snapshot = await GameConnectionDiscovery.DiscoverAsync();
            var sb = new StringBuilder();
            sb.AppendLine("\nÇALIŞAN OYUNLAR VE BAĞLANTI ENVANTERİ — SALT OKUNUR");
            sb.AppendLine(new string('=', 68));
            sb.AppendLine("Tarama zamanı: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Bu tarama yerel süreç adlarını, PID'leri, yaklaşık bellek kullanımını ve Windows'un sunduğu uç nokta meta verilerini okur.");
            sb.AppendLine("Paket içeriği, parola, mesaj veya oyun belleği okunmaz; dışarıya veri gönderilmez.");
            sb.AppendLine();

            if (snapshot.Processes.Count == 0)
            {
                sb.AppendLine("Desteklenen oyunlardan çalışan bir süreç bulunamadı.");
                sb.AppendLine("Desteklenen profiller: Valorant, Counter-Strike 2, PUBG ve Fortnite.");
            }
            else
            {
                sb.AppendLine("BAYAT | [TESPİT EDİLEN OYUN SÜREÇLERİ]");
                foreach (var game in snapshot.Processes)
                {
                    sb.AppendLine($"• {game.Profile} — {game.ProcessName} | PID: {game.ProcessId} | RAM: {game.WorkingSetMb:0.0} MB");
                    if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
                        sb.AppendLine("  Dosya: " + game.ExecutablePath);
                }

                var distinctProfiles = snapshot.Processes.Select(p => p.Profile).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (distinctProfiles.Length == 1)
                {
                    var index = distinctProfiles[0] switch
                    {
                        "Valorant" => 1,
                        "Counter-Strike 2" => 2,
                        "PUBG" => 3,
                        "Fortnite" => 4,
                        _ => 0
                    };
                    GameProfileCombo.SelectedIndex = index;
                    sb.AppendLine("Oyun profili, tek oyun ailesi bulunduğu için otomatik seçildi: " + distinctProfiles[0]);
                }
                else if (distinctProfiles.Length > 1)
                {
                    sb.AppendLine("Birden fazla oyun ailesi bulundu; mevcut profil seçimi değiştirilmedi.");
                }

                sb.AppendLine("\nBAYAT | [TCP — ETKİN BAĞLANTILAR]");
                var tcpCount = 0;
                foreach (var game in snapshot.Processes)
                {
                    var rows = snapshot.TcpEndpoints.Where(x => x.ProcessId == game.ProcessId).ToArray();
                    foreach (var endpoint in rows)
                    {
                        if (tcpCount++ >= 80) break;
                        sb.AppendLine($"• {game.ProcessName} (PID {game.ProcessId}): {endpoint.LocalAddress}:{endpoint.LocalPort} → {endpoint.RemoteAddress}:{endpoint.RemotePort} [{endpoint.State}]");
                    }
                    if (tcpCount >= 80) break;
                }
                if (tcpCount == 0) sb.AppendLine("Tespit edilen oyun süreçleri için etkin TCP bağlantısı bulunmadı.");
                else sb.AppendLine("Uyarı: Bu TCP uçları aynı sürece aittir; oyun sunucusu, oturum doğrulama veya yardımcı servis bağlantısı olduğu kesin değildir.");

                sb.AppendLine("\nBAYAT | [UDP — YEREL UÇ NOKTALARI]");
                var udpCount = 0;
                foreach (var game in snapshot.Processes)
                {
                    var rows = snapshot.UdpEndpoints.Where(x => x.ProcessId == game.ProcessId).ToArray();
                    foreach (var endpoint in rows)
                    {
                        if (udpCount++ >= 80) break;
                        sb.AppendLine($"• {game.ProcessName} (PID {game.ProcessId}): {endpoint.LocalAddress}:{endpoint.LocalPort} (yerel UDP portu)");
                    }
                    if (udpCount >= 80) break;
                }
                if (udpCount == 0) sb.AppendLine("Tespit edilen oyun süreçleri için UDP yerel uç noktası bulunmadı.");
                else sb.AppendLine("Windows Get-NetUDPEndpoint yerel UDP adresi/portu ve PID bilgisini verir; uzak UDP sunucu IP'sini vermez.");
            }

            sb.AppendLine("\nBAYAT | [OTOMATİK HEDEF SEÇİMİ YAPILMADI]");
            sb.AppendLine("Süreçle ilişkili bir TCP uzak ucu bile oyun sunucusu olduğunun kanıtı değildir. UDP kullanan oyunlarda uzak eş IP'si bu salt okunur uç nokta tablosundan çıkarılamaz.");
            sb.AppendLine("Bu nedenle hiçbir IP otomatik olarak oyun sunucusu alanına yazılmadı ve hiçbir ağ ayarı değiştirilmedi.");
            sb.AppendLine("\nGizlilik: İşlem yalnızca bu bilgisayarda çalışır. Süreç/bağlantı listesi sunucuya gönderilmez.");

            _lastGameDiscoveryReport = sb.ToString();
            AppendLine(_lastGameDiscoveryReport);
            StatusText.Text = snapshot.Processes.Count == 0
                ? "Oyun süreci bulunamadı; hiçbir ayar değiştirilmedi."
                : $"{snapshot.Processes.Count} oyun süreci bulundu; bağlantı envanteri tamamlandı.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[OYUN SÜRECİ TARAMA HATASI] " + ex.Message);
            StatusText.Text = "Oyun süreci/bağlantı taraması tamamlanamadı.";
        }
        finally { SetBusy(false); }
    }

    private static bool JsonBoolean(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False || value.ValueKind == JsonValueKind.Null) return false;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static string JsonString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString() : "Bilinmiyor";
    }

    private static IReadOnlyList<string> JsonStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Array.Empty<string>();
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(v => v.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            return new[] { value.ToString() };
        return Array.Empty<string>();
    }

    private string CurrentGameProfile => GameProfileCombo.SelectedItem is ComboBoxItem item
        ? item.Content?.ToString() ?? "Özel sunucu" : "Özel sunucu";

    private static string GetGameProfileGuidance(string profile) => profile switch
    {
        "Valorant" => "Valorant istemcisinin ağ grafikleri ve maç içi sunucu bölgesiyle aynı oturumu doğrulayın. Gelişigüzel IP/port varsayımı yapılmaz; istemcinin gösterdiği gerçek hedef kullanılmalıdır.",
        "Counter-Strike 2" => "Steam/CS2 sunucu tarayıcısı veya maç içi ağ istatistiklerinden gerçek sunucu IP'sini alın. Sunucu değişebileceği için eski maç adresi yeni maç için geçerli olmayabilir.",
        "PUBG" => "PUBG maçındaki gerçek bölge ve sunucu hedefi esas alınmalıdır. Bölge seçimi ve oyunun kendi ağ göstergesiyle karşılaştırın; genel IP testi tek başına oyun rotasını kanıtlamaz.",
        "Fortnite" => "Fortnite eşleştirme bölgesi ve oyun içi ağ istatistiklerini referans alın. Eşleştirme sunucusu maçtan maça değişebilir; sabit bir IP varsayılmamalıdır.",
        _ => "Özel sunucuda test ettiğiniz adresin gerçekten oyun oturumunun hedefi olduğundan emin olun. CDN, web sitesi veya DNS adresi oyun sunucusunun adresi olmayabilir."
    };

    private static async Task<List<string>> TraceRouteAsync(IPAddress destination, int maxHops, int timeoutMs)
    {
        var results = new List<string>();
        var payload = Encoding.ASCII.GetBytes("SNO-Route-Probe");
        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(destination, timeoutMs, payload, new PingOptions(ttl, true));
                if (reply.Status == IPStatus.TtlExpired)
                    results.Add($"{ttl,2}. {reply.Address?.ToString() ?? "?"}  {reply.RoundtripTime} ms");
                else if (reply.Status == IPStatus.Success)
                {
                    results.Add($"{ttl,2}. HEDEF: {reply.Address?.ToString() ?? destination.ToString()}  {reply.RoundtripTime} ms");
                    break;
                }
                else
                    results.Add($"{ttl,2}. *  ({reply.Status})");
            }
            catch (Exception ex) when (ex is PingException || ex is SocketException || ex is InvalidOperationException)
            {
                results.Add($"{ttl,2}. *  ({ex.GetType().Name})");
            }
        }
        return results;
    }

    private static async Task<string> MeasureTcpConnectAsync(IPAddress address, int port, TimeSpan timeout)
    {
        using var client = new TcpClient(address.AddressFamily);
        var watch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(address, port).WaitAsync(timeout);
            watch.Stop();
            return $"TCP bağlantısı başarılı. Bağlantı kurulma süresi: {watch.Elapsed.TotalMilliseconds:0.0} ms";
        }
        catch (Exception ex) when (ex is SocketException || ex is TimeoutException || ex is OperationCanceledException)
        {
            watch.Stop();
            return $"TCP bağlantısı kurulamadı ({ex.GetType().Name}). Süre: {watch.Elapsed.TotalMilliseconds:0.0} ms. Bu durum UDP tabanlı oyun trafiği hakkında sonuç vermez.";
        }
    }

    private void RecommendationsButton_Click(object sender, RoutedEventArgs e)
    {
        var activeAdapter = _activeAdapter;
        if (activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taraması yapın ve aktif internet bağlantısı olduğundan emin olun.", "Akıllı öneriler");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("BAYAT | SMART NETWORK OPTIMIZER — KİŞİYE ÖZEL ÖNERİLER");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine($"Bağdaştırıcı: {activeAdapter.Name}");
        sb.AppendLine($"Tür: {activeAdapter.NetworkInterfaceType} | Link hızı: {FormatLinkSpeed(activeAdapter.Speed)}");
        sb.AppendLine();

        sb.AppendLine("BAYAT | [ÖLÇÜLEBİLEN DURUM]");
        try
        {
            var stats = activeAdapter.GetIPStatistics();
            sb.AppendLine($"IPv4 gelen paket hatası: {stats.IncomingPacketsWithErrors}");
            sb.AppendLine($"IPv4 gelen paket atımı: {stats.IncomingPacketsDiscarded}");
            sb.AppendLine($"IPv4 giden paket atımı: {stats.OutgoingPacketsDiscarded}");
            sb.AppendLine("Not: Bunlar Windows açılışından/arabirim sıfırlamasından beri birikmiş sayaçlardır; tek başına arıza kanıtı değildir.");
        }
        catch (Exception ex) { sb.AppendLine("Paket sayaçları okunamadı: " + ex.Message); }

        if (_lastPingResults.Count > 0)
        {
            sb.AppendLine("\nBAYAT | [SON PİNG TESTİ]");
            foreach (var pair in _lastPingResults)
            {
                var r = pair.Value;
                sb.AppendLine($"{pair.Key}: kayıp %{r.Loss:0.0}, ortalama {(r.AverageMs?.ToString("0.0") ?? "ölçülemedi")} ms, jitter {(r.JitterMs?.ToString("0.0") ?? "ölçülemedi")} ms");
            }
        }
        else
        {
            sb.AppendLine("\nBAYAT | [SON PİNG TESTİ]");
            sb.AppendLine("Henüz ping testi yapılmadı. Önerileri ölçümle kişiselleştirmek için önce Ping / Jitter / Paket Kaybı Testi düğmesini kullanın.");
        }

        sb.AppendLine("\nBAYAT | [OYUN PROFİLİ]");
        sb.AppendLine(CurrentGameProfile + ": " + GetGameProfileGuidance(CurrentGameProfile));
        sb.AppendLine("Oyun sunucusu testi için oyunun gösterdiği gerçek IP/alan adını üstteki alana girin.");
        if (!string.IsNullOrWhiteSpace(_lastGameDiscoveryReport))
        {
            sb.AppendLine("\nBAYAT | [SON OYUN SÜRECİ TARAMASI]");
            sb.AppendLine("Yerel süreç/bağlantı taraması daha önce çalıştırıldı. Ayrıntılı uç noktalar ana tanılama çıktısındaki son envanter bölümündedir.");
        }

        sb.AppendLine("\nBAYAT | [BU SİSTEM İÇİN ÖNERİLER]");
        int n = 1;
        if (activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            sb.AppendLine($"{n++}. ÖNCELİKLİ — Wi-Fi kullanıyorsunuz. Oyun testi için mümkünse Ethernet kablosuyla karşılaştırma yapın; Wi-Fi'da modem mesafesi, duvarlar ve kanal yoğunluğu gecikme/jitter oluşturabilir.");
            sb.AppendLine($"{n++}. ÖNCELİKLİ — Wi-Fi bağlantısında modem destekliyorsa 5 GHz veya 6 GHz bandını deneyin. Modem ve istemci aynı bandı desteklemelidir.");
        }
        else if (activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
        {
            sb.AppendLine($"{n++}. ÖNCELİKLİ — Ethernet bağlantısı algılandı. Link hızı 100 Mbps veya altındaysa kabloyu ve modem/router portunu kontrol edin; bu değer internet paket hızınız değil, yerel bağlantı anlaşma hızıdır.");
            if (activeAdapter.Speed > 0 && activeAdapter.Speed <= 100_000_000)
                sb.AppendLine($"{n++}. TESPİT — Link hızı {activeAdapter.Speed / 1_000_000d:0} Mbps. Gigabit destekli cihazlarda Cat5e veya daha iyi kablo ve gigabit portla yeniden deneyin.");
        }
        else
        {
            sb.AppendLine($"{n++}. KONTROL — Aktif bağlantı türü standart Ethernet/Wi-Fi değil. VPN veya sanal ağ adaptörü kullanıyorsanız gerçek fiziksel adaptörü ayrıca doğrulayın.");
        }

        if (_lastPingResults.TryGetValue("Yerel ağ geçidi", out var gateway) && gateway.Loss > 0)
            sb.AppendLine($"{n++}. ÖNCELİKLİ — Yerel ağ geçidine ping kaybı ölçüldü (%{gateway.Loss:0.0}). Önce modem, kablo/Wi-Fi sinyali ve yerel ağ kararlılığını inceleyin.");
        else if (_lastPingResults.TryGetValue("Yerel ağ geçidi", out gateway) && gateway.AverageMs > 10)
            sb.AppendLine($"{n++}. KONTROL — Yerel ağ geçidi ortalaması {gateway.AverageMs:0.0} ms. Kablolu bağlantıda bu değer beklenenden yüksek olabilir; Wi-Fi'da ortam koşullarına göre değişebilir.");

        if (_lastPingResults.TryGetValue("Cloudflare", out var cf) && _lastPingResults.TryGetValue("Google DNS", out var google))
        {
            if (cf.Loss > 0 || google.Loss > 0)
                sb.AppendLine($"{n++}. KONTROL — İnternet test hedeflerinden en az birinde ICMP yanıt kaybı var. Bazı ağlar ping paketlerini kısıtlayabilir; tek başına oyun paket kaybını kanıtlamaz. Oyunun kendi ağ istatistikleriyle doğrulayın.");
            else if (cf.AverageMs.HasValue && google.AverageMs.HasValue && Math.Abs(cf.AverageMs.Value - google.AverageMs.Value) > 25)
                sb.AppendLine($"{n++}. BİLGİ — İki genel test hedefinin gecikmeleri farklı. Bu, hedeflerin farklı ağ rotalarını kullanmasından kaynaklanabilir; DNS değiştirmek aktif oyun pingini genellikle düşürmez.");
        }

        try
        {
            var power = RunReadOnlyCommand("powercfg.exe", "/getactivescheme");
            if (!string.IsNullOrWhiteSpace(power))
            {
                sb.AppendLine("\nBAYAT | [ETKİN GÜÇ PLANI]");
                sb.AppendLine(power.Trim());
                sb.AppendLine("Not: Masaüstü/dizüstü ve güç kaynağına göre güç planı seçimi değişir. Bu araç güç planını otomatik değiştirmez.");
            }
        }
        catch { /* Power plan is optional diagnostic information. */ }

        sb.AppendLine("\nBAYAT | [OTOMATİK UYGULANMAYAN AYARLAR]");
        sb.AppendLine("• TCP autotuning, RSS, interrupt moderation, Energy Efficient Ethernet ve Registry gecikme tweak'leri sürücü/donanıma göre değişir. Genel geçer oldukları varsayılıp otomatik değiştirilmiyor.");
        sb.AppendLine("• DNS seçimi isim çözümleme süresini etkileyebilir; mevcut oyun bağlantısının rota gecikmesini normalde değiştirmez.");
        sb.AppendLine("• Hiçbir ayar sıfır paket kaybı veya belirli ping garantisi vermez.");
        sb.AppendLine("\nSonuç: Önce yerel ağ kararlılığı ve gerçek oyun sunucusu ölçümü doğrulanmalı; ardından yalnızca kanıta dayalı ayarlar seçilmelidir.");

        PerformanceBenchmarkSession? abSession = null;
        if (File.Exists(AbBenchmarkPath))
        {
            try
            {
                var json = File.ReadAllText(AbBenchmarkPath);
                abSession = JsonSerializer.Deserialize<PerformanceBenchmarkSession>(json)
                    ?? throw new InvalidDataException("A/B oturumu boş.");
                PerformanceBenchmark.ValidateSession(abSession);
                if (!abSession.AdapterId.Equals(activeAdapter.Id, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("\nA/B oturumu başka bir bağdaştırıcıya ait; karar planında güncel adaptör için kanıt sayılmadı.");
                    abSession = null;
                }
            }
            catch (Exception ex) { sb.AppendLine("\nA/B oturumu okunamadı: " + ex.Message); }
        }
        var pingEvidence = _lastPingResults.ToDictionary(
            pair => pair.Key,
            pair => new OptimizationDecisionEngine.PingEvidence(pair.Value.Success, pair.Value.Loss,
                pair.Value.AverageMs, pair.Value.JitterMs), StringComparer.OrdinalIgnoreCase);
        var nicMatches = activeAdapter is not null &&
            string.Equals(_lastNicAdapterId, activeAdapter.Id, StringComparison.OrdinalIgnoreCase);
        var decisionAdapter = activeAdapter ?? throw new InvalidOperationException("Aktif ağ bağdaştırıcısı beklenmedik şekilde null.");
        var decisionInput = new OptimizationDecisionEngine.Input(
            decisionAdapter.NetworkInterfaceType,
            decisionAdapter.OperationalStatus == OperationalStatus.Up,
            decisionAdapter.Speed,
            decisionAdapter.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)),
            pingEvidence,
            nicMatches,
            _lastNicIsPhysical,
            nicMatches ? _lastNicProperties : Array.Empty<NicOptimizationAdvisor.DriverProperty>(),
            abSession,
            Environment.ProcessorCount,
            File.Exists(NicBackupPath));
        var plan = OptimizationDecisionEngine.Build(decisionInput);
        sb.AppendLine("\n" + plan.ToReport());
        sb.AppendLine("\nBAYAT | [LOKAL GEÇMİŞ]");
        sb.AppendLine("Tamamlanmış A/B karşılaştırmaları, IP/hostname ve adaptör kimliği saklanmadan yerel performans geçmişine eklenir.");
        AppendLine("\n" + sb);
        StatusText.Text = $"Akıllı karar planı oluşturuldu. Kanıt kapsamı: %{plan.EvidenceCoverage}. Hiçbir kalıcı ayar değiştirilmedi.";
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Tanılama raporunu kaydet",
            FileName = $"SmartNetworkOptimizer-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "Metin dosyası (*.txt)|*.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog() != true) return;

        var content = OutputText.Text;
        try
        {
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            StatusText.Text = "Tanılama raporu kaydedildi.";
        }
        catch (Exception ex)
        {
            ShowBAYATMessageBox("Rapor kaydedilemedi: " + ex.Message, "Rapor dışa aktarma", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string RunReadOnlyCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Tanılama komutu başlatılamadı.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        return process.ExitCode == 0 ? stdout : stderr;
    }

    private async void DnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockPrivilegedNetworkChanges()) return;
        await ApplyDnsProfileAsync(ResolverProfile.Cloudflare);
    }

    private async void BenchmarkDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taraması yapın ve aktif internet bağlantısı olduğundan emin olun.", "DNS karşılaştırması");
            return;
        }

        SetBusy(true, "43 DNS adresi, tekrarlı gerçek DNS sorgularıyla ölçülüyor...");
        try
        {
            var results = await RunDnsResolverBenchmarkAsync();

            _resolverBenchmarks.Clear();
            _resolverBenchmarks.AddRange(results);

            var currentSession = new DnsResolverBenchmarkSession(
                DateTimeOffset.Now,
                results);

            var sessionsForReport = new List<DnsResolverBenchmarkSession>();
            var historyNotice = string.Empty;

            try
            {
                var fingerprint = BuildDnsNetworkFingerprint(_activeAdapter);
                var historyPath = DnsResolverHistoryStore.GetHistoryPath(
                    _appDataDir,
                    fingerprint);

                var storedHistory = DnsResolverHistoryStore.Load(
                    historyPath,
                    fingerprint);

                sessionsForReport.AddRange(storedHistory.Sessions);
                sessionsForReport.Add(currentSession);

                sessionsForReport = sessionsForReport
                    .TakeLast(DnsResolverRanking.MaximumStoredSessions)
                    .ToList();

                var updatedHistory = new DnsResolverHistoryDocument(
                    DnsResolverHistoryStore.CurrentSchemaVersion,
                    fingerprint,
                    sessionsForReport);

                try
                {
                    DnsResolverHistoryStore.Save(historyPath, updatedHistory);
                }
                catch (Exception historySaveException)
                {
                    historyNotice =
                        "Yeni oturum bellekte değerlendirildi ancak yerel geçmişe kaydedilemedi: " +
                        historySaveException.Message;
                }
            }
            catch (Exception historyLoadException)
            {
                // Bozuk veya başka ağa ait bir geçmişin üzerine yazılmaz.
                // Güvenilir sıralama için yalnızca bu oturum kullanılır.
                sessionsForReport.Clear();
                sessionsForReport.Add(currentSession);

                historyNotice =
                    "Önceki DNS geçmişi güvenilir biçimde okunamadı. Eski dosya korunarak " +
                    "bu oturum ayrı değerlendiriliyor: " +
                    historyLoadException.Message;
            }

            _dnsBenchmarkSessions.Clear();
            _dnsBenchmarkSessions.AddRange(sessionsForReport);

            var sb = new StringBuilder();
            sb.AppendLine("\nDNS ÇÖZÜMLEYİCİ KARŞILAŞTIRMASI");
            sb.AppendLine(new string('=', 68));
            sb.AppendLine("Her sunucu: 5 alan adı × 3 tur = 15 doğrudan DNS sorgusu.");
            sb.AppendLine("Ölçüm: UDP/53; kesilmiş yanıt alınırsa TCP/53 yedek sorgusu.");
            sb.AppendLine("Ham sunucu listesi medyana göre sıralanır. Güvenilir tekil DNS seçimi önce P95, sonra jitter ve ardından medyanı karşılaştırır. Profil seçimi iki sunucuyu birlikte değerlendirir.");
            sb.AppendLine();

            foreach (var result in results.OrderBy(r => r.MedianMs ?? double.MaxValue))
            {
                sb.AppendLine($"{result.Provider} — {result.Service}");
                sb.AppendLine($"  Sunucu: {result.Server}");
                sb.AppendLine($"  Başarılı: {result.SuccessCount}/{result.RequestedCount} | Zaman aşımı: {result.TimeoutCount} | Diğer hata: {result.FailureCount - result.TimeoutCount}");
                sb.AppendLine($"  Minimum: {(result.MinimumMs.HasValue ? result.MinimumMs.Value.ToString("0.00") + " ms" : "—")}");
                sb.AppendLine($"  Medyan: {(result.MedianMs.HasValue ? result.MedianMs.Value.ToString("0.00") + " ms" : "ölçülemedi")}");
                sb.AppendLine($"  Ortalama: {(result.AverageMs.HasValue ? result.AverageMs.Value.ToString("0.00") + " ms" : "—")} | P95: {(result.P95Ms.HasValue ? result.P95Ms.Value.ToString("0.00") + " ms" : "—")}");
                sb.AppendLine($"  Jitter tahmini: {(result.JitterMs.HasValue ? result.JitterMs.Value.ToString("0.00") + " ms" : "—")}");
                sb.AppendLine($"  Uygulama adayı: {(result.IsApplicationCandidate ? "Evet, profil çifti ayrıca değerlendirilecek." : "Hayır, yalnızca ölçüm.")}");
                if (result.FailureSummary != "Yok")
                    sb.AppendLine($"  Hata özeti: {result.FailureSummary}");
                if (!result.IsApplicationCandidate)
                    sb.AppendLine($"  Not: {result.ApplicationNotice}");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("ÇOKLU DNS OTURUM DEĞERLENDİRMESİ");
            sb.AppendLine(new string('-', 68));
            sb.AppendLine($"Bu ağ için saklanan oturum: {_dnsBenchmarkSessions.Count}/{DnsResolverRanking.MaximumStoredSessions}");
            sb.AppendLine($"Güvenilir profil kararı için gereken en az oturum: {DnsResolverRanking.MinimumTrustedSessionCount}");

            if (!string.IsNullOrWhiteSpace(historyNotice))
                sb.AppendLine("GEÇMİŞ BİLGİSİ: " + historyNotice);

            var aggregateResults = DnsResolverRanking.AggregateSessions(
                _dnsBenchmarkSessions);

            foreach (var aggregate in aggregateResults)
            {
                sb.AppendLine($"{aggregate.Provider} — {aggregate.Server}");
                sb.AppendLine($"  Oturum: {aggregate.SessionCount} | Oturum başarı ölçütü: {aggregate.PassingSessionCount}/{aggregate.SessionCount}");
                sb.AppendLine($"  Toplam başarı: {aggregate.SuccessCount}/{aggregate.RequestedCount} (%{aggregate.SuccessRate * 100d:0.0})");
                sb.AppendLine($"  Medyan: {(aggregate.MedianMs.HasValue ? aggregate.MedianMs.Value.ToString("0.00") + " ms" : "—")}");
                sb.AppendLine($"  Ortalama: {(aggregate.AverageMs.HasValue ? aggregate.AverageMs.Value.ToString("0.00") + " ms" : "—")} | P95: {(aggregate.P95Ms.HasValue ? aggregate.P95Ms.Value.ToString("0.00") + " ms" : "—")}");
                sb.AppendLine($"  Jitter: {(aggregate.JitterMs.HasValue ? aggregate.JitterMs.Value.ToString("0.00") + " ms" : "—")}");

                if (aggregate.FailureSummary != "Yok")
                    sb.AppendLine("  Hatalar: " + aggregate.FailureSummary);

                sb.AppendLine();
            }

            var fastestIndividual =
                DnsResolverRanking.SelectBestReliableIndividual(_dnsBenchmarkSessions);

            if (fastestIndividual is not null)
            {
                sb.AppendLine("EN İSTİKRARLI GÜVENİLİR TEKİL DNS — ÖLÇÜM SONUCU");
                sb.AppendLine($"{fastestIndividual.Provider} — {fastestIndividual.Server}");
                sb.AppendLine($"Medyan: {fastestIndividual.MedianMs:0.00} ms | P95: {fastestIndividual.P95Ms:0.00} ms | Jitter: {fastestIndividual.JitterMs:0.00} ms");
                sb.AppendLine($"Başarı: {fastestIndividual.SuccessCount}/{fastestIndividual.RequestedCount}");
                sb.AppendLine("Bu bölüm tek DNS adresini değerlendirir; seçimde önce P95, sonra jitter ve ardından medyan dikkate alınır. Tek başına iki sunuculu profil uygulama kararı değildir.");
            }
            else
            {
                sb.AppendLine("GÜVENİLİR TEKİL DNS HENÜZ BELİRLENMEDİ");
                sb.AppendLine("En az üç oturum ve yeterli başarı/tutarlılık koşulları bekleniyor.");
            }

            var fastestProfile = DnsResolverRanking.SelectBestProfile(
                _dnsBenchmarkSessions,
                GetDnsProfileDefinitions());

            sb.AppendLine();

            if (fastestProfile is not null)
            {
                sb.AppendLine("EN İYİ GÜVENİLİR İKİ SUNUCULU DNS PROFİLİ");
                sb.AppendLine($"{fastestProfile.Provider} — medyan ortalaması: {fastestProfile.MedianAverageMs:0.00} ms");
                sb.AppendLine($"P95 ortalaması: {fastestProfile.P95AverageMs:0.00} ms | Jitter ortalaması: {fastestProfile.JitterAverageMs:0.00} ms");
                sb.AppendLine($"En yüksek sunucu P95: {fastestProfile.WorstServerP95Ms:0.00} ms");
                sb.AppendLine($"En yüksek sunucu jitter: {fastestProfile.WorstServerJitterMs:0.00} ms");
                sb.AppendLine($"Başarı: {fastestProfile.SuccessCount}/{fastestProfile.RequestedCount}");
                sb.AppendLine($"Uygun oturum: {fastestProfile.PassingSessionCount}/{fastestProfile.SessionCount}");
                sb.AppendLine("Bu profil yalnızca ölçüm açısından uygun bulundu. DNS ayarları güvenlik nedeniyle değiştirilmedi.");
            }
            else
            {
                sb.AppendLine("GÜVENİLİR İKİ SUNUCULU PROFİL HENÜZ BELİRLENMEDİ");
                sb.AppendLine("En az üç oturum, her sunucuda toplam en az %90 başarı ve yeterli oturum tutarlılığı aranıyor. DNS ayarı değiştirilmemelidir.");
            }

            sb.AppendLine();
            sb.AppendLine("ÖNEMLİ:");
            sb.AppendLine("• DNS yanıt süresi oyun sunucusuna giden mevcut bağlantının ping değeri değildir.");
            sb.AppendLine("• Profil sıralaması önce iki sunucunun daha yüksek P95 değerini, sonra daha yüksek jitter değerini ve ardından medyan ortalamasını karşılaştırır.");
            sb.AppendLine("• Her iki sunucuda da en az %80 başarılı sorgu aranır.");
            sb.AppendLine("• DNS filtreleme politikaları farklı olabilir. En hızlı sunucunun içerik filtreleme davranışı da dikkate alınmalıdır.");
            sb.AppendLine("• Tek test oturumu kalıcı olarak en iyi sağlayıcıyı kanıtlamaz. Farklı saatlerde tekrar ölçüm önerilir.");

            AppendLine(sb.ToString());

            StatusText.Text = fastestProfile is null
                ? $"DNS ölçümü tamamlandı. Güvenilir profil için {_dnsBenchmarkSessions.Count}/{DnsResolverRanking.MinimumTrustedSessionCount} oturum mevcut; DNS değişikliği devre dışı."
                : $"DNS ölçümü tamamlandı. Profil adayı: {fastestProfile.Provider}; medyan {fastestProfile.MedianAverageMs:0.00} ms, en yüksek sunucu P95 {fastestProfile.WorstServerP95Ms:0.00} ms. DNS değişikliği devre dışı.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[DNS TESTİ HATASI] " + ex.Message);
            StatusText.Text = "DNS karşılaştırması başarısız.";
        }
        finally
        {
            SetBusy(false);
            UpdateRestoreButton();
        }
    }
    private async void ApplyBestDnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockPrivilegedNetworkChanges()) return;

        var fastest = DnsResolverRanking.SelectBestProfile(
            _dnsBenchmarkSessions,
            GetDnsProfileDefinitions());

        if (fastest is null)
        {
            ShowBAYATMessageBox(
                "En az üç güvenilir DNS ölçüm oturumu ve iki sunucuda yeterli başarı/tutarlılık sağlanmadan profil seçilmeyecek.",
                "DNS optimizasyonu");
            return;
        }

        var profile = ResolverProfile.All.SingleOrDefault(
            item => item.ProfileId == fastest.ProfileId);

        if (profile is null)
        {
            ShowBAYATMessageBox(
                "Ölçüm profili mevcut uygulama kataloğuyla eşleştirilemedi. DNS ayarı değiştirilmedi.",
                "DNS güvenlik kontrolü");
            return;
        }

        await ApplyDnsProfileAsync(profile);
    }

    private static DnsResolverProfileDefinition[] GetDnsProfileDefinitions()
    {
        return ResolverProfile.All
            .Select(profile => new DnsResolverProfileDefinition(
                profile.ProfileId,
                profile.Provider,
                profile.Ipv4Servers.ToArray()))
            .ToArray();
    }
    private async Task ApplyDnsProfileAsync(ResolverProfile profile)
    {
        if (BlockPrivilegedNetworkChanges()) return;
        if (!OperatingSystem.IsWindows()) return;
        if (_activeAdapter is null)
        {
            ShowBAYATMessageBox("Önce sistem taraması yapın.", "DNS ayarı");
            return;
        }
        if (File.Exists(BackupPath))
        {
            ShowBAYATMessageBox("Zaten bir DNS yedeği var. Yeni değişiklikten önce mevcut ayarı geri yükleyin; böylece ilk ayar yedeği korunur.", "DNS yedeği mevcut");
            return;
        }

        bool ipv6Supported = _activeAdapter.Supports(NetworkInterfaceComponent.IPv6);
        bool applyIpv6 = ipv6Supported && profile.Ipv6Servers.Length == 2;
        bool? ipv4StaticDnsValue = HasStaticDnsConfig("Tcpip", _activeAdapter.Id);
        bool? ipv6StaticDnsValue = ipv6Supported ? HasStaticDnsConfig("Tcpip6", _activeAdapter.Id) : false;
        if (ipv4StaticDnsValue is null || (applyIpv6 && ipv6StaticDnsValue is null))
        {
            ShowBAYATMessageBox("Windows kayıt defterinden DNS yapılandırma türü güvenilir biçimde okunamadı. Güvenlik için DNS değişikliği uygulanmayacak.", "Güvenlik nedeniyle işlem durduruldu", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ipProperties = _activeAdapter.GetIPProperties();
        var ipv4Servers = ipProperties.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
        var ipv6Servers = ipProperties.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.ToString()).ToList();
        int index;
        try { index = ipProperties.GetIPv4Properties().Index; }
        catch (Exception ex) { ShowBAYATMessageBox("Ağ arabirimi numarası okunamadı: " + ex.Message); return; }

        var confirm = ShowBAYATMessageBox(
            $"'{_activeAdapter.Name}' bağlantısında {profile.Provider} DNS kullanılacak.\n\nIPv4: {string.Join(", ", profile.Ipv4Servers)}\nIPv6: {(applyIpv6 ? string.Join(", ", profile.Ipv6Servers) : ipv6Supported ? "Mevcut IPv6 DNS ayarı korunacak." : "Bu bağdaştırıcıda IPv6 etkin değil.")}\n\nÖnceki DNS ayarları yedeklenecek. DNS değişikliği oyun pingini düşürmeyebilir. Kurumsal, VPN veya ebeveyn denetimli DNS kullanıyorsanız devam etmeyin.\n\nDevam edilsin mi?",
            "DNS değişikliğini onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true, "DNS ayarı değiştiriliyor. Windows yönetici izni isteyecek...");
        try
        {
            Directory.CreateDirectory(_appDataDir);
            var backup = new DnsBackup(2, index, _activeAdapter.Id, _activeAdapter.Name,
                ipv4Servers, ipv6Servers, !ipv4StaticDnsValue.GetValueOrDefault(), !ipv6StaticDnsValue.GetValueOrDefault(), ipv6Supported, DateTimeOffset.Now);
            await File.WriteAllTextAsync(BackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
            await RunElevatedPowerShellAsync(BuildSetDnsScript(_activeAdapter.Id, profile.Ipv4Servers, profile.Ipv6Servers, applyIpv6));
            AppendLine($"\n[DNS] {profile.Provider} DNS '{_activeAdapter.Name}' bağdaştırıcısına uygulandı. IPv4/IPv6 yedeği ayrı ayrı kaydedildi.");
            StatusText.Text = $"{profile.Provider} DNS uygulandı. Geri yükleme kullanılabilir.";
        }
        catch (OperationCanceledException)
        {
            // UAC cancellation occurs before the elevated script starts, so no setting was changed.
            try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
            StatusText.Text = "Yönetici izni verilmedi; ayar değiştirilmedi.";
        }
        catch (Exception ex)
        {
            AppendLine("\n[DNS HATASI] " + ex.Message);
            if (File.Exists(BackupPath))
            {
                try
                {
                    var saved = ParseDnsBackup(await File.ReadAllTextAsync(BackupPath));
                    if (saved is null) throw new InvalidDataException("İşlem yedeği okunamadı.");
                    StatusText.Text = "Kısmi DNS işlemi algılandı; önceki ayarlar geri alınıyor...";
                    await RunElevatedPowerShellAsync(BuildRestoreDnsScript(saved));
                    File.Delete(BackupPath);
                    AppendLine("[OTOMATİK GERİ ALMA] DNS ayarları önceki haline döndürüldü.");
                    StatusText.Text = "İşlem başarısız oldu; önceki DNS ayarları geri yüklendi.";
                }
                catch (Exception rollbackError)
                {
                    AppendLine("[GERİ ALMA UYARISI] Otomatik geri yükleme tamamlanamadı: " + rollbackError.Message + " Yedek dosyası korundu; geri yükleme düğmesini kullanın.");
                    StatusText.Text = "DNS işlemi başarısız. Geri yükleme yedeği korundu.";
                }
            }
            else StatusText.Text = "DNS ayarı uygulanamadı; değişiklik öncesi yedek oluşturulamadı.";
        }
        finally { SetBusy(false); UpdateRestoreButton(); }
    }

    private static string BuildDnsNetworkFingerprint(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();

        var gateway = GetDefaultGateway(adapter)?.ToString() ?? "no-default-gateway";
        var physicalAddress = Convert.ToHexString(
            adapter.GetPhysicalAddress().GetAddressBytes());

        var ipv4Networks = properties.UnicastAddresses
            .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(item =>
            {
                var addressBytes = item.Address.GetAddressBytes();
                var maskBytes = item.IPv4Mask.GetAddressBytes();

                if (addressBytes.Length != maskBytes.Length)
                    return string.Empty;

                for (var index = 0; index < addressBytes.Length; index++)
                    addressBytes[index] = (byte)(addressBytes[index] & maskBytes[index]);

                return $"{new IPAddress(addressBytes)}/{item.IPv4Mask}";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var dhcpServers = properties.DhcpServerAddresses
            .Select(address => address.ToString())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return DnsResolverHistoryStore.CreateNetworkFingerprint(
            adapter.Id,
            adapter.NetworkInterfaceType.ToString(),
            gateway,
            physicalAddress,
            string.Join(",", ipv4Networks),
            string.Join(",", dhcpServers),
            properties.DnsSuffix ?? string.Empty);
    }

    private static async Task<List<DnsResolverBenchmarkResult>> RunDnsResolverBenchmarkAsync()
    {
        var results = await DnsResolverBenchmark.RunAsync();
        return results.ToList();
    }
    private static async Task<string> RunPowerShellCaptureAsync(string script, TimeSpan timeout)
    {
        var path = Path.Combine(Path.GetTempPath(), "SNO-READ-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(path, script, new UTF8Encoding(true));
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + QuoteArgument(path)
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell başlatılamadı.");
            using var cts = new CancellationTokenSource(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("PowerShell tanılama komutu zaman aşımına uğradı."); }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0) throw new InvalidOperationException("PowerShell tanılama komutu başarısız: " + stderr);
            return stdout;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockPrivilegedNetworkChanges()) return;
        if (!File.Exists(BackupPath))
        {
            ShowBAYATMessageBox("Geri yüklenecek DNS yedeği bulunamadı.", "DNS geri yükleme");
            return;
        }
        try
        {
            var json = await File.ReadAllTextAsync(BackupPath);
            var backup = ParseDnsBackup(json);
            if (backup is null || backup.Ipv4Servers is null || backup.Ipv6Servers is null)
                throw new InvalidDataException("DNS yedeği geçersiz veya desteklenmeyen sürümde.");
            var confirm = ShowBAYATMessageBox($"'{backup.InterfaceName}' bağdaştırıcısının IPv4 ve IPv6 DNS ayarları yedekteki haline döndürülecek.\n\nIPv4: {(backup.Ipv4Automatic ? "Otomatik" : string.Join(", ", backup.Ipv4Servers))}\nIPv6: {(!backup.Ipv6StackSupported ? "Etkin değil; dokunulmadı" : backup.Ipv6Automatic ? "Otomatik" : string.Join(", ", backup.Ipv6Servers))}\n\nDevam edilsin mi?", "DNS geri yükleme", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            SetBusy(true, "Önceki IPv4/IPv6 DNS ayarları geri yükleniyor...");
            await RunElevatedPowerShellAsync(BuildRestoreDnsScript(backup));
            File.Delete(BackupPath);
            AppendLine($"\n[GERİ YÜKLEME] '{backup.InterfaceName}' bağdaştırıcısının IPv4 ve IPv6 DNS ayarları ayrı ayrı geri yüklendi.");
            StatusText.Text = "Önceki DNS ayarları geri yüklendi.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Yönetici izni verilmedi; geri yükleme yapılmadı."; }
        catch (Exception ex) { AppendLine("\n[GERİ YÜKLEME HATASI] " + ex.Message); StatusText.Text = "DNS geri yüklenemedi; yedek korundu."; }
        finally { SetBusy(false); UpdateRestoreButton(); }
    }

    private async void FlushButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockPrivilegedNetworkChanges()) return;
        var confirm = ShowBAYATMessageBox("Windows DNS önbelleği temizlenecek. Bu işlem kayıtlı DNS sunucularını değiştirmez. Devam edilsin mi?", "DNS önbelleği", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        SetBusy(true, "DNS önbelleği temizleniyor...");
        try
        {
            await RunElevatedPowerShellAsync("Clear-DnsClientCache\nif ($?) { exit 0 } else { exit 1 }");
            AppendLine("\n[DNS ÖNBELLEĞİ] Windows DNS önbelleği temizlendi.");
            StatusText.Text = "DNS önbelleği temizlendi.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Yönetici izni verilmedi."; }
        catch (Exception ex) { AppendLine("\n[ÖNBELLEK HATASI] " + ex.Message); StatusText.Text = "DNS önbelleği temizlenemedi."; }
        finally { SetBusy(false); }
    }

    private static DnsBackup? ParseDnsBackup(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("SchemaVersion", out _))
            return JsonSerializer.Deserialize<DnsBackup>(json);

        // Backward compatibility for 0.1/0.2 backups. Those versions refused mixed IPv4/IPv6 modes,
        // so their single automatic/static flag can safely be applied to both address families.
        if (!root.TryGetProperty("InterfaceIndex", out var indexElement) ||
            !root.TryGetProperty("InterfaceName", out var nameElement) ||
            !root.TryGetProperty("RestoreAutomatically", out var autoElement) ||
            !root.TryGetProperty("Servers", out var serversElement))
            throw new InvalidDataException("Eski DNS yedeğinin gerekli alanları eksik.");

        var v4 = new List<string>();
        var v6 = new List<string>();
        foreach (var item in serversElement.EnumerateArray())
        {
            var value = item.GetString();
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidDataException("DNS yedeğinde geçersiz IP adresi bulundu.");
            (address.AddressFamily == AddressFamily.InterNetwork ? v4 : v6).Add(address.ToString());
        }
        var savedAt = root.TryGetProperty("SavedAt", out var dateElement) && dateElement.TryGetDateTimeOffset(out var date)
            ? date : DateTimeOffset.MinValue;
        return new DnsBackup(1, indexElement.GetInt32(), string.Empty,
            nameElement.GetString() ?? "Bilinmeyen bağdaştırıcı", v4, v6,
            autoElement.GetBoolean(), autoElement.GetBoolean(), true, savedAt);
    }

    private static string BuildSetDnsScript(string interfaceId, IEnumerable<string> ipv4Servers, IEnumerable<string> ipv6Servers, bool applyIpv6)
    {
        if (!Guid.TryParse(interfaceId, out var guid)) throw new InvalidDataException("Ağ bağdaştırıcısı kimliği geçersiz.");
        var v4 = ipv4Servers.Select(IPAddress.Parse).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
        var v6 = ipv6Servers.Select(IPAddress.Parse).Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.ToString()).ToArray();
        if (v4.Length == 0 || (applyIpv6 && v6.Length == 0)) throw new InvalidOperationException("Uygulanacak IP ailesi için DNS sunucu listesi boş.");
        var id = guid.ToString("D");
        var v4Literal = "@(" + string.Join(",", v4.Select(PsQuote)) + ")";
        var v6Literal = "@(" + string.Join(",", v6.Select(PsQuote)) + ")";
        return "$ErrorActionPreference = 'Stop'\n" +
               "$adapter = Get-NetAdapter -IncludeHidden | Where-Object { $_.InterfaceGuid.ToString() -eq " + PsQuote(id) + " } | Select-Object -First 1\n" +
               "if ($null -eq $adapter) { throw 'Ağ bağdaştırıcısı artık bulunamıyor.' }\n" +
               "$ifIndex = [int]$adapter.ifIndex\n" +
               "$v4 = " + v4Literal + "\n" +
               "$v6 = " + v6Literal + "\n" +
               "& netsh.exe interface ipv4 set dnsservers name=$($ifIndex) source=static address=$($v4[0]) validate=no\n" +
               "if ($LASTEXITCODE -ne 0) { throw 'IPv4 DNS ayarı başarısız.' }\n" +
               "for ($i = 1; $i -lt $v4.Count; $i++) { & netsh.exe interface ipv4 add dnsservers name=$($ifIndex) address=$($v4[$i]) index=$($i + 1) validate=no; if ($LASTEXITCODE -ne 0) { throw 'IPv4 ikincil DNS eklenemedi.' } }\n" +
               (applyIpv6 ? "& netsh.exe interface ipv6 set dnsservers name=$($ifIndex) source=static address=$($v6[0]) validate=no\n" +
                   "if ($LASTEXITCODE -ne 0) { throw 'IPv6 DNS ayarı başarısız.' }\n" +
                   "for ($i = 1; $i -lt $v6.Count; $i++) { & netsh.exe interface ipv6 add dnsservers name=$($ifIndex) address=$($v6[$i]) index=$($i + 1) validate=no; if ($LASTEXITCODE -ne 0) { throw 'IPv6 ikincil DNS eklenemedi.' } }\n" : string.Empty) +
               "Clear-DnsClientCache\nexit 0\n";
    }

    private static string BuildRestoreDnsScript(DnsBackup backup)
    {
        var hasGuid = Guid.TryParse(backup.InterfaceId, out var guid);
        var ipv4 = backup.Ipv4Servers.Select(IPAddress.Parse).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
        var ipv6 = backup.Ipv6Servers.Select(IPAddress.Parse).Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.ToString()).ToArray();
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        if (hasGuid)
            script.AppendLine("$adapter = Get-NetAdapter -IncludeHidden | Where-Object { $_.InterfaceGuid.ToString() -eq " + PsQuote(guid.ToString("D")) + " } | Select-Object -First 1");
        else
        {
            script.AppendLine("$adapter = Get-NetAdapter -IncludeHidden | Where-Object { $_.Name -eq " + PsQuote(backup.InterfaceName) + " } | Select-Object -First 1");
            script.AppendLine("if ($null -eq $adapter) { $adapter = Get-NetAdapter -IncludeHidden | Where-Object { $_.ifIndex -eq " + backup.InterfaceIndex.ToString(CultureInfo.InvariantCulture) + " } | Select-Object -First 1 }");
        }
        script.AppendLine("if ($null -eq $adapter) { throw 'Yedek alınan ağ bağdaştırıcısı bulunamadı; yedek dosyası korundu.' }");
        script.AppendLine("$ifIndex = [int]$adapter.ifIndex");
        AppendNetshDnsRestore(script, "ipv4", ipv4, backup.Ipv4Automatic, "IPv4");
        if (backup.Ipv6StackSupported)
            AppendNetshDnsRestore(script, "ipv6", ipv6, backup.Ipv6Automatic, "IPv6");
        script.AppendLine("Clear-DnsClientCache");
        script.AppendLine("exit 0");
        return script.ToString();
    }

    private static void AppendNetshDnsRestore(StringBuilder script, string family, string[] addresses, bool automatic, string label)
    {
        var source = automatic ? "dhcp" : "static";
        if (automatic || addresses.Length == 0)
        {
            script.AppendLine("& netsh.exe interface " + family + " set dnsservers name=$($ifIndex) source=" + source + (automatic ? "" : " address=none") + " validate=no");
            script.AppendLine("if ($LASTEXITCODE -ne 0) { throw '" + label + " DNS geri yüklenemedi.' }");
            return;
        }
        script.AppendLine("$" + family + "Dns = @(" + string.Join(",", addresses.Select(PsQuote)) + ")");
        script.AppendLine("& netsh.exe interface " + family + " set dnsservers name=$($ifIndex) source=static address=$($" + family + "Dns[0]) validate=no");
        script.AppendLine("if ($LASTEXITCODE -ne 0) { throw '" + label + " birincil DNS geri yüklenemedi.' }");
        script.AppendLine("for ($i = 1; $i -lt $" + family + "Dns.Count; $i++) { & netsh.exe interface " + family + " add dnsservers name=$($ifIndex) address=$($" + family + "Dns[$i]) index=$($i + 1) validate=no; if ($LASTEXITCODE -ne 0) { throw '" + label + " ikincil DNS geri yüklenemedi.' } }");
    }

    private static bool? HasStaticDnsConfig(string protocolKey, string adapterId)
    {
        try
        {
            var path = $@"SYSTEM\CurrentControlSet\Services\{protocolKey}\Parameters\Interfaces\{adapterId}";
            using var key = Registry.LocalMachine.OpenSubKey(path, writable: false);
            if (key is null) return null;
            var configured = Convert.ToString(key.GetValue("NameServer"), CultureInfo.InvariantCulture);
            return !string.IsNullOrWhiteSpace(configured);
        }
        catch { return null; }
    }

    private static string PsQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static Task RunElevatedPowerShellAsync(string script)
    {
        return Task.FromException(new NotSupportedException("Yönetici yetkisiyle sistem değişikliği bu sürümde güvenlik nedeniyle devre dışıdır."));
    }
    private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void QueryWmi(StringBuilder sb, string query, Func<ManagementObject, string> formatter)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject item in searcher.Get())
                using (item) sb.AppendLine("• " + formatter(item));
        }
        catch (Exception ex) { sb.AppendLine("Bilgi okunamadı: " + ex.Message); }
    }

    private static string Val(ManagementObject item, string property) => Convert.ToString(item[property], CultureInfo.InvariantCulture) ?? "Bilinmiyor";
    private static ulong ParseUlong(object? value) { try { return Convert.ToUInt64(value ?? 0, CultureInfo.InvariantCulture); } catch { return 0; } }
    private static string FormatBytes(ulong bytes) => bytes == 0 ? "Boyut bilinmiyor" : $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
    private static string FormatLinkSpeed(long bitsPerSecond) => bitsPerSecond <= 0 ? "Hız bilinmiyor" : $"{bitsPerSecond / 1_000_000d:0} Mbps link";

    private void AppendLine(string text) => OutputText.AppendText(text + Environment.NewLine);
    private bool BlockPrivilegedNetworkChanges()
    {
        const string message = "Bu sürümde DNS, RSS ve ağ ayarı değiştirme işlemleri güvenlik nedeniyle devre dışıdır. Hiçbir sistem ayarı değiştirilmedi.";
        StatusText.Text = message;
        AppendLine("\n[GÜVENLİK] " + message);
        return true;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        ScanButton.IsEnabled = !busy;
        PingButton.IsEnabled = !busy;
        FlushButton.IsEnabled = false;
        RecommendationsButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        DnsButton.IsEnabled = false;
        RestoreButton.IsEnabled = false;
        BenchmarkDnsButton.IsEnabled = !busy;
        GameTestButton.IsEnabled = !busy;
        NicPropertiesButton.IsEnabled = !busy;
        DiscoverGameConnectionsButton.IsEnabled = !busy;
        ApplyNicSettingsButton.IsEnabled = false;
        RestoreNicSettingsButton.IsEnabled = false;
        CaptureBaselineButton.IsEnabled = !busy;
        CaptureAfterButton.IsEnabled = !busy && File.Exists(AbBenchmarkPath);
        CompareAbButton.IsEnabled = !busy && File.Exists(AbBenchmarkPath);
        ResetAbButton.IsEnabled = !busy && File.Exists(AbBenchmarkPath);
        ViewHistoryButton.IsEnabled = !busy;
        ClearHistoryButton.IsEnabled = !busy && File.Exists(PerformanceHistoryPath);
        GameProfileCombo.IsEnabled = !busy;
        GameHostText.IsEnabled = !busy;
        GamePortText.IsEnabled = !busy;
        LiveClearButton.IsEnabled = !busy && _liveSamples.Count > 0;
        LiveExportButton.IsEnabled = !busy && _liveSamples.Count > 0;
        ApplyBestDnsButton.IsEnabled = false;
        if (status is not null) StatusText.Text = status;
    }
    private void UpdateRestoreButton() => RestoreButton.IsEnabled = false;
    private void UpdateNicRestoreButton() => RestoreNicSettingsButton.IsEnabled = false;

    private sealed record NicSnapshot(string InterfaceGuid, string InterfaceName, string InterfaceDescription,
        bool IsHardwareInterface, IReadOnlyList<NicOptimizationAdvisor.DriverProperty> Properties);
    private sealed record SystemReport(string Text, NetworkInterface? ActiveAdapter);
    private sealed record PingResult(int Success, double Loss, double? AverageMs, double? JitterMs);
    private sealed record LivePingSample(DateTimeOffset Timestamp, long? RoundtripMs, string Status);
    private sealed record DnsBackup(int SchemaVersion, int InterfaceIndex, string InterfaceId, string InterfaceName,
        List<string> Ipv4Servers, List<string> Ipv6Servers, bool Ipv4Automatic, bool Ipv6Automatic,
        bool Ipv6StackSupported, DateTimeOffset SavedAt);

    private sealed record ResolverProfile(
        string ProfileId,
        string Provider,
        string[] Ipv4Servers,
        string[] Ipv6Servers)
    {
        public static readonly ResolverProfile[] All = BuildAll();

        public static readonly ResolverProfile Cloudflare =
            All.Single(profile => profile.ProfileId == "cloudflare-standard");

        private static ResolverProfile[] BuildAll()
        {
            var profiles = new List<ResolverProfile>();

            foreach (var group in DnsResolverCatalog.All
                .Where(entry => entry.IsApplicationCandidate)
                .GroupBy(entry => entry.ProfileId, StringComparer.Ordinal))
            {
                var entries = group.ToArray();

                if (entries.Length != 2 ||
                    entries.Select(entry => entry.Provider)
                        .Distinct(StringComparer.Ordinal).Count() != 1)
                {
                    continue;
                }

                profiles.Add(new ResolverProfile(
                    group.Key,
                    entries[0].Provider,
                    entries.Select(entry => entry.Server).ToArray(),
                    GetKnownIpv6Servers(group.Key)));
            }

            return profiles.ToArray();
        }

        private static string[] GetKnownIpv6Servers(string profileId)
        {
            return profileId switch
            {
                "cloudflare-standard" =>
                    new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" },

                "google-public" =>
                    new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" },

                "quad9-secure" =>
                    new[] { "2620:fe::fe", "2620:fe::9" },

                _ => Array.Empty<string>()
            };
        }
    }
}