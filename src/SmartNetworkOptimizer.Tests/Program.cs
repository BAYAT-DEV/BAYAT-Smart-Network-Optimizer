// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using SmartNetworkOptimizer;

var passed = 0;
var failed = 0;

void Test(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine("[BAŞARILI] " + name);
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine("[BAŞARISIZ] " + name + " — " + ex.Message);
        failed++;
    }
}

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Test("Boş ölçümlerde eksik ping ve A/B bulguları oluşur", () =>
{
    var plan = OptimizationDecisionEngine.Build(CreateInput());
    Check(Has(plan, "PING_REQUIRED"), "PING_REQUIRED bulunamadı.");
    Check(Has(plan, "AB_REQUIRED"), "AB_REQUIRED bulunamadı.");
    Check(Has(plan, "MISSING_GATEWAY_MEASUREMENT"), "Ağ geçidi ölçüm uyarısı bulunamadı.");
});

Test("Geçersiz ping değerleri kanıt sayılmaz", () =>
{
    var pings = new Dictionary<string, OptimizationDecisionEngine.PingEvidence>
    {
        ["Yerel ağ geçidi"] = new(5, double.NaN, 2, 1),
        ["Cloudflare"] = new(0, 0, 10, 1),
        ["Google DNS"] = new(3, 10, double.PositiveInfinity, 1)
    };

    var plan = OptimizationDecisionEngine.Build(CreateInput(pings: pings, hasGateway: true));
    Check(Has(plan, "PING_REQUIRED"), "Geçersiz ping verileri ölçüm olarak kabul edildi.");
    Check(!Has(plan, "GATEWAY_LATENCY"), "Geçersiz gecikme üzerinden bulgu üretildi.");
});

Test("Geçerli yerel ve uzak ping ölçümleri kanıt kapsamına eklenir", () =>
{
    var pings = ValidPings();
    var plan = OptimizationDecisionEngine.Build(CreateInput(pings: pings, hasGateway: true));
    Check(!Has(plan, "PING_REQUIRED"), "Geçerli ping varken ölçüm eksik gösterildi.");
    Check(!Has(plan, "MISSING_GATEWAY_MEASUREMENT"), "Geçerli ağ geçidi ölçümü eksik gösterildi.");
    Check(plan.EvidenceCoverage == 70, $"Beklenen kapsam 70, bulunan {plan.EvidenceCoverage}.");
});

Test("Wi-Fi için RSS test adayı oluşturulmaz", () =>
{
    var properties = new[] { DisabledRss() };
    var plan = OptimizationDecisionEngine.Build(CreateInput(
        adapterType: NetworkInterfaceType.Wireless80211,
        properties: properties,
        nicMatches: true,
        physical: true));

    Check(!Has(plan, "RSS_CANDIDATE"), "Wi-Fi için RSS adayı oluşturuldu.");
    Check(NicOptimizationAdvisor.CreateSafeRssCandidate(
        true, false, true, 8, properties) is null, "Güvenli RSS metodu Wi-Fi için aday döndürdü.");
});

Test("Uygun fiziksel Ethernet koşullarında RSS adayı tanınır", () =>
{
    var properties = new[] { DisabledRss() };
    var plan = OptimizationDecisionEngine.Build(CreateInput(
        properties: properties,
        nicMatches: true,
        physical: true));

    Check(Has(plan, "RSS_CANDIDATE"), "Uygun RSS adayı bulunamadı.");
    Check(NicOptimizationAdvisor.CreateSafeRssCandidate(
        false, true, true, 8, properties) is not null, "Güvenli RSS adayı oluşturulamadı.");
});

Test("Birden fazla RSS anahtarı belirsizlik nedeniyle reddedilir", () =>
{
    var properties = new[] { DisabledRss(), DisabledRss() };
    var candidate = NicOptimizationAdvisor.CreateSafeRssCandidate(
        false, true, true, 8, properties);
    Check(candidate is null, "Birden fazla RSS anahtarı varken aday oluşturuldu.");
});

Test("A/B oturumu yoksa karşılaştırma zorunluluğu gösterilir", () =>
{
    var plan = OptimizationDecisionEngine.Build(CreateInput());
    Check(Has(plan, "AB_REQUIRED"), "A/B zorunluluğu bulunamadı.");
});

Test("Yetersiz A/B yanıt sayısı başarı olarak yorumlanmaz", () =>
{
    var session = CreateSession(
        CreateRun(20, 14, 50),
        CreateRun(20, 20, 40));
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Yetersiz ölçüm uyarısı bulunamadı.");
    Check(!Has(plan, "AB_BETTER_REPEAT"), "Yetersiz ölçüm iyileşme sayıldı.");
});

Test("A/B ölçümünde belirgin iyileşme tekrar doğrulama ister", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40));
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_BETTER_REPEAT"), "İyileşme bulgusu bulunamadı.");
});

Test("A/B ölçümünde belirgin kötüleşme tespit edilir", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 60));
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_WORSE"), "Kötüleşme bulgusu bulunamadı.");
});

Test("Küçük A/B farkı kesin kazanç olarak sunulmaz", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 49));
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_NO_CLEAR_GAIN"), "Belirsiz sonuç bulgusu bulunamadı.");
});

Test("NaN medyan gecikme geçersiz A/B ölçümü sayılır", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { MedianMs = double.NaN });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "NaN medyan reddedilmedi.");
});

Test("Negatif medyan gecikme geçersiz A/B ölçümü sayılır", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { MedianMs = -1 });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Negatif medyan reddedilmedi.");
});

Test("Geçersiz paket kaybı A/B karşılaştırmasına alınmaz", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { LossPercent = double.NaN });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "NaN paket kaybı reddedilmedi.");
});

