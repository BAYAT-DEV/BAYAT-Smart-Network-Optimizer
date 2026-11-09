// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.IO;
using System.Text;
using System.Text.Json;

namespace SmartNetworkOptimizer;

/// <summary>Privacy-preserving local history: no hostname, IP address, adapter GUID, SSID, or raw probes.</summary>
internal sealed record PerformanceHistoryEntry(
    string SessionId,
    DateTimeOffset RecordedAt,
    string GameProfile,
    string AdapterType,
    int LinkSpeedMbps,
    double? BeforeMedianMs,
    double? AfterMedianMs,
    double BeforeLossPercent,
    double AfterLossPercent,
    double? BeforeJitterMs,
    double? AfterJitterMs,
    string Outcome);

internal sealed record PerformanceHistoryDocument(int SchemaVersion, List<PerformanceHistoryEntry> Entries);

internal static class LocalPerformanceHistory
{
    public const int SchemaVersion = 1;
    public const int MaxEntries = 100;

    public static PerformanceHistoryEntry FromSession(PerformanceBenchmarkSession session)
    {
        PerformanceBenchmark.ValidateSession(session);
        if (session.After is null)
            throw new InvalidOperationException("A/B geçmişine yalnızca tamamlanmış ölçümler eklenebilir.");

        var before = session.Before!;
        var after = session.After;
        var minimumResponses = Math.Ceiling(Math.Min(before.RequestedSamples, after.RequestedSamples) * 0.75);
        string outcome;
        if (before.SuccessCount < minimumResponses || after.SuccessCount < minimumResponses ||
            !before.MedianMs.HasValue || !after.MedianMs.HasValue)
            outcome = "Yetersiz ölçüm";
        else
        {
            var delta = after.MedianMs.Value - before.MedianMs.Value;
            var threshold = Math.Max(2d, before.MedianMs.Value * 0.08d);
            var lossDelta = after.LossPercent - before.LossPercent;
            outcome = delta >= threshold || lossDelta >= 5d ? "Kötüleşme gözlendi" :
                delta <= -threshold && lossDelta <= 1d ? "Olumlu değişim gözlendi" : "Belirgin fark yok";
        }

        var mbps = session.LinkSpeedBitsPerSecond <= 0
            ? 0
            : (int)Math.Clamp(Math.Round(session.LinkSpeedBitsPerSecond / 1_000_000d), 0, int.MaxValue);

        // Intentionally exclude requested host, resolved IP, adapter GUID/name and raw packet samples.
        return new PerformanceHistoryEntry(
            session.SessionId,
            DateTimeOffset.Now,
            SafeLabel(session.GameProfile, 40),
            SafeLabel(session.AdapterType, 32),
            mbps,
            before.MedianMs,
            after.MedianMs,
            before.LossPercent,
            after.LossPercent,
            before.JitterMs,
            after.JitterMs,
            outcome);
    }

    public static async Task<bool> AddIfMissingAsync(string path, PerformanceHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Geçmiş dosyası klasörü belirlenemedi."));
        var document = await LoadAsync(path, cancellationToken);
        if (document.Entries.Any(e => e.SessionId.Equals(entry.SessionId, StringComparison.OrdinalIgnoreCase)))
            return false;

        document.Entries.Add(entry);
        var retainedEntries = document.Entries
            .OrderBy(e => e.RecordedAt)
            .TakeLast(MaxEntries)
            .ToList();
        document.Entries.Clear();
        document.Entries.AddRange(retainedEntries);
        await SaveAtomicAsync(path, document, cancellationToken);
        return true;
    }

    public static async Task<PerformanceHistoryDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new PerformanceHistoryDocument(SchemaVersion, new List<PerformanceHistoryEntry>());
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<PerformanceHistoryDocument>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Performans geçmişi dosyası okunamadı.");
        Validate(document);
        return document;
    }

    public static async Task ClearAsync(string path, CancellationToken cancellationToken = default)
    {
        if (File.Exists(path)) await Task.Run(() => File.Delete(path), cancellationToken);
    }

    public static void Validate(PerformanceHistoryDocument document)
    {
        if (document.SchemaVersion != SchemaVersion || document.Entries is null || document.Entries.Count > MaxEntries)
            throw new InvalidDataException("Performans geçmişinin sürümü veya kayıt sayısı geçersiz.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.Entries)
        {
            if (!Guid.TryParse(item.SessionId, out _) || !ids.Add(item.SessionId) ||
                string.IsNullOrWhiteSpace(item.GameProfile) || item.GameProfile.Length > 40 ||
                string.IsNullOrWhiteSpace(item.AdapterType) || item.AdapterType.Length > 32 ||
                item.LinkSpeedMbps < 0 || item.BeforeLossPercent is < 0 or > 100 ||
                item.AfterLossPercent is < 0 or > 100 ||
                !FiniteOrNull(item.BeforeMedianMs) || !FiniteOrNull(item.AfterMedianMs) ||
                !FiniteOrNull(item.BeforeJitterMs) || !FiniteOrNull(item.AfterJitterMs) ||
                string.IsNullOrWhiteSpace(item.Outcome) || item.Outcome.Length > 64)
                throw new InvalidDataException("Performans geçmişinde geçersiz veya yinelenen kayıt var.");
        }
    }

    public static string BuildReport(PerformanceHistoryDocument document)
    {
        Validate(document);
        var sb = new StringBuilder();
        sb.AppendLine("YEREL A/B PERFORMANS GEÇMİŞİ");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"Kayıt: {document.Entries.Count}/{MaxEntries}");
        sb.AppendLine("Gizlilik: Bu özet hedef IP/alan adı, adaptör GUID/adı, SSID veya ham ping örneği içermez.");
        sb.AppendLine("Dosya yalnızca bu bilgisayarda %LOCALAPPDATA%\\SmartNetworkOptimizer altında saklanır.");
        sb.AppendLine();

        if (document.Entries.Count == 0)
        {
            sb.AppendLine("Henüz tamamlanmış A/B karşılaştırması kaydedilmedi.");
            return sb.ToString();
        }

        foreach (var item in document.Entries.OrderByDescending(e => e.RecordedAt))
        {
            sb.AppendLine($"{item.RecordedAt:dd.MM.yyyy HH:mm} | {item.GameProfile} | {item.AdapterType} | {(item.LinkSpeedMbps > 0 ? item.LinkSpeedMbps + " Mbps" : "Link hızı bilinmiyor")}");
            sb.AppendLine($"  Medyan RTT: {Format(item.BeforeMedianMs)} → {Format(item.AfterMedianMs)} ms | Kayıp: %{item.BeforeLossPercent:0.0} → %{item.AfterLossPercent:0.0}");
            sb.AppendLine($"  Jitter tahmini: {Format(item.BeforeJitterMs)} → {Format(item.AfterJitterMs)} ms | Sonuç: {item.Outcome}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static async Task SaveAtomicAsync(string path, PerformanceHistoryDocument document, CancellationToken cancellationToken)
    {
        Validate(document);
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, document, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    private static bool FiniteOrNull(double? value) => !value.HasValue || (double.IsFinite(value.Value) && value.Value >= 0);
    private static string Format(double? value) => value.HasValue ? value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "—";
    private static string SafeLabel(string value, int maximum) => new string(value.Where(c => !char.IsControl(c)).Take(maximum).ToArray()).Trim();
}
