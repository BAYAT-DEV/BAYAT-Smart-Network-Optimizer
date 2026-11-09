// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartNetworkOptimizer;

internal sealed record DnsResolverHistoryDocument(
    int SchemaVersion,
    string NetworkFingerprint,
    List<DnsResolverBenchmarkSession> Sessions);

internal static class DnsResolverHistoryStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    internal static string CreateNetworkFingerprint(params string[] components)
    {
        if (components is null || components.Length == 0)
            throw new ArgumentException("Ağ kimliği bileşenleri eksik.", nameof(components));

        var normalized = string.Join(
            "\n",
            components.Select(component => component?.Trim() ?? string.Empty));

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Ağ kimliği boş olamaz.", nameof(components));

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static string GetHistoryPath(string appDataDirectory, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(appDataDirectory))
            throw new ArgumentException("Uygulama veri klasörü boş.", nameof(appDataDirectory));

        ValidateFingerprint(fingerprint);

        return Path.Combine(
            appDataDirectory,
            "dns-benchmark-history-" + fingerprint + ".json");
    }

    internal static DnsResolverHistoryDocument Load(
        string path,
        string expectedFingerprint)
    {
        ValidateFingerprint(expectedFingerprint);

        if (!File.Exists(path))
        {
            return new DnsResolverHistoryDocument(
                CurrentSchemaVersion,
                expectedFingerprint,
                new List<DnsResolverBenchmarkSession>());
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var document = JsonSerializer.Deserialize<DnsResolverHistoryDocument>(
            stream,
            JsonOptions);

        if (document is null)
            throw new InvalidDataException("DNS geçmiş dosyası okunamadı.");

        ValidateDocument(document, expectedFingerprint);
        return document;
    }

    internal static void Save(
        string path,
        DnsResolverHistoryDocument document)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        ValidateDocument(document, document.NetworkFingerprint);

        var directory = Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidDataException("DNS geçmiş klasörü belirlenemedi.");

        Directory.CreateDirectory(directory);

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
            JsonSerializer.Serialize(stream, document, JsonOptions);
            stream.Flush(true);
        }

        if (File.Exists(path))
        {
            var backupDirectory = Path.Combine(directory, "safety-backups");
            Directory.CreateDirectory(backupDirectory);

            var backupName =
                "dns-benchmark-history-" +
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
                "-" +
                Guid.NewGuid().ToString("N") +
                ".json.bak";

            var backupPath = Path.Combine(backupDirectory, backupName);

            // Önce mevcut geçmişin ayrı ve kalıcı yedeği alınır.
            File.Copy(path, backupPath, overwrite: false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    internal static void ValidateDocument(
        DnsResolverHistoryDocument document,
        string expectedFingerprint)
    {
        ValidateFingerprint(expectedFingerprint);

        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException("DNS geçmişinin şema sürümü desteklenmiyor.");

        if (!string.Equals(
            document.NetworkFingerprint,
            expectedFingerprint,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("DNS geçmişi farklı bir ağ kimliğine ait.");
        }

        if (document.Sessions is null ||
            document.Sessions.Count > DnsResolverRanking.MaximumStoredSessions)
        {
            throw new InvalidDataException("DNS geçmiş oturum sayısı geçersiz.");
        }

        var catalog = DnsResolverCatalog.All.ToDictionary(
            entry => entry.Server,
            StringComparer.Ordinal);

        foreach (var session in document.Sessions)
        {
            if (session is null ||
                session.CompletedAt == default ||
                session.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(10) ||
                session.Results is null ||
                session.Results.Count != DnsResolverCatalog.ExpectedCount)
            {
                throw new InvalidDataException("DNS geçmişinde geçersiz oturum bulundu.");
            }

            var seenServers = new HashSet<string>(StringComparer.Ordinal);

            foreach (var result in session.Results)
            {
                if (result is null ||
                    string.IsNullOrWhiteSpace(result.Server) ||
                    !catalog.TryGetValue(result.Server, out var definition) ||
                    !seenServers.Add(result.Server))
                {
                    throw new InvalidDataException("DNS geçmişinde bilinmeyen veya yinelenen sunucu var.");
                }

                if (result.Provider != definition.Provider ||
                    result.ProfileId != definition.ProfileId ||
                    result.Service != definition.Service ||
                    result.IsApplicationCandidate != definition.IsApplicationCandidate ||
                    result.ApplicationNotice != definition.ApplicationNotice)
                {
                    throw new InvalidDataException("DNS geçmişindeki katalog bilgileri uyuşmuyor.");
                }

                if (result.RequestedCount != DnsResolverRanking.SamplesPerResolverSession ||
                    result.Samples is null ||
                    result.Samples.Count != result.RequestedCount)
                {
                    throw new InvalidDataException("DNS geçmişindeki sorgu örneği sayısı geçersiz.");
                }

                // Bozuk JSON içindeki null örnekleri sıralamadan önce reddet.
                if (result.Samples.Any(sample => sample is null))
                    throw new InvalidDataException("DNS geçmişinde boş sorgu örneği var.");

                var ordered = result.Samples
                    .OrderBy(sample => sample.Sequence)
                    .ToArray();

                if (!ordered.Select(sample => sample.Sequence)
                    .SequenceEqual(Enumerable.Range(1, result.RequestedCount)))
                {
                    throw new InvalidDataException("DNS geçmişindeki sorgu sıra numaraları geçersiz.");
                }

                var successCount = 0;
                var timeoutCount = 0;
                var failureCount = 0;

                foreach (var sample in ordered)
                {
                    if (string.IsNullOrWhiteSpace(sample.Domain) ||
                        sample.Domain.Length > 253 ||
                        sample.Domain.Any(char.IsWhiteSpace) ||
                        string.IsNullOrWhiteSpace(sample.Status) ||
                        sample.Status.Length > 128)
                    {
                        throw new InvalidDataException("DNS geçmişinde geçersiz sorgu örneği var.");
                    }

                    if (sample.Success)
                    {
                        if (sample.Status != "Success" ||
                            !sample.RoundtripMs.HasValue ||
                            !double.IsFinite(sample.RoundtripMs.Value) ||
                            sample.RoundtripMs.Value < 0)
                        {
                            throw new InvalidDataException("DNS geçmişinde geçersiz başarılı sorgu var.");
                        }

                        successCount++;
                    }
                    else
                    {
                        if (sample.RoundtripMs.HasValue)
                            throw new InvalidDataException("Başarısız DNS sorgusunda gecikme değeri bulundu.");

                        failureCount++;

                        if (sample.Status == "Timeout")
                            timeoutCount++;
                    }
                }

                if (result.SuccessCount != successCount ||
                    result.FailureCount != failureCount ||
                    result.TimeoutCount != timeoutCount ||
                    result.SuccessCount + result.FailureCount != result.RequestedCount)
                {
                    throw new InvalidDataException("DNS geçmişindeki başarı/hata sayaçları uyuşmuyor.");
                }
            }

            if (seenServers.Count != DnsResolverCatalog.ExpectedCount)
                throw new InvalidDataException("DNS geçmişinde katalogdaki tüm adresler bulunmuyor.");
        }
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) ||
            fingerprint.Length != 64 ||
            fingerprint.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("DNS ağ parmak izi biçimi geçersiz.");
        }
    }
}