Test("Sıfır örnek veya imkânsız yanıt sayısı geçersizdir", () =>
{
    var zeroSamples = CreateSession(
        CreateRun(0, 0, 50),
        CreateRun(0, 0, 40));

    var impossibleCount = CreateSession(
        CreateRun(20, 21, 50),
        CreateRun(20, 20, 40));

    var zeroPlan = OptimizationDecisionEngine.Build(CreateInput(abSession: zeroSamples));
    var impossiblePlan = OptimizationDecisionEngine.Build(CreateInput(abSession: impossibleCount));

    Check(Has(zeroPlan, "AB_INSUFFICIENT"), "Sıfır örnek sayısı reddedilmedi.");
    Check(Has(impossiblePlan, "AB_INSUFFICIENT"), "İstenen örnek sayısını aşan yanıt sayısı reddedilmedi.");
});
Console.WriteLine();
Test("Sonsuz medyan gecikme geçersiz A/B ölçümü sayılır", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { MedianMs = double.PositiveInfinity });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Sonsuz medyan reddedilmedi.");
});

Test("Negatif paket kaybı geçersiz A/B ölçümü sayılır", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { LossPercent = -0.1 });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Negatif paket kaybı reddedilmedi.");
});

Test("Yüzde 100 üzerindeki paket kaybı geçersizdir", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { LossPercent = 100.1 });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Yüzde 100 üzerindeki paket kaybı reddedilmedi.");
});
Test("Geçerli A/B ölçümleri kanıt kapsamına eklenir", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40));
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(plan.EvidenceCoverage == 35, $"Geçerli A/B için beklenen kapsam 35, bulunan {plan.EvidenceCoverage}.");
});

Test("A ölçümü geçersizse A/B kanıt kapsamına eklenmez", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50) with { MedianMs = double.NaN },
        CreateRun(20, 20, 40));
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Geçersiz A ölçümü reddedilmedi.");
    Check(plan.EvidenceCoverage == 25, $"Geçersiz A ölçümünde beklenen kapsam 25, bulunan {plan.EvidenceCoverage}.");
});

Test("B ölçümü geçersizse A/B kanıt kapsamına eklenmez", () =>
{
    var session = CreateSession(
        CreateRun(20, 20, 50),
        CreateRun(20, 20, 40) with { LossPercent = 100.1 });
    var plan = OptimizationDecisionEngine.Build(CreateInput(abSession: session));
    Check(Has(plan, "AB_INSUFFICIENT"), "Geçersiz B ölçümü reddedilmedi.");
    Check(plan.EvidenceCoverage == 25, $"Geçersiz B ölçümünde beklenen kapsam 25, bulunan {plan.EvidenceCoverage}.");
});
Test("Boş native tablolar dört ayrıştırıcıda da boş sonuç verir", () =>
{
    Check(GameConnectionDiscovery.ParseTcp4Table(Array.Empty<byte>()).Count == 0, "TCP4 boş değil.");
    Check(GameConnectionDiscovery.ParseTcp6Table(Array.Empty<byte>()).Count == 0, "TCP6 boş değil.");
    Check(GameConnectionDiscovery.ParseUdp4Table(Array.Empty<byte>()).Count == 0, "UDP4 boş değil.");
    Check(GameConnectionDiscovery.ParseUdp6Table(Array.Empty<byte>()).Count == 0, "UDP6 boş değil.");
});

Test("TCP4 tablosu adresleri portları ve PID değerini doğru ayrıştırır", () =>
{
    var table = CreateNativeTable(24);
    WriteUInt32(table, 4, 5);
    WriteAddress(table, 8, "192.0.2.10");
    WritePort(table, 12, 443);
    WriteAddress(table, 16, "198.51.100.20");
    WritePort(table, 20, 27015);
    WriteUInt32(table, 24, 1234);

    var rows = GameConnectionDiscovery.ParseTcp4Table(table);
    Check(rows.Count == 1, "TCP4 kaydı bulunamadı.");
    Check(rows[0].ProcessId == 1234, "TCP4 PID hatalı.");
    Check(rows[0].LocalAddress == "192.0.2.10", "TCP4 yerel adres hatalı.");
    Check(rows[0].LocalPort == 443, "TCP4 yerel port hatalı.");
    Check(rows[0].RemoteAddress == "198.51.100.20", "TCP4 uzak adres hatalı.");
    Check(rows[0].RemotePort == 27015, "TCP4 uzak port hatalı.");
});

Test("TCP6 ScopeId byte sırası değiştirilmeden ayrıştırılır", () =>
{
    var table = CreateNativeTable(56);
    WriteAddress(table, 4, "fe80::1");
    WriteUInt32(table, 20, 0x01020304);
    WritePort(table, 24, 443);
    WriteAddress(table, 28, "2001:db8::2");
    WriteUInt32(table, 44, 0);
    WritePort(table, 48, 27015);
    WriteUInt32(table, 52, 5);
    WriteUInt32(table, 56, 1234);

    var rows = GameConnectionDiscovery.ParseTcp6Table(table);
    Check(rows.Count == 1, "TCP6 kaydı bulunamadı.");
    Check(IPAddress.Parse(rows[0].LocalAddress).ScopeId == 0x01020304,
        "TCP6 yerel ScopeId byte sırası hatalı.");
    Check(rows[0].LocalPort == 443, "TCP6 yerel port hatalı.");
    Check(rows[0].RemotePort == 27015, "TCP6 uzak port hatalı.");
    Check(rows[0].ProcessId == 1234, "TCP6 PID hatalı.");
});

