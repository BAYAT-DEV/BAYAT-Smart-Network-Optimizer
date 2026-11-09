// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.Net.NetworkInformation;

namespace SmartNetworkOptimizer;

/// <summary>Evidence-based, read-only plan generator. It never writes Windows or driver settings.</summary>
internal static class OptimizationDecisionEngine
{
    internal sealed record PingEvidence(int Success, double LossPercent, double? AverageMs, double? JitterMs);

    internal sealed record Input(
        NetworkInterfaceType AdapterType,
        bool AdapterUp,
        long LinkSpeedBitsPerSecond,
        bool HasGateway,
        IReadOnlyDictionary<string, PingEvidence> PingTests,
        bool NicAnalysisMatchesAdapter,
        bool IsPhysicalAdapter,
        IReadOnlyList<NicOptimizationAdvisor.DriverProperty> DriverProperties,
        PerformanceBenchmarkSession? AbSession,
        int LogicalProcessorCount,
        bool HasNicBackup);

    internal sealed record Finding(string Id, string Priority, string Title, string Evidence, string Recommendation);

    internal sealed record Plan(int EvidenceCoverage, string CoverageLabel, IReadOnlyList<Finding> Findings)
    {
        public string ToReport()
        {
            var lines = new List<string>
            {
                "AKILLI OPTİMİZASYON KARAR PLANI — v0.9.0",
                new string('=', 68),
                $"Kanıt kapsamı: %{EvidenceCoverage} — {CoverageLabel}",
                "Bu yüzde performans/kalite puanı değildir; karar üretmek için mevcut ölçüm ve donanım verilerinin kapsamını gösterir.",
                ""
            };

            if (Findings.Count == 0)
                lines.Add("Ek bir işlem önerisi oluşmadı. Mevcut varsayılan ayarları koruyun ve gerçek oyun içi ağ göstergesini izleyin.");

            var index = 1;
            foreach (var finding in Findings)
            {
                lines.Add($"{index++}. [{finding.Priority}] {finding.Title}");
                lines.Add("   Kanıt: " + finding.Evidence);
                lines.Add("   Öneri: " + finding.Recommendation);
                lines.Add(string.Empty);
            }

            lines.Add("GÜVENLİK: Bu karar motoru salt okunurdur. Hiçbir ayarı kendiliğinden değiştirmez.");
            lines.Add("Ölçüm sınırı: ICMP ping/kayıp/jitter, oyunun UDP performans metriğiyle aynı değildir.");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public static Plan Build(Input input)
    {
        var findings = new List<Finding>();
        var evidence = 0;
        var validPingTests = input.PingTests.Where(pair => IsValidPingEvidence(pair.Value)).ToArray();

        // Coverage is completeness of evidence, explicitly not a connection-quality grade.
        evidence += input.AdapterUp ? 15 : 0;
        evidence += input.LinkSpeedBitsPerSecond > 0 ? 10 : 0;
        evidence += input.HasGateway ? 10 : 0;
        evidence += TryGetValidPing(input.PingTests, "Yerel ağ geçidi", out _) ? 20 : 0;
        evidence += (TryGetValidPing(input.PingTests, "Cloudflare", out _) || TryGetValidPing(input.PingTests, "Google DNS", out _)) ? 15 : 0;
        evidence += input.NicAnalysisMatchesAdapter && input.DriverProperties.Count > 0 ? 20 : 0;
        evidence += (PerformanceBenchmark.IsValidRun(input.AbSession?.Before) && PerformanceBenchmark.IsValidRun(input.AbSession?.After)) ? 10 : 0;

        if (!input.AdapterUp)
        {
            findings.Add(new Finding("ADAPTER_DOWN", "YÜKSEK", "Aktif bağdaştırıcı bağlantıda değil",
                "Seçili ağ arabirimi Up durumunda değil.",
                "Önce kablo/Wi-Fi bağlantısını, modem durumunu ve Windows ağ durumunu kontrol edin. Bağlantı yokken optimizasyon uygulamayın."));
        }

        if (input.AdapterType == NetworkInterfaceType.Wireless80211)
        {
            findings.Add(new Finding("WIFI_STABILITY", "ÖNCELİKLİ", "Oyun için kablolu bağlantıyı karşılaştırın",
                "Aktif bağlantı türü Wi-Fi.",
                "Aynı oyun sunucusunda Ethernet ile karşılaştırma yapın. Wi-Fi kullanmanız gerekiyorsa modem/istemci destekliyorsa 5 veya 6 GHz bandını deneyin; sinyal ve kanal yoğunluğunu kontrol edin."));
        }
        else if (input.AdapterType == NetworkInterfaceType.Ethernet && input.LinkSpeedBitsPerSecond > 0 && input.LinkSpeedBitsPerSecond <= 100_000_000)
        {
            findings.Add(new Finding("ETHERNET_LINK_100", "ORTA", "Ethernet link hızı 100 Mbps veya altında",
                $"Windows link hızı {input.LinkSpeedBitsPerSecond / 1_000_000d:0} Mbps.",
                "Kabloyu, router/switch portunu ve her iki cihazın gigabit desteğini kontrol edin. Link hızı internet paketi hızınız değildir; yalnızca yerel Ethernet anlaşma hızıdır."));
        }

        if (!input.HasGateway)
        {
            findings.Add(new Finding("NO_GATEWAY", "YÜKSEK", "Varsayılan ağ geçidi bulunamadı",
                "Aktif bağdaştırıcı için IPv4 varsayılan ağ geçidi okunamadı.",
                "VPN/sanal adaptör etkisini, DHCP/IP yapılandırmasını ve modem bağlantısını doğrulayın. Doğru fiziksel arabirim belirlenmeden ağ ayarı değiştirmeyin."));
        }

        if (TryGetValidPing(input.PingTests, "Yerel ağ geçidi", out var gateway))
        {
            if (gateway.LossPercent > 0)
                findings.Add(new Finding("GATEWAY_ICMP_LOSS", "YÜKSEK", "Yerel ağ geçidine ICMP yanıt kaybı görüldü",
                    $"Başarılı {gateway.Success} prob; ICMP yanıt kaybı %{gateway.LossPercent:0.0}.",
                    "Önce aynı testi birkaç kez tekrarlayın. Wi-Fi ise Ethernet ile karşılaştırın; kablo, sinyal ve router yükünü inceleyin. ICMP kısıtlaması olabileceği için bunu tek başına oyun paket kaybı saymayın."));
            else if (gateway.AverageMs.HasValue)
            {
                var threshold = input.AdapterType == NetworkInterfaceType.Wireless80211 ? 15d : 5d;
                if (gateway.AverageMs.Value > threshold)
                    findings.Add(new Finding("GATEWAY_LATENCY", "ORTA", "Yerel ağ geçidi yanıt süresi görece yüksek",
                        $"Ortalama ICMP RTT {gateway.AverageMs.Value:0.0} ms.",
                        "Aynı koşullarda yeniden ölçün. Wi-Fi paraziti/mesafesi, router yoğunluğu veya kablo/port sorunlarını inceleyin. Tek bir ölçümle sürücü ayarı değiştirmeyin."));
            }
        }
        else
        {
            findings.Add(new Finding("MISSING_GATEWAY_MEASUREMENT", "BİLGİ", "Yerel ağ ölçümü eksik",
                "Henüz ağ geçidine ping testi kaydı yok.",
                "Ping / Jitter / Paket Kaybı Testi ile önce yerel ağ geçidini ölçün; yerel sorun ile ISS/uzak hedef sorununu ayırmaya yardımcı olur."));
        }

        foreach (var name in new[] { "Cloudflare", "Google DNS" })
        {
            if (TryGetValidPing(input.PingTests, name, out var remote) && remote.LossPercent > 0)
            {
                findings.Add(new Finding("REMOTE_ICMP_FILTERING", "BİLGİ", $"{name} hedefinde ICMP yanıt kaybı",
                    $"ICMP yanıt kaybı %{remote.LossPercent:0.0}.",
                    "Başka hedeflerle ve oyun içi ağ istatistiğiyle doğrulayın. Uzak hedef ICMP'yi filtreliyor/önceliksizleştiriyor olabilir; bu sonuç UDP oyun paket kaybını kanıtlamaz."));
                break;
            }
        }

        if (!input.NicAnalysisMatchesAdapter || input.DriverProperties.Count == 0)
        {
            findings.Add(new Finding("NIC_ANALYSIS_MISSING", "BİLGİ", "Sürücüye özel analiz henüz yok",
                "Aktif bağdaştırıcı için NetAdapter gelişmiş özellik envanteri alınmadı veya son taramadan sonra adaptör değişti.",
                "Akıllı Ağ Kartı Analizi düğmesiyle sürücünün bildirdiği mevcut/geçerli seçenekleri okuyun. Analiz yapılmadan hiçbir Registry veya driver tweak uygulamayın."));
        }
        else
        {
            var rss = input.DriverProperties.FirstOrDefault(p => p.RegistryKeyword.Equals("*RSS", StringComparison.OrdinalIgnoreCase));
            if (rss is not null && IsDisabled(rss.DisplayValue))
            {
                var candidate = NicOptimizationAdvisor.CreateSafeRssCandidate(
                    isWifi: input.AdapterType == NetworkInterfaceType.Wireless80211,
                    isEthernet: input.AdapterType == NetworkInterfaceType.Ethernet,
                    isPhysical: input.IsPhysicalAdapter,
                    logicalProcessorCount: input.LogicalProcessorCount,
                    properties: input.DriverProperties);
                if (candidate is not null)
                    findings.Add(new Finding("RSS_CANDIDATE", "DENEYSEL", "Sürücüde RSS için kontrollü test adayı var",
                        $"Fiziksel Ethernet, {input.LogicalProcessorCount} mantıksal işlemci ve sürücünün açıkça bildirdiği Enabled değeri doğrulandı.",
                        input.HasNicBackup
                            ? "Önceki NIC ayar yedeği zaten mevcut. Yeni değişiklik yapmadan önce mevcut A/B oturumunu değerlendirin veya yedeği geri yükleyin."
                            : "Aynı oyun sunucusunda Aşama A ölçümü alın; ardından 'Uygun RSS Testini Uygula' ile ayrı onay verin ve Aşama B ölçümünü yapın. RSS'nin ping düşürmesi garanti değildir."));
            }
            else if (rss is not null && IsEnabled(rss.DisplayValue))
                findings.Add(new Finding("RSS_ALREADY_ENABLED", "BİLGİ", "RSS zaten etkin",
                    "Sürücünün *RSS özelliği açık değer bildiriyor.",
                    "Mevcut durumu koruyun. Sırf düşük ping amacıyla RSS'yi kapatmayın."));
        }

        if (input.AbSession is null || input.AbSession.After is null)
        {
            findings.Add(new Finding("AB_REQUIRED", "ÖNCELİKLİ", "Oyun hedefinde karşılaştırmalı ölçüm eksik",
                "Tamamlanmış A ve B ölçümlerinden oluşan A/B oturumu bulunmuyor.",
                "Oyunun gösterdiği doğrulanmış sunucu adresinde Aşama A ve değişiklikten sonra Aşama B ölçümü alın. Aynı IP kullanılsın ve arka plan indirmeleri kapalı olsun."));
        }
        else
        {
            var before = input.AbSession.Before;
            var after = input.AbSession.After;
            var beforeText = before is null
                ? "A: eksik"
                : $"A: {before.SuccessCount}/{before.RequestedSamples}";
            var afterText = after is null
                ? "B: eksik"
                : $"B: {after.SuccessCount}/{after.RequestedSamples}";
            if (before is null ||
                after is null ||
                !PerformanceBenchmark.IsValidRun(before) ||
                !PerformanceBenchmark.IsValidRun(after) ||
                before.SuccessCount < Math.Ceiling(Math.Min(before.RequestedSamples, after.RequestedSamples) * 0.75) ||
                after.SuccessCount < Math.Ceiling(Math.Min(before.RequestedSamples, after.RequestedSamples) * 0.75))
                findings.Add(new Finding("AB_INSUFFICIENT", "ORTA", "A/B ölçümünün yanıt sayısı yetersiz",
                    $"{beforeText}, {afterText} başarılı ICMP yanıt.",
                    "Sonucu performans kazanımı/kaybı olarak yorumlamayın. ICMP engeli veya hedefin yanıt sınırlaması olabilir; oyun içi istatistik kullanın."));
            else
            {
                var delta = after.MedianMs!.Value - before.MedianMs!.Value;
                var lossDelta = after.LossPercent - before.LossPercent;
                var threshold = Math.Max(2d, before.MedianMs!.Value * 0.08d);
                if (delta >= threshold || lossDelta >= 5d)
                    findings.Add(new Finding("AB_WORSE", "YÜKSEK", "Son ölçümde kötüleşme gözlendi",
                        $"Medyan RTT farkı {delta:+0.0;-0.0;0.0} ms; kayıp farkı {lossDelta:+0.0;-0.0;0.0} yüzde puan.",
                        input.HasNicBackup
                            ? "RSS/NIC deneme ayarı uygulanmışsa 'Önceki Ağ Kartı Ayarını Geri Yükle' ile geri dönün. Ardından aynı koşullarda tekrar ölçün."
                            : "Son yapılan değişikliği belirleyin ve ilgili Windows ayarını geri alın. Tek A/B çifti kesin nedensellik kanıtı değildir."));
                else if (delta <= -threshold && lossDelta <= 1d)
                    findings.Add(new Finding("AB_BETTER_REPEAT", "ORTA", "Olumlu değişim gözlendi; tekrar doğrulayın",
                        $"Medyan RTT farkı {delta:+0.0;-0.0;0.0} ms; kayıp farkı {lossDelta:+0.0;-0.0;0.0} yüzde puan.",
                        "Bu tek A/B çifti kesin kazanç kanıtlamaz. Aynı sunucu ve benzer saat/ağ yükünde en az iki ayrı A/B çifti daha alın."));
                else
                    findings.Add(new Finding("AB_NO_CLEAR_GAIN", "BİLGİ", "A/B testinde belirgin kazanç kanıtlanmadı",
                        $"Medyan RTT farkı {delta:+0.0;-0.0;0.0} ms; kayıp farkı {lossDelta:+0.0;-0.0;0.0} yüzde puan.",
                        "Varsayılan ayarı korumak makul olabilir. Gürültü eşiğinin altındaki farkı optimizasyon başarısı olarak sunmayın."));
            }
        }

        if (validPingTests.Length == 0)
            findings.Add(new Finding("PING_REQUIRED", "ÖNCELİKLİ", "Ağ ölçümü bulunmuyor",
                "Bu oturumda tamamlanmış genel ping testi yok.",
                "Önce yerel ağ geçidi ve iki uzak hedefte ping/jitter/kayıp testi alın; sonuçları tek hedefin yanıt politikasından bağımsız yorumlayın."));

        var label = evidence >= 80 ? "YÜKSEK — temel kanıtlar mevcut" : evidence >= 55 ? "ORTA — bazı ölçümler eksik" : "DÜŞÜK — önce tanılama verisi toplayın";
        var sorted = findings.OrderBy(f => PriorityRank(f.Priority)).ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
        return new Plan(evidence, label, sorted);
    }

    private static bool IsValidPingEvidence(PingEvidence? ping)
    {
        return ping is not null &&
            ping.Success > 0 &&
            double.IsFinite(ping.LossPercent) &&
            ping.LossPercent is >= 0 and < 100 &&
            ping.AverageMs.HasValue &&
            double.IsFinite(ping.AverageMs.Value) &&
            ping.AverageMs.Value >= 0 &&
            (!ping.JitterMs.HasValue || (double.IsFinite(ping.JitterMs.Value) && ping.JitterMs.Value >= 0));
    }

    private static bool TryGetValidPing(IReadOnlyDictionary<string, PingEvidence> pingTests, string name, out PingEvidence evidence)
    {
        foreach (var pair in pingTests)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && IsValidPingEvidence(pair.Value))
            {
                evidence = pair.Value;
                return true;
            }
        }
        evidence = null!;
        return false;
    }
    private static int PriorityRank(string priority) => priority switch
    {
        "YÜKSEK" => 0,
        "ÖNCELİKLİ" => 1,
        "ORTA" => 2,
        "DENEYSEL" => 3,
        _ => 4
    };

    private static bool IsDisabled(string value) =>
        value.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("False", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnabled(string value) =>
        value.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("On", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("True", StringComparison.OrdinalIgnoreCase);
}
