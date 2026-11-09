// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SmartNetworkOptimizer;

/// <summary>Persistent A/B measurement session. The resolved IP is kept fixed so DNS rotation cannot change the target between A and B.</summary>
internal sealed record PerformanceBenchmarkSession(
    int SchemaVersion,
    string SessionId,
    string RequestedHost,
    string ResolvedAddress,
    string GameProfile,
    string AdapterId,
    string AdapterName,
    string AdapterDescription,
    string AdapterType,
    long LinkSpeedBitsPerSecond,
    int LogicalProcessorCount,
    DateTimeOffset CreatedAt,
    PerformanceBenchmarkRun? Before,
    PerformanceBenchmarkRun? After);

internal sealed record PerformanceBenchmarkRun(
    int RequestedSamples,
    int SuccessCount,
    double LossPercent,
    long? MinimumMs,
    double? AverageMs,
    double? MedianMs,
    double? P95Ms,
    double? JitterMs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    List<PerformancePingSample> Samples);

internal sealed record PerformancePingSample(int Sequence, bool Success, long? RoundtripMs, string Status);

internal static class PerformanceBenchmark
{
    public const int SchemaVersion = 1;
    public const int DefaultSampleCount = 20;
    private const int TimeoutMilliseconds = 800;
    private const int IntervalMilliseconds = 150;

    public static async Task<IPAddress> ResolveTargetAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(char.IsWhiteSpace) || host.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("IP adresi veya alan adı girin; URL ya da boşluk kabul edilmez.", nameof(host));