Test("UDP4 tablosu adres port ve PID değerlerini doğru ayrıştırır", () =>
{
    var table = CreateNativeTable(12);
    WriteAddress(table, 4, "192.0.2.30");
    WritePort(table, 8, 50000);
    WriteUInt32(table, 12, 4321);

    var rows = GameConnectionDiscovery.ParseUdp4Table(table);
    Check(rows.Count == 1, "UDP4 kaydı bulunamadı.");
    Check(rows[0].LocalAddress == "192.0.2.30", "UDP4 adres hatalı.");
    Check(rows[0].LocalPort == 50000, "UDP4 port hatalı.");
    Check(rows[0].ProcessId == 4321, "UDP4 PID hatalı.");
});

Test("UDP6 ScopeId byte sırası değiştirilmeden ayrıştırılır", () =>
{
    var table = CreateNativeTable(28);
    WriteAddress(table, 4, "fe80::5");
    WriteUInt32(table, 20, 0x01020304);
    WritePort(table, 24, 60000);
    WriteUInt32(table, 28, 5678);

    var rows = GameConnectionDiscovery.ParseUdp6Table(table);
    Check(rows.Count == 1, "UDP6 kaydı bulunamadı.");
    Check(IPAddress.Parse(rows[0].LocalAddress).ScopeId == 0x01020304,
        "UDP6 ScopeId byte sırası hatalı.");
    Check(rows[0].LocalPort == 60000, "UDP6 port hatalı.");
    Check(rows[0].ProcessId == 5678, "UDP6 PID hatalı.");
});

Test("Eksik tablo başlığı ve geçersiz kayıt sayısı reddedilir", () =>
{
    ExpectInvalidData(
        () => GameConnectionDiscovery.ParseTcp4Table(new byte[] { 1, 2, 3 }),
        "Eksik TCP4 başlığı kabul edildi.");

    ExpectInvalidData(
        () => GameConnectionDiscovery.ParseTcp4Table(new byte[] { 1, 0, 0, 0 }),
        "Olmayan TCP4 kaydı kabul edildi.");
});
Test("DNS kataloğu 41 kullanıcı adresini ve iki mevcut Cloudflare adresini içerir", () =>
{
    var entries = DnsResolverCatalog.All;

    Check(entries.Count == DnsResolverCatalog.ExpectedCount,
        $"Beklenen {DnsResolverCatalog.ExpectedCount} DNS adresi, bulunan {entries.Count}.");

    Check(entries.Count(e => e.WasProvidedByUser) == DnsResolverCatalog.UserProvidedServerCount,
        "Kullanıcının verdiği 41 DNS adresinin sayısı eşleşmiyor.");

    Check(entries.Count(e => !e.WasProvidedByUser) == DnsResolverCatalog.ExistingBuiltInServerCount,
        "Mevcut Cloudflare adresleri korunmadı.");

    var addresses = entries.Select(e => e.Server).ToArray();

    Check(addresses.Distinct(StringComparer.Ordinal).Count() == DnsResolverCatalog.ExpectedCount,
        "DNS kataloğunda yinelenen IP adresi var.");

    foreach (var entry in entries)
    {
        Check(!string.IsNullOrWhiteSpace(entry.Provider), "DNS sağlayıcı adı boş.");
        Check(!string.IsNullOrWhiteSpace(entry.ProfileId), "DNS profil kimliği boş.");
        Check(!string.IsNullOrWhiteSpace(entry.Service), "DNS hizmet açıklaması boş.");
        Check(IPAddress.TryParse(entry.Server, out var address) &&
              address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork,
              "Geçersiz IPv4 DNS adresi: " + entry.Server);
    }
});

Test("DNS uygulama adayları aynı sağlayıcının eksiksiz profil çiftlerinden oluşur", () =>
{
    var groups = DnsResolverCatalog.All
        .Where(e => e.IsApplicationCandidate)
        .GroupBy(e => e.ProfileId, StringComparer.Ordinal);

    foreach (var group in groups)
    {
        Check(group.Count() == 2,
            "Uygulama adayı profil iki DNS adresi içermiyor: " + group.Key);

        Check(group.Select(e => e.Provider).Distinct(StringComparer.Ordinal).Count() == 1,
            "Bir DNS profilinde farklı sağlayıcılar karıştırılmış: " + group.Key);
    }
});

Test("DNS sorgu paketi doğru DNS başlığı ve A/IN sorusu üretir", () =>
{
    var query = DnsResolverBenchmark.BuildQuery("www.example.com", 0x1234);

    Check(query.Length >= 12, "DNS sorgu başlığı eksik.");
    Check(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(0, 2)) == 0x1234,
        "DNS işlem kimliği hatalı.");
    Check(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(2, 2)) == 0x0100,
        "DNS recursion desired bayrağı hatalı.");
    Check(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4, 2)) == 1,
        "DNS soru sayısı bir değil.");
    Check(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(query.Length - 4, 2)) == 1,
        "DNS soru türü A değil.");
    Check(BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(query.Length - 2, 2)) == 1,
        "DNS soru sınıfı IN değil.");

    var rejected = false;
    try
    {
        DnsResolverBenchmark.BuildQuery("invalid domain name", 1);
    }
    catch (ArgumentException)
    {
        rejected = true;
    }

    Check(rejected, "Boşluk içeren alan adı DNS paketine kabul edildi.");
});

Test("DNS yanıtı işlem kimliği soru adı ve A kaydıyla doğrulanır", () =>
{
    var query = DnsResolverBenchmark.BuildQuery("www.example.com", 0x2345);
    var response = CreateDnsAResponse(query);

    Check(DnsResolverBenchmark.TryValidateResponse(
        response, 0x2345, "www.example.com", out var status),
        "Geçerli DNS A yanıtı reddedildi: " + status);

    Check(status == "Success", "DNS başarı durumu yanlış.");
});

