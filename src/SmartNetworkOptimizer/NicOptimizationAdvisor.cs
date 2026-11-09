// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
namespace SmartNetworkOptimizer;

/// <summary>
/// Read-only, evidence-conscious NIC advice. This class never changes driver settings.
/// Driver display strings vary by vendor and language, so unknown properties remain informational.
/// </summary>
internal static class NicOptimizationAdvisor
{
    internal sealed record DriverProperty(
        string DisplayName,
        string DisplayValue,
        string RegistryKeyword,
        string RegistryValue,
        IReadOnlyList<string> ValidDisplayValues,
        IReadOnlyList<string> ValidRegistryValues);

    internal sealed record Advice(string Feature, string CurrentValue, string Assessment, string Reason);

    public static IReadOnlyList<Advice> Evaluate(
        bool isWifi,
        bool isEthernet,
        bool isPhysical,
        int logicalProcessorCount,
        IReadOnlyList<DriverProperty> properties)
    {
        var result = new List<Advice>();
        var rss = Find(properties, "RSS", "Receive Side Scaling", "Receive Side Scaling (RSS)");
        if (rss is not null)
        {
            var enabledSupported = Supports(rss, "Enabled", "On", "True");
            var currentEnabled = IsEnabled(rss.DisplayValue);
            var currentDisabled = IsDisabled(rss.DisplayValue);

            if (currentEnabled)
                result.Add(new Advice("Receive Side Scaling (RSS)", rss.DisplayValue,
                    "MEVCUT DURUMU KORU", "RSS sürücü tarafından etkin görünüyor. Çok kuyruklu paket işleme, destekleyen donanımda CPU yükünü dağıtabilir; yalnızca ping düşürmek için kapatmayın."));
            else if (currentDisabled && !isWifi && isEthernet && isPhysical && logicalProcessorCount >= 4 && enabledSupported &&
                     properties.Count(p => p.RegistryKeyword.Equals("*RSS", StringComparison.OrdinalIgnoreCase)) == 1)
                result.Add(new Advice("Receive Side Scaling (RSS)", rss.DisplayValue,
                    "KONTROLLÜ TEST ADAYI", $"Fiziksel Ethernet bağdaştırıcısı ve {logicalProcessorCount} mantıksal işlemci tespit edildi; sürücü standart *RSS anahtarı ve Enabled seçeneğini bildiriyor. A/B testi öncesi/sonrası aynı oyun sunucusunda ölçüm gerekir."));
            else
                result.Add(new Advice("Receive Side Scaling (RSS)", rss.DisplayValue,
                    "DEĞİŞİKLİK ÖNERİLMİYOR", "Fiziksel Ethernet bağdaştırıcısı, işlemci sayısı, standart *RSS anahtarı veya sürücünün geçerli değer eşlemesi güvenli bir A/B adayı üretmek için yeterli değil."));
        }

        var interrupt = Find(properties, "InterruptModeration", "Interrupt Moderation", "Interrupt Moderation Rate");
        if (interrupt is not null)
            result.Add(new Advice("Interrupt Moderation", interrupt.DisplayValue,
                "VARSAYILANI KORU", "Kapatmak bazı koşullarda gecikme hissini etkileyebilir; ancak kesme ve CPU yükünü artırabilir. Evrensel bir oyun ayarı değildir. Yalnızca ölçümlü A/B testi ve geri alma planıyla değerlendirilmelidir."));

        var eee = Find(properties, "EEE", "Energy Efficient Ethernet", "Green Ethernet");
        if (eee is not null)
        {
            var assessment = isWifi ? "UYGULANMAZ / BİLGİ" : "YALNIZCA SORUN VARSA A/B TESTİ";
            var reason = isWifi
                ? "Bu özellik Ethernet enerji tasarrufu davranışıdır; seçili arayüz Wi-Fi olduğu için bu özellik üzerinden öneri üretilmedi."
                : "Energy Efficient/Green Ethernet bazı cihazlarda düşük trafikte güç tasarrufu sağlar. Boşta gecikme sıçraması ölçülüyorsa kapalı/açık karşılaştırması yapılabilir; kapatmak güç tüketimini artırabilir. Evrensel olarak kapatılması önerilmez.";
            result.Add(new Advice("Energy Efficient / Green Ethernet", eee.DisplayValue, assessment, reason));
        }

        var lso = Find(properties, "Lso", "Large Send Offload", "Large Send Offload v2");
        if (lso is not null)
            result.Add(new Advice("Large Send Offload", lso.DisplayValue, "VARSAYILANI KORU",
                "Büyük gönderim segmentlerini ağ kartına devreder ve throughput verimliliği için kullanılır. Tek başına oyun pingini düşürmek amacıyla kapatılmamalıdır."));

        var checksum = Find(properties, "ChecksumOffload", "Checksum Offload", "TCP Checksum", "UDP Checksum", "IPv4 Checksum", "IPv6 Checksum");
        if (checksum is not null)
            result.Add(new Advice("Checksum Offload", checksum.DisplayValue, "VARSAYILANI KORU",
                "Checksum işini donanıma devretmek CPU kullanımını azaltabilir. Devre dışı bırakılması genel bir düşük gecikme optimizasyonu değildir."));

        var flow = Find(properties, "FlowControl", "Flow Control");
        if (flow is not null)
            result.Add(new Advice("Flow Control", flow.DisplayValue, "VARSAYILANI KORU",
                "Akış kontrolü karşılıklı bağlantı ekipmanları ve ağ koşullarına bağlıdır. Paket kaybı veya duplex sorunu teşhis edilmeden değiştirilmemelidir."));

        var buffers = properties.Where(p => ContainsAny(p, "Receive Buffers", "Transmit Buffers", "Rx Buffer", "Tx Buffer"))
            .Take(2).ToArray();
        foreach (var buffer in buffers)
            result.Add(new Advice(buffer.DisplayName, buffer.DisplayValue, "SÜRÜCÜ VARSAYILANINI KORU",
                "Tampon değerleri kart/driver modeline ve trafik tipine göre değişir. Artırmak bellek ve kuyruk gecikmesini etkileyebilir; en yüksek değeri seçmek otomatik olarak daha iyi değildir."));

        if (result.Count == 0)
            result.Add(new Advice("Tanınan optimizasyon özelliği", "—", "YALNIZCA BİLGİ",
                "Sürücünün yayınladığı özellikler arasında bu sürümün tanıdığı RSS/EEE/LSO/offload/flow-control anahtarları bulunamadı. Bilinmeyen özelliklere dokunulmadı."));

        return result;
    }