        if (IPAddress.TryParse(host, out var literal)) return literal;

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Alan adı 8 saniye içinde çözümlenemedi.");
        }

        // Prefer IPv4 for consistency with the existing route diagnostics, but preserve IPv6-only destinations.
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
            ?? throw new InvalidOperationException("Alan adı için kullanılabilir IPv4/IPv6 adresi bulunamadı.");
    }

    public static async Task<PerformanceBenchmarkRun> CaptureAsync(
        IPAddress target,
        int sampleCount = DefaultSampleCount,
        Action<int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sampleCount is < 10 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Örnek sayısı 10 ile 100 arasında olmalıdır.");

        var started = DateTimeOffset.Now;
        var samples = new List<PerformancePingSample>(sampleCount);
        using var ping = new Ping();

        for (var i = 1; i <= sampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(target, TimeoutMilliseconds)
                    .WaitAsync(TimeSpan.FromMilliseconds(TimeoutMilliseconds + 500), cancellationToken);
                var success = reply.Status == IPStatus.Success;
                samples.Add(new PerformancePingSample(i, success,
                    success ? reply.RoundtripTime : null, reply.Status.ToString()));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                samples.Add(new PerformancePingSample(i, false, null, "Timeout"));
            }
            catch (PingException ex)
            {
                samples.Add(new PerformancePingSample(i, false, null, ex.InnerException?.GetType().Name ?? "PingException"));
            }
            catch (SocketException ex)
            {
                samples.Add(new PerformancePingSample(i, false, null, ex.SocketErrorCode.ToString()));
            }
            catch (InvalidOperationException)
            {
                samples.Add(new PerformancePingSample(i, false, null, "InvalidOperation"));
            }

            progress?.Invoke(i, sampleCount);
            if (i < sampleCount) await Task.Delay(IntervalMilliseconds, cancellationToken);
        }

        var values = samples.Where(s => s.Success && s.RoundtripMs.HasValue)
            .Select(s => s.RoundtripMs!.Value).ToArray();
        var ordered = values.OrderBy(v => v).ToArray();
        var adjacentSuccessful = samples.Where(s => s.Success && s.RoundtripMs.HasValue).ToArray();
        var jitterPairs = adjacentSuccessful.Zip(adjacentSuccessful.Skip(1))
            .Where(pair => pair.Second.Sequence == pair.First.Sequence + 1)
            .Select(pair => Math.Abs(pair.Second.RoundtripMs!.Value - pair.First.RoundtripMs!.Value))
            .ToArray();
        var jitter = jitterPairs.Length == 0 ? (double?)null : jitterPairs.Average();

        return new PerformanceBenchmarkRun(
            sampleCount,
            values.Length,
            (sampleCount - values.Length) * 100d / sampleCount,
            ordered.Length == 0 ? null : ordered[0],
            values.Length == 0 ? null : values.Average(),
            Percentile(ordered, 0.50),
            Percentile(ordered, 0.95),
            jitter,
            started,
            DateTimeOffset.Now,
            samples);
    }

    public static void ValidateSession(PerformanceBenchmarkSession session)
    {
        if (session.SchemaVersion != SchemaVersion || !Guid.TryParse(session.SessionId, out _))
            throw new InvalidDataException("A/B oturum dosyasının sürümü veya kimliği geçersiz.");
        if (string.IsNullOrWhiteSpace(session.RequestedHost) ||
            !IPAddress.TryParse(session.ResolvedAddress, out _) ||
            string.IsNullOrWhiteSpace(session.AdapterId) ||
            string.IsNullOrWhiteSpace(session.AdapterName) ||
            session.Before is null)
            throw new InvalidDataException("A/B oturumunda hedef, bağdaştırıcı veya A ölçümü eksik.");
        ValidateRun(session.Before!);
        if (session.After is not null) ValidateRun(session.After);
    }

    public static string BuildComparisonReport(PerformanceBenchmarkSession session)
    {
        ValidateSession(session);
        if (session.After is null)
            throw new InvalidOperationException("B ölçümü henüz alınmadı. Değişiklikten sonra 'A/B Aşama B' ölçümünü alın.");

        var a = session.Before!;
        var b = session.After!;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("A/B PERFORMANS KARŞILAŞTIRMASI");
        sb.AppendLine(new string('=', 58));
        sb.AppendLine($"Oturum: {session.SessionId}");
        sb.AppendLine($"Hedef: {session.RequestedHost} → {session.ResolvedAddress} (A ve B'de aynı IP)");
        sb.AppendLine($"Bağdaştırıcı: {session.AdapterName} | {session.AdapterDescription}");
        sb.AppendLine($"Bağlantı: {session.AdapterType} | Link: {(session.LinkSpeedBitsPerSecond > 0 ? $"{session.LinkSpeedBitsPerSecond / 1_000_000d:0} Mbps" : "Bilinmiyor")}");
        sb.AppendLine($"Oyun profili: {session.GameProfile}");
        sb.AppendLine($"A ölçümü: {a.StartedAt:dd.MM.yyyy HH:mm:ss} | B ölçümü: {b.StartedAt:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("Metrik                 A / Önce       B / Sonra       Değişim");
        sb.AppendLine("--------------------------------------------------------------");
        AppendMetric(sb, "Minimum RTT", a.MinimumMs, b.MinimumMs, "ms");
        AppendMetric(sb, "Medyan RTT", a.MedianMs, b.MedianMs, "ms");
        AppendMetric(sb, "Ortalama RTT", a.AverageMs, b.AverageMs, "ms");
        AppendMetric(sb, "P95 RTT", a.P95Ms, b.P95Ms, "ms");
        AppendMetric(sb, "Jitter tahmini", a.JitterMs, b.JitterMs, "ms");
        sb.AppendLine($"Paket kaybı            %{a.LossPercent:0.0}         %{b.LossPercent:0.0}         {b.LossPercent - a.LossPercent:+0.0;-0.0;0.0} yüzde puan");
        sb.AppendLine($"Başarılı yanıt         {a.SuccessCount}/{a.RequestedSamples}         {b.SuccessCount}/{b.RequestedSamples}");
        sb.AppendLine();

        var assessment = Assess(a, b);
        sb.AppendLine("DEĞERLENDİRME: " + assessment.Label);
        sb.AppendLine(assessment.Reason);
        sb.AppendLine();
        sb.AppendLine("Yorumlama sınırları:");
        sb.AppendLine("• ICMP RTT/kayıp/jitter ölçümü, oyunun UDP paket metriğiyle aynı değildir.");
        sb.AppendLine("• Aynı bağdaştırıcı kimliği, VPN/çoklu rota durumlarında aynı fiziksel çıkış yolunu garanti etmez; Windows yönlendirme tablosu hedef için başka arayüz seçebilir.");
        sb.AppendLine("• Tek bir A/B çifti nedenselliği kanıtlamaz. Aynı hedefte, benzer saat ve ağ yükünde en az 3 ayrı A/B çifti tekrarlayın.");
        sb.AppendLine("• Arka plandaki indirme/yüklemeler, Wi-Fi paraziti, ISS rotası ve hedef sunucu yükü sonucu etkileyebilir.");
        sb.AppendLine("• Bu rapor ayarı otomatik geri almaz; kötüleşme varsa NIC geri yükleme düğmesini kullanın.");
        return sb.ToString();
    }

    private static (string Label, string Reason) Assess(PerformanceBenchmarkRun a, PerformanceBenchmarkRun b)
    {
        var minimumResponses = Math.Ceiling(Math.Min(a.RequestedSamples, b.RequestedSamples) * 0.75);
        if (a.SuccessCount < minimumResponses || b.SuccessCount < minimumResponses ||
            !a.MedianMs.HasValue || !b.MedianMs.HasValue)
            return ("YETERSİZ ÖLÇÜM", "A veya B aşamasında yanıt sayısı %75'in altında. ICMP kısıtlaması ya da kararsız bağlantı nedeniyle sonuç güvenilir karşılaştırma için yetersiz.");

        var medianDelta = b.MedianMs.Value - a.MedianMs.Value;
        var lossDelta = b.LossPercent - a.LossPercent;
        var jitterDelta = a.JitterMs.HasValue && b.JitterMs.HasValue ? b.JitterMs.Value - a.JitterMs.Value : 0;
        var medianThreshold = Math.Max(2d, a.MedianMs.Value * 0.08d);
        var jitterThreshold = a.JitterMs.HasValue ? Math.Max(3d, a.JitterMs.Value * 0.20d) : double.MaxValue;

        if ((medianDelta <= -medianThreshold && lossDelta <= 1.0 && jitterDelta < jitterThreshold) ||
            (lossDelta <= -5.0 && medianDelta < 3.0))
            return ("OLUMLU DEĞİŞİM GÖZLENDİ", $"B aşamasında medyan RTT {Math.Abs(medianDelta):0.0} ms azaldı veya paket kaybında anlamlı düşüş görüldü. Tek test çifti kesin kazanç kanıtı değildir; tekrar edin.");

        if (medianDelta >= medianThreshold || lossDelta >= 5.0 || jitterDelta >= jitterThreshold)
            return ("OLUMSUZ DEĞİŞİM GÖZLENDİ", "B aşamasında gecikme, jitter veya kayıp kötüleşti. NIC ayarını geri yüklemeyi ve koşulları tekrar kontrol etmeyi değerlendirin.");

        return ("BELİRGİN FARK YOK", "Değişim belirlenen gürültü eşiklerinin altında. Bu ölçümde belirgin bir iyileşme kanıtlanmadı; varsayılan ayarı korumak makul olabilir.");
    }

    private static void AppendMetric(System.Text.StringBuilder sb, string label, double? a, double? b, string unit)
    {
        var aText = a.HasValue ? $"{a.Value:0.0} {unit}" : "—";
        var bText = b.HasValue ? $"{b.Value:0.0} {unit}" : "—";
        var delta = a.HasValue && b.HasValue ? $"{b.Value - a.Value:+0.0;-0.0;0.0} {unit}" : "—";
        sb.AppendLine($"{label,-22} {aText,-14} {bText,-14} {delta}");
    }

    private static double? Percentile(long[] sorted, double percentile)
    {
        if (sorted.Length == 0) return null;
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    internal static bool IsValidRun(PerformanceBenchmarkRun? run)
    {
        if (run is null) return false;
        try
        {
            ValidateRun(run);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void ValidateRun(PerformanceBenchmarkRun run)
    {
        if (run.RequestedSamples is < 10 or > 100 ||
            run.Samples is null ||
            run.Samples.Count != run.RequestedSamples ||
            run.SuccessCount < 0 || run.SuccessCount > run.RequestedSamples ||
            !double.IsFinite(run.LossPercent) || run.LossPercent is < 0 or > 100 ||
            Math.Abs(run.LossPercent - (run.RequestedSamples - run.SuccessCount) * 100d / run.RequestedSamples) > 0.01 ||
            run.CompletedAt < run.StartedAt ||
            run.Samples.Any(s => s is null || string.IsNullOrWhiteSpace(s.Status)))
            throw new InvalidDataException("A/B ölçüm örnekleri bozuk veya tutarsız.");

        for (var i = 0; i < run.Samples.Count; i++)
        {
            var sample = run.Samples[i];
            if (sample.Sequence != i + 1 ||
                (sample.Success && (!sample.RoundtripMs.HasValue || sample.RoundtripMs.Value < 0)) ||
                (!sample.Success && sample.RoundtripMs.HasValue))
                throw new InvalidDataException("A/B ölçüm sırası veya ping örneği geçersiz.");
        }

        var successful = run.Samples.Where(s => s.Success && s.RoundtripMs.HasValue).ToArray();
        if (successful.Length != run.SuccessCount)
            throw new InvalidDataException("Başarılı ping sayısı örneklerle eşleşmiyor.");

        var ordered = successful.Select(s => s.RoundtripMs!.Value).OrderBy(v => v).ToArray();
        if (ordered.Length == 0)
        {
            if (run.MinimumMs.HasValue || run.AverageMs.HasValue || run.MedianMs.HasValue || run.P95Ms.HasValue || run.JitterMs.HasValue)
                throw new InvalidDataException("Yanıt alınmayan ölçümde RTT veya jitter değeri bulunamaz.");
            return;
        }

        if (!run.MinimumMs.HasValue || !run.AverageMs.HasValue || !run.MedianMs.HasValue || !run.P95Ms.HasValue ||
            !double.IsFinite(run.AverageMs.Value) || !double.IsFinite(run.MedianMs.Value) || !double.IsFinite(run.P95Ms.Value) ||
            run.AverageMs.Value < 0 || run.MedianMs.Value < 0 || run.P95Ms.Value < 0)
            throw new InvalidDataException("A/B özet metrikleri eksik veya geçersiz.");

        if (run.MinimumMs.Value != ordered[0] ||
            !MetricsEqual(run.AverageMs.Value, ordered.Average()) ||
            !MetricsEqual(run.MedianMs.Value, Percentile(ordered, 0.50)!.Value) ||
            !MetricsEqual(run.P95Ms.Value, Percentile(ordered, 0.95)!.Value))
            throw new InvalidDataException("A/B özet RTT değerleri ping örnekleriyle eşleşmiyor.");

        if (run.JitterMs.HasValue && (!double.IsFinite(run.JitterMs.Value) || run.JitterMs.Value < 0))
            throw new InvalidDataException("A/B jitter değeri geçersiz.");

        var adjacent = successful.OrderBy(s => s.Sequence).ToArray();
        var jitterPairs = adjacent.Zip(adjacent.Skip(1))
            .Where(pair => pair.Second.Sequence == pair.First.Sequence + 1)
            .Select(pair => Math.Abs((double)pair.Second.RoundtripMs!.Value - pair.First.RoundtripMs!.Value))
            .ToArray();
        double? expectedJitter = jitterPairs.Length == 0 ? null : jitterPairs.Average();
        if (run.JitterMs.HasValue != expectedJitter.HasValue ||
            (expectedJitter.HasValue && !MetricsEqual(run.JitterMs!.Value, expectedJitter.Value)))
            throw new InvalidDataException("A/B jitter değeri ping örnekleriyle eşleşmiyor.");
    }

    private static bool MetricsEqual(double actual, double expected)
    {
        var tolerance = Math.Max(0.000001d, Math.Abs(expected) * 0.000000001d);
        return Math.Abs(actual - expected) <= tolerance;
    }
}