Test("DNS yanıtı farklı işlem kimliğinde veya farklı soru adında reddedilir", () =>
{
    var query = DnsResolverBenchmark.BuildQuery("www.example.com", 0x3456);
    var response = CreateDnsAResponse(query);

    Check(!DnsResolverBenchmark.TryValidateResponse(
        response, 0x3457, "www.example.com", out var idStatus),
        "Farklı işlem kimliğine sahip yanıt kabul edildi.");

    Check(idStatus == "TransactionIdMismatch",
        "İşlem kimliği hatası doğru sınıflandırılmadı.");

    Check(!DnsResolverBenchmark.TryValidateResponse(
        response, 0x3456, "www.other.test", out var nameStatus),
        "Farklı soru adına ait yanıt kabul edildi.");

    Check(nameStatus == "QuestionNameMismatch",
        "Soru adı uyuşmazlığı doğru sınıflandırılmadı.");
});

Test("DNS NXDOMAIN kesilmiş ve A kaydı içermeyen yanıtlar başarılı sayılmaz", () =>
{
    var query = DnsResolverBenchmark.BuildQuery("www.example.com", 0x4567);

    var nxdomain = CreateDnsAResponse(query, 0x8183, false);
    Check(!DnsResolverBenchmark.TryValidateResponse(
        nxdomain, 0x4567, "www.example.com", out var nxStatus),
        "NXDOMAIN başarılı DNS yanıtı sayıldı.");
    Check(nxStatus == "NXDOMAIN", "NXDOMAIN durumu doğru sınıflandırılmadı.");

    var truncated = CreateDnsAResponse(query, 0x8380, false);
    Check(!DnsResolverBenchmark.TryValidateResponse(
        truncated, 0x4567, "www.example.com", out var truncatedStatus),
        "Kesilmiş DNS yanıtı geçerli kabul edildi.");
    Check(truncatedStatus == "Truncated",
        "Kesilmiş yanıt durumu doğru sınıflandırılmadı.");

    var noAnswer = CreateDnsAResponse(query, 0x8180, false);
    Check(!DnsResolverBenchmark.TryValidateResponse(
        noAnswer, 0x4567, "www.example.com", out var noAnswerStatus),
        "A kaydı olmayan yanıt başarılı sayıldı.");
    Check(noAnswerStatus == "NoAAnswer",
        "A kaydı olmayan yanıt doğru sınıflandırılmadı.");
});

Test("DNS median uses the middle pair for an even sample count", () =>
{
    var oddMedian = DnsResolverBenchmark.CalculateMedian(new[] { 1d, 3d, 5d });
    var evenMedian = DnsResolverBenchmark.CalculateMedian(new[] { 1d, 2d, 3d, 4d });

    Check(oddMedian == 3d, "Odd-sample median is incorrect.");
    Check(evenMedian == 2.5d, "Even-sample median must average the two middle values.");

    var emptyMedian = DnsResolverBenchmark.CalculateMedian(Array.Empty<double>());
    Check(emptyMedian is null, "Empty input must not produce a median.");
});

Test("DNS nearest-rank P95 is calculated from sorted valid samples", () =>
{
    var values = Enumerable.Range(1, 15).Select(value => (double)value).ToArray();
    var p95 = DnsResolverBenchmark.CalculatePercentile(values, 0.95d);

    Check(p95 == 15d, "P95 for 15 samples must use the nearest-rank 15th sample.");

    var invalidRejected = false;
    try
    {
        DnsResolverBenchmark.CalculatePercentile(values, 1.5d);
    }
    catch (ArgumentOutOfRangeException)
    {
        invalidRejected = true;
    }

    Check(invalidRejected, "An invalid percentile value was accepted.");
});

Test("DNS profile ranking combines three sessions and preserves failures", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    for (var index = 0; index < 3; index++)
    {
        var tt39 = CreateDnsBenchmarkTestResult(
            "Türk Telekom", "tt-test", "195.175.39.39", true, 14d, 15);

        var tt40 = CreateDnsBenchmarkTestResult(
            "Türk Telekom", "tt-test", "195.175.39.40", true, 15d,
            index == 0 ? 12 : 15);

        var cf1 = CreateDnsBenchmarkTestResult(
            "Cloudflare", "cf-test", "1.1.1.1", true, 70d, 15);

        var cf2 = CreateDnsBenchmarkTestResult(
            "Cloudflare", "cf-test", "1.0.0.1", true, 71d, 15);

        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[] { tt39, tt40, cf1, cf2 }));
    }

    var profiles = new[]
    {
        new DnsResolverProfileDefinition(
            "tt-test", "Türk Telekom",
            new[] { "195.175.39.39", "195.175.39.40" }),

        new DnsResolverProfileDefinition(
            "cf-test", "Cloudflare",
            new[] { "1.1.1.1", "1.0.0.1" })
    };

    var ranking = DnsResolverRanking.SelectBestProfile(sessions, profiles);

    Check(ranking is not null, "A reliable profile should have been selected.");
    Check(ranking!.ProfileId == "tt-test", "The lowest-latency stable profile was not selected.");
    Check(ranking.SessionCount == 3, "All three sessions must be included.");
    Check(ranking.SuccessCount == 87, "The aggregate success count is incorrect.");
    Check(ranking.RequestedCount == 90, "The aggregate request count is incorrect.");
    Check(ranking.PassingSessionCount == 3, "The per-session passing count is incorrect.");

    var aggregates = DnsResolverRanking.AggregateSessions(sessions);
    var tt40Aggregate = aggregates.Single(result => result.Server == "195.175.39.40");

    Check(tt40Aggregate.SuccessCount == 42, "The first-session failures were not preserved.");
    Check(tt40Aggregate.RequestedCount == 45, "The three-session request count is incorrect.");
    Check(tt40Aggregate.PassingSessionCount == 3, "The 80% first-session threshold was not applied.");
});