    /// <summary>
    /// Returns one conservative, reversible test candidate. It deliberately does not
    /// tune interrupt moderation, offloads, flow control, EEE, or queue buffers.
    /// </summary>
    public static NicPropertyBackup? CreateSafeRssCandidate(
        bool isWifi,
        bool isEthernet,
        bool isPhysical,
        int logicalProcessorCount,
        IReadOnlyList<DriverProperty> properties)
    {
        if (isWifi || !isEthernet || !isPhysical || logicalProcessorCount < 4) return null;

        var candidates = properties.Where(p =>
            p.RegistryKeyword.Equals("*RSS", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) return null; // Ambiguous or unknown driver mapping: do not write.

        var rss = candidates[0];
        if (!IsDisabled(rss.DisplayValue) ||
            string.IsNullOrWhiteSpace(rss.RegistryKeyword) ||
            string.IsNullOrWhiteSpace(rss.RegistryValue) ||
            rss.RegistryValue.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase) ||
            rss.ValidDisplayValues.Count == 0 ||
            rss.ValidDisplayValues.Count != rss.ValidRegistryValues.Count)
            return null;

        var enabledIndexes = rss.ValidDisplayValues
            .Select((value, index) => (value, index))
            .Where(x => x.value.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
                        x.value.Equals("On", StringComparison.OrdinalIgnoreCase) ||
                        x.value.Equals("True", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.index).ToArray();
        if (enabledIndexes.Length != 1) return null;

        var targetRegistryValue = rss.ValidRegistryValues[enabledIndexes[0]];
        if (string.IsNullOrWhiteSpace(targetRegistryValue) ||
            targetRegistryValue.Equals(rss.RegistryValue, StringComparison.OrdinalIgnoreCase))
            return null;

        return new NicPropertyBackup(
            rss.RegistryKeyword,
            string.IsNullOrWhiteSpace(rss.DisplayName) ? "Receive Side Scaling (RSS)" : rss.DisplayName,
            rss.DisplayValue,
            rss.RegistryValue,
            rss.ValidDisplayValues[enabledIndexes[0]],
            targetRegistryValue);
    }

    private static DriverProperty? Find(IReadOnlyList<DriverProperty> properties, params string[] tokens) =>
        properties.FirstOrDefault(p => ContainsAny(p, tokens));

    private static bool ContainsAny(DriverProperty property, params string[] tokens)
    {
        var text = property.DisplayName + " " + property.RegistryKeyword;
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Supports(DriverProperty property, params string[] choices) =>
        property.ValidDisplayValues.Any(value => choices.Any(choice => value.Trim().Equals(choice, StringComparison.OrdinalIgnoreCase)));

    private static bool IsEnabled(string value) =>
        value.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("On", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("True", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabled(string value) =>
        value.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("False", StringComparison.OrdinalIgnoreCase);
}