Test("DNS ranking refuses a trusted decision with fewer than three sessions", () =>
{
    var singleSession = new[]
    {
        new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow,
            new[]
            {
                CreateDnsBenchmarkTestResult(
                    "Türk Telekom", "tt-test", "195.175.39.39", true, 14d, 15),
                CreateDnsBenchmarkTestResult(
                    "Türk Telekom", "tt-test", "195.175.39.40", true, 15d, 15)
            })
    };

    var profiles = new[]
    {
        new DnsResolverProfileDefinition(
            "tt-test", "Türk Telekom",
            new[] { "195.175.39.39", "195.175.39.40" })
    };

    var ranking = DnsResolverRanking.SelectBestProfile(singleSession, profiles);

    Check(ranking is null, "One session must not produce a trusted profile decision.");
});

Test("DNS ranking excludes a profile below the aggregate success threshold", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    for (var index = 0; index < 3; index++)
    {
        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[]
            {
                CreateDnsBenchmarkTestResult(
                    "Türk Telekom", "tt-test", "195.175.39.39", true, 14d, 15),
                CreateDnsBenchmarkTestResult(
                    "Türk Telekom", "tt-test", "195.175.39.40", true, 15d, 10),
                CreateDnsBenchmarkTestResult(
                    "Cloudflare", "cf-test", "1.1.1.1", true, 70d, 15),
                CreateDnsBenchmarkTestResult(
                    "Cloudflare", "cf-test", "1.0.0.1", true, 71d, 15)
            }));
    }

    var profiles = new[]
    {
        new DnsResolverProfileDefinition(
            "tt-test", "Türk Telekom",
            new[] { "195.175.39.39", "195.175.39.40" }),

        new DnsResolverProfileDefinition(
            "cf-test", "Cloudflare",
            new[] { "1.1.1.1", "1.0.0.1" })
    };

    var ranking = DnsResolverRanking.SelectBestProfile(sessions, profiles);

    Check(ranking is not null, "The reliable fallback profile was not selected.");
    Check(ranking!.ProfileId == "cf-test", "A profile below 90% aggregate success was not excluded.");
});
Test("DNS multi-session individual ranking selects the fastest reliable resolver", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    for (var index = 0; index < 3; index++)
    {
        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[]
            {
                CreateDnsBenchmarkTestResult(
                    "Türk Telekom", "tt-test", "195.175.39.39", true, 14d, 15),
                CreateDnsBenchmarkTestResult(
                    "Türk Telekom", "tt-test", "195.175.39.40", true, 15d, 15),
                CreateDnsBenchmarkTestResult(
                    "Cloudflare", "cf-test", "1.1.1.1", true, 70d, 15)
            }));
    }

    var best = DnsResolverRanking.SelectBestReliableIndividual(sessions);

    Check(best is not null, "A reliable individual DNS server should be selected.");
    Check(best!.Server == "195.175.39.39", "The fastest reliable individual resolver was not selected.");
    Check(best.SuccessCount == 45, "The individual resolver success count is incorrect.");
});

Test("DNS profile ranking penalizes extreme tail latency", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    var fastSpiky = Enumerable.Repeat(20d, 14)
        .Concat(new[] { 800d })
        .ToArray();

    var stable = Enumerable.Repeat(35d, 15).ToArray();

    for (var index = 0; index < 3; index++)
    {
        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[]
            {
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Fast Spiky", "spiky-test", "192.0.2.1", true, fastSpiky),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Fast Spiky", "spiky-test", "192.0.2.2", true, fastSpiky),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Stable Network", "stable-test", "198.51.100.1", true, stable),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Stable Network", "stable-test", "198.51.100.2", true, stable)
            }));
    }

    var profiles = new[]
    {
        new DnsResolverProfileDefinition(
            "spiky-test", "Fast Spiky",
            new[] { "192.0.2.1", "192.0.2.2" }),

        new DnsResolverProfileDefinition(
            "stable-test", "Stable Network",
            new[] { "198.51.100.1", "198.51.100.2" })
    };

    var ranking = DnsResolverRanking.SelectBestProfile(sessions, profiles);

    Check(ranking is not null, "A reliable profile should have been selected.");
    Check(
        ranking!.ProfileId == "stable-test",
        "A profile with extreme P95 latency was preferred over the stable profile.");
});

Test("DNS profile ranking does not hide a weak server behind pair averages", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    var veryFast = Enumerable.Repeat(10d, 15).ToArray();
    var slow = Enumerable.Repeat(60d, 15).ToArray();
    var balanced = Enumerable.Repeat(40d, 15).ToArray();

    for (var index = 0; index < 3; index++)
    {
        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[]
            {
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Imbalanced Network", "imbalanced-test", "192.0.2.10", true, veryFast),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Imbalanced Network", "imbalanced-test", "192.0.2.11", true, slow),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Balanced Network", "balanced-test", "198.51.100.10", true, balanced),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Balanced Network", "balanced-test", "198.51.100.11", true, balanced)
            }));
    }

    var profiles = new[]
    {
        new DnsResolverProfileDefinition(
            "imbalanced-test", "Imbalanced Network",
            new[] { "192.0.2.10", "192.0.2.11" }),

        new DnsResolverProfileDefinition(
            "balanced-test", "Balanced Network",
            new[] { "198.51.100.10", "198.51.100.11" })
    };

    var ranking = DnsResolverRanking.SelectBestProfile(sessions, profiles);

    Check(ranking is not null, "A reliable profile should have been selected.");
    Check(
        ranking!.ProfileId == "balanced-test",
        "The weak server was hidden by averaging the two server latencies.");
});

Test("DNS individual ranking penalizes extreme tail latency", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    var fastSpiky = Enumerable.Repeat(10d, 14)
        .Concat(new[] { 700d })
        .ToArray();

    var stable = Enumerable.Repeat(25d, 15).ToArray();

    for (var index = 0; index < 3; index++)
    {
        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[]
            {
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Fast Spiky", "individual-spiky", "192.0.2.20", true, fastSpiky),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Stable Network", "individual-stable", "198.51.100.20", true, stable)
            }));
    }

    var best = DnsResolverRanking.SelectBestReliableIndividual(sessions);

    Check(best is not null, "A reliable individual DNS server should have been selected.");
    Check(
        best!.Server == "198.51.100.20",
        "The individual ranking preferred a DNS server with extreme tail latency.");
});

Test("DNS profile ranking uses jitter when P95 is equal", () =>
{
    var sessions = new List<DnsResolverBenchmarkSession>();

    var lowJitter = Enumerable.Repeat(10d, 7)
        .Concat(Enumerable.Repeat(20d, 8))
        .ToArray();

    var highJitter = Enumerable.Range(0, 14)
        .Select(index => index % 2 == 0 ? 10d : 20d)
        .Append(20d)
        .ToArray();

    for (var index = 0; index < 3; index++)
    {
        sessions.Add(new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow.AddMinutes(index),
            new[]
            {
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Low Jitter", "low-jitter-test", "192.0.2.30", true, lowJitter),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "Low Jitter", "low-jitter-test", "192.0.2.31", true, lowJitter),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "High Jitter", "high-jitter-test", "198.51.100.30", true, highJitter),
                CreateDnsBenchmarkTestResultWithLatencies(
                    "High Jitter", "high-jitter-test", "198.51.100.31", true, highJitter)
            }));
    }

    var profiles = new[]
    {
        new DnsResolverProfileDefinition(
            "low-jitter-test", "Low Jitter",
            new[] { "192.0.2.30", "192.0.2.31" }),

        new DnsResolverProfileDefinition(
            "high-jitter-test", "High Jitter",
            new[] { "198.51.100.30", "198.51.100.31" })
    };

    var ranking = DnsResolverRanking.SelectBestProfile(sessions, profiles);

    Check(ranking is not null, "A reliable profile should have been selected.");
    Check(
        ranking!.ProfileId == "low-jitter-test",
        "The profile with higher jitter was preferred despite equal median and P95.");
});

Test("DNS network fingerprint is deterministic and changes with the network identity", () =>
{
    var first = DnsResolverHistoryStore.CreateNetworkFingerprint(
        "adapter-guid", "Wireless80211", "192.168.1.1", "AABBCCDDEEFF", "192.168.1.0/24");

    var same = DnsResolverHistoryStore.CreateNetworkFingerprint(
        "adapter-guid", "Wireless80211", "192.168.1.1", "AABBCCDDEEFF", "192.168.1.0/24");

    var otherNetwork = DnsResolverHistoryStore.CreateNetworkFingerprint(
        "adapter-guid", "Wireless80211", "192.168.2.1", "AABBCCDDEEFF", "192.168.2.0/24");

    Check(first == same, "The same network identity must produce the same fingerprint.");
    Check(first != otherNetwork, "A different network identity must produce a different fingerprint.");
    Check(first.Length == 64, "The fingerprint must be a SHA-256 hexadecimal value.");
    Check(!first.Contains("192.168", StringComparison.Ordinal), "Raw network information leaked into the fingerprint.");
});
Test("DNS history persistence round-trips all catalog entries and preserves previous file", () =>
{
    var fingerprint = DnsResolverHistoryStore.CreateNetworkFingerprint(
        "test-adapter",
        "Wireless80211",
        "192.168.1.1",
        "AABBCCDDEEFF",
        "192.168.1.0/24");

    var testRoot = Path.Combine(
        Path.GetTempPath(),
        "BAYAT-DNS-History-Tests",
        Guid.NewGuid().ToString("N"));

    var historyPath = DnsResolverHistoryStore.GetHistoryPath(
        testRoot,
        fingerprint);

    static DnsResolverBenchmarkSession CreateSession(double offsetMs)
    {
        var results = DnsResolverCatalog.All
            .Select(definition =>
            {
                var latencies = Enumerable.Range(
                        1,
                        DnsResolverRanking.SamplesPerResolverSession)
                    .Select(index =>
                        10d + offsetMs + ((index % 3) * 0.1d))
                    .ToArray();

                var samples = latencies
                    .Select((latency, index) =>
                        new DnsResolverSample(
                            index + 1,
                            $"host{(index % 5) + 1}.example",
                            true,
                            latency,
                            "Success"))
                    .ToArray();

                return new DnsResolverBenchmarkResult(
                    definition.Provider,
                    definition.ProfileId,
                    definition.Service,
                    definition.Server,
                    definition.IsApplicationCandidate,
                    definition.ApplicationNotice,
                    latencies.Length,
                    latencies.Length,
                    0,
                    0,
                    latencies.Min(),
                    latencies.Average(),
                    DnsResolverBenchmark.CalculateMedian(latencies),
                    DnsResolverBenchmark.CalculatePercentile(latencies, 0.95d),
                    0.1d,
                    "Yok",
                    samples);
            })
            .ToArray();

        return new DnsResolverBenchmarkSession(
            DateTimeOffset.UtcNow,
            results);
    }

    var firstSession = CreateSession(0d);

    var firstDocument = new DnsResolverHistoryDocument(
        DnsResolverHistoryStore.CurrentSchemaVersion,
        fingerprint,
        new List<DnsResolverBenchmarkSession>
        {
            firstSession
        });

    // İlk kayıt.
    DnsResolverHistoryStore.Save(historyPath, firstDocument);

    Check(
        File.Exists(historyPath),
        "İlk DNS geçmiş dosyası oluşturulmadı.");

    var firstSavedBytes = File.ReadAllBytes(historyPath);

    Check(
        firstSavedBytes.Length > 0,
        "İlk DNS geçmiş dosyası boş oluşturuldu.");

    // İkinci oturum.
    var secondSession = CreateSession(1d);

    var secondDocument = new DnsResolverHistoryDocument(
        DnsResolverHistoryStore.CurrentSchemaVersion,
        fingerprint,
        new List<DnsResolverBenchmarkSession>
        {
            firstSession,
            secondSession
        });

    DnsResolverHistoryStore.Save(historyPath, secondDocument);

    // Diskten yeniden oku.
    var loaded = DnsResolverHistoryStore.Load(
        historyPath,
        fingerprint);

    Check(
        loaded.SchemaVersion == DnsResolverHistoryStore.CurrentSchemaVersion,
        "DNS geçmiş şema sürümü korunmadı.");

    Check(
        loaded.NetworkFingerprint == fingerprint,
        "Ağ parmak izi kayıt sırasında değişti.");

    Check(
        loaded.Sessions.Count == 2,
        "İki DNS oturumu diskten geri yüklenemedi.");

    Check(
        loaded.Sessions.All(session =>
            session.Results.Count == DnsResolverCatalog.ExpectedCount),
        "Oturumlardan birinde katalogdaki 43 DNS adresinin tamamı bulunmuyor.");

    Check(
        loaded.Sessions.All(session =>
            session.Results.All(result =>
                result.Samples is not null &&
                result.Samples.Count == DnsResolverRanking.SamplesPerResolverSession)),
        "DNS sorgu örnekleri eksik veya kaybolmuş.");

    Check(
        loaded.Sessions.All(session =>
            session.Results.All(result =>
                result.SuccessCount == 15 &&
                result.FailureCount == 0 &&
                result.TimeoutCount == 0)),
        "DNS başarı ve hata sayaçları geri yüklemede değişti.");

    // İkinci kayıt öncesindeki dosyanın kalıcı yedeğini doğrula.
    var backupDirectory = Path.Combine(
        testRoot,
        "safety-backups");

    Check(
        Directory.Exists(backupDirectory),
        "İkinci kayıt sırasında yedek klasörü oluşturulmadı.");

    var backupFiles = Directory.GetFiles(
        backupDirectory,
        "*.json.bak",
        SearchOption.TopDirectoryOnly);

    Check(
        backupFiles.Length == 1,
        "Önceki DNS geçmişinin tam olarak bir yedeği bekleniyordu.");

    var backupBytes = File.ReadAllBytes(backupFiles[0]);

    Check(
        backupBytes.SequenceEqual(firstSavedBytes),
        "Önceki DNS geçmişinin yedeği ilk kayıtla aynı değil.");

    // Başka bir ağ kimliğiyle bu geçmişin okunması reddedilmeli.
    var otherFingerprint = DnsResolverHistoryStore.CreateNetworkFingerprint(
        "different-adapter",
        "Wireless80211",
        "192.168.2.1",
        "112233445566",
        "192.168.2.0/24");

    ExpectInvalidData(
        () => DnsResolverHistoryStore.Load(historyPath, otherFingerprint),
        "Farklı ağ kimliğiyle DNS geçmişi okunmasına izin verildi.");

    Console.WriteLine("DNS geçmiş test alanı: " + testRoot);
});
Console.WriteLine($"TEST SONUCU: {passed} başarılı, {failed} başarısız.");

Environment.ExitCode = failed == 0 ? 0 : 1;

static bool Has(OptimizationDecisionEngine.Plan plan, string id) =>
    plan.Findings.Any(f => f.Id == id);

static OptimizationDecisionEngine.Input CreateInput(
    NetworkInterfaceType adapterType = NetworkInterfaceType.Ethernet,
    IReadOnlyDictionary<string, OptimizationDecisionEngine.PingEvidence>? pings = null,
    bool hasGateway = false,
    IReadOnlyList<NicOptimizationAdvisor.DriverProperty>? properties = null,
    bool nicMatches = false,
    bool physical = false,
    PerformanceBenchmarkSession? abSession = null) =>
    new(
        adapterType,
        true,
        1_000_000_000,
        hasGateway,
        pings ?? new Dictionary<string, OptimizationDecisionEngine.PingEvidence>(),
        nicMatches,
        physical,
        properties ?? Array.Empty<NicOptimizationAdvisor.DriverProperty>(),
        abSession,
        8,
        false);

static Dictionary<string, OptimizationDecisionEngine.PingEvidence> ValidPings() =>
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["Yerel ağ geçidi"] = new(10, 0, 1, 0.5),
        ["Cloudflare"] = new(10, 0, 20, 1)
    };

static NicOptimizationAdvisor.DriverProperty DisabledRss() =>
    new(
        "Receive Side Scaling",
        "Disabled",
        "*RSS",
        "0",
        new[] { "Disabled", "Enabled" },
        new[] { "0", "1" });

static PerformanceBenchmarkSession CreateSession(
    PerformanceBenchmarkRun before,
    PerformanceBenchmarkRun after) =>
    new(
        1,
        "test-session",
        "example.test",
        "192.0.2.1",
        "Test",
        "test-adapter",
        "Test Adapter",
        "Test Adapter",
        "Ethernet",
        1_000_000_000,
        8,
        DateTimeOffset.UtcNow,
        before,
        after);

static PerformanceBenchmarkRun CreateRun(int requested, int success, double median)
{
    var safeRequested = Math.Clamp(requested, 0, 100);
    var safeSuccess = Math.Clamp(success, 0, safeRequested);
    var sampleLatency = double.IsFinite(median) && median >= 0 && median < 1_000_000_000d
        ? (long)median
        : 50L;

    var samples = Enumerable.Range(1, safeRequested)
        .Select(sequence => sequence <= safeSuccess
            ? new PerformancePingSample(sequence, true, sampleLatency, "Success")
            : new PerformancePingSample(sequence, false, null, "Timeout"))
        .ToList();

    var hasSuccess = safeSuccess > 0;
    var now = DateTimeOffset.UtcNow;

    return new PerformanceBenchmarkRun(
        requested,
        success,
        requested > 0 ? (requested - success) * 100d / requested : 0d,
        hasSuccess ? sampleLatency : null,
        hasSuccess ? sampleLatency : null,
        hasSuccess ? median : null,
        hasSuccess ? sampleLatency : null,
        safeSuccess >= 2 ? 0d : null,
        now,
        now,
        samples);
}


static byte[] CreateDnsAResponse(
    byte[] query,
    ushort flags = 0x8180,
    bool includeA = true)
{
    var answerLength = includeA ? 16 : 0;
    var response = new byte[query.Length + answerLength];

    Buffer.BlockCopy(query, 0, response, 0, query.Length);

    BinaryPrimitives.WriteUInt16BigEndian(
        response.AsSpan(2, 2), flags);

    BinaryPrimitives.WriteUInt16BigEndian(
        response.AsSpan(6, 2), (ushort)(includeA ? 1 : 0));

    if (includeA)
    {
        var offset = query.Length;

        response[offset++] = 0xC0;
        response[offset++] = 0x0C;

        BinaryPrimitives.WriteUInt16BigEndian(
            response.AsSpan(offset, 2), 1);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(
            response.AsSpan(offset, 2), 1);
        offset += 2;

        BinaryPrimitives.WriteUInt32BigEndian(
            response.AsSpan(offset, 4), 60);
        offset += 4;

        BinaryPrimitives.WriteUInt16BigEndian(
            response.AsSpan(offset, 2), 4);
        offset += 2;

        response[offset++] = 203;
        response[offset++] = 0;
        response[offset++] = 113;
        response[offset] = 7;
    }

    return response;
}
static byte[] CreateNativeTable(int rowSize)
{
    var table = new byte[sizeof(uint) + rowSize];
    WriteUInt32(table, 0, 1);
    return table;
}

static void WriteUInt32(byte[] data, int offset, uint value)
{
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
}

static void WritePort(byte[] data, int offset, int port)
{
    var encoded = (uint)BinaryPrimitives.ReverseEndianness((ushort)port);
    WriteUInt32(data, offset, encoded);
}

static void WriteAddress(byte[] data, int offset, string address)
{
    var bytes = IPAddress.Parse(address).GetAddressBytes();
    bytes.CopyTo(data, offset);
}

static DnsResolverBenchmarkResult CreateDnsBenchmarkTestResult(
    string provider,
    string profileId,
    string server,
    bool applicationCandidate,
    double baseLatencyMs,
    int successCount)
{
    const int requestedCount = 15;

    var samples = Enumerable.Range(1, requestedCount)
        .Select(index =>
        {
            var success = index <= successCount;
            double? latency = success
                ? baseLatencyMs + ((index % 3) * 0.1d)
                : null;

            return new DnsResolverSample(
                index,
                $"host{index}.example",
                success,
                latency,
                success ? "Success" : "Timeout");
        })
        .ToArray();

    var successful = samples
        .Where(sample => sample.Success && sample.RoundtripMs.HasValue)
        .Select(sample => sample.RoundtripMs!.Value)
        .OrderBy(value => value)
        .ToArray();

    var failures = requestedCount - successful.Length;

    return new DnsResolverBenchmarkResult(
        provider,
        profileId,
        "Test DNS",
        server,
        applicationCandidate,
        "Test profile",
        requestedCount,
        successful.Length,
        failures,
        failures,
        successful.Length == 0 ? null : successful[0],
        successful.Length == 0 ? null : successful.Average(),
        DnsResolverBenchmark.CalculateMedian(successful),
        DnsResolverBenchmark.CalculatePercentile(successful, 0.95d),
        0.1d,
        failures == 0 ? "Yok" : $"Timeout: {failures}",
        samples);
}
static DnsResolverBenchmarkResult CreateDnsBenchmarkTestResultWithLatencies(
    string provider,
    string profileId,
    string server,
    bool applicationCandidate,
    IReadOnlyList<double> latencies)
{
    const int requestedCount = DnsResolverRanking.SamplesPerResolverSession;

    if (latencies is null || latencies.Count != requestedCount)
        throw new ArgumentException("Exactly 15 latency samples are required.", nameof(latencies));

    if (latencies.Any(value => !double.IsFinite(value) || value < 0))
        throw new ArgumentException("Latency samples must be finite and non-negative.", nameof(latencies));

    var samples = Enumerable.Range(1, requestedCount)
        .Select(index =>
            new DnsResolverSample(
                index,
                $"host{index}.example",
                true,
                latencies[index - 1],
                "Success"))
        .ToArray();

    var successful = samples
        .Select(sample => sample.RoundtripMs!.Value)
        .OrderBy(value => value)
        .ToArray();

    var adjacentDifferences = samples
        .Zip(
            samples.Skip(1),
            (first, second) => new { first, second })
        .Where(pair =>
            pair.second.Sequence == pair.first.Sequence + 1 &&
            pair.first.RoundtripMs.HasValue &&
            pair.second.RoundtripMs.HasValue)
        .Select(pair =>
            Math.Abs(pair.second.RoundtripMs!.Value - pair.first.RoundtripMs!.Value))
        .ToArray();

    var jitter = adjacentDifferences.Length == 0
        ? (double?)null
        : adjacentDifferences.Average();

    return new DnsResolverBenchmarkResult(
        provider,
        profileId,
        "Test DNS",
        server,
        applicationCandidate,
        "Test profile",
        requestedCount,
        successful.Length,
        0,
        0,
        successful[0],
        successful.Average(),
        DnsResolverBenchmark.CalculateMedian(successful),
        DnsResolverBenchmark.CalculatePercentile(successful, 0.95d),
        jitter,
        "Yok",
        samples);
}

static void ExpectInvalidData(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}