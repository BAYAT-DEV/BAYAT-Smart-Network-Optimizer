// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.Net;

namespace SmartNetworkOptimizer;

internal sealed record DnsResolverDefinition(
    string Provider,
    string ProfileId,
    string Service,
    string Server,
    bool WasProvidedByUser,
    bool IsApplicationCandidate,
    string ApplicationNotice);

internal static class DnsResolverCatalog
{
    public const int UserProvidedServerCount = 41;
    public const int ExistingBuiltInServerCount = 2;
    public const int ExpectedCount = UserProvidedServerCount + ExistingBuiltInServerCount;

    private static readonly DnsResolverDefinition[] Entries =
    {
        new("Cloudflare", "cloudflare-standard", "Cloudflare Public DNS", "1.1.1.1", false, true, "Genel DNS çözümleme hizmeti."),
        new("Cloudflare", "cloudflare-standard", "Cloudflare Public DNS", "1.0.0.1", false, true, "Genel DNS çözümleme hizmeti."),

        new("Kaynağı doğrulanmamış", "unverified-89", "Hizmet türü doğrulanmamış", "89.238.154.6", true, false, "Test edilir; sağlayıcı ve hizmet politikası doğrulanmadan uygulanmaz."),

        new("Google", "google-public", "Google Public DNS", "8.8.8.8", true, true, "Genel DNS çözümleme hizmeti."),
        new("Google", "google-public", "Google Public DNS", "8.8.4.4", true, true, "Genel DNS çözümleme hizmeti."),

        new("Quad9", "quad9-secure", "Secure - tehdit engelleme", "9.9.9.9", true, true, "Zararlı ve kimlik avı alan adlarını engelleyen profil."),
        new("Quad9", "quad9-secure", "Secure - tehdit engelleme", "149.112.112.112", true, true, "Zararlı ve kimlik avı alan adlarını engelleyen profil."),

        new("Cisco OpenDNS", "opendns-public", "OpenDNS Public", "208.67.222.222", true, true, "OpenDNS genel çözümleme profili."),
        new("Cisco OpenDNS", "opendns-public", "OpenDNS Public", "208.67.220.220", true, true, "OpenDNS genel çözümleme profili."),

        new("Level 3 / Lumen", "lumen-legacy", "Eski genel DNS adresleri", "4.2.2.1", true, false, "Güncel birinci taraf hizmet ve kullanım politikası doğrulanmadan uygulanmaz."),
        new("Level 3 / Lumen", "lumen-legacy", "Eski genel DNS adresleri", "4.2.2.2", true, false, "Güncel birinci taraf hizmet ve kullanım politikası doğrulanmadan uygulanmaz."),
        new("Level 3 / Lumen", "lumen-legacy", "Eski genel DNS adresleri", "4.2.2.3", true, false, "Güncel birinci taraf hizmet ve kullanım politikası doğrulanmadan uygulanmaz."),
        new("Level 3 / Lumen", "lumen-legacy", "Eski genel DNS adresleri", "4.2.2.4", true, false, "Güncel birinci taraf hizmet ve kullanım politikası doğrulanmadan uygulanmaz."),

        new("Comodo", "comodo-secure", "Comodo Secure DNS", "8.26.56.26", true, true, "Var olmayan alan adları için yönlendirme davranışı gösterebilir."),
        new("Comodo", "comodo-secure", "Comodo Secure DNS", "8.20.247.20", true, true, "Var olmayan alan adları için yönlendirme davranışı gösterebilir."),

        new("CleanBrowsing", "cleanbrowsing-security", "Security Filter", "185.228.168.9", true, true, "Kimlik avı, kötü amaçlı yazılım ve zararlı alan adı filtrelemesi."),
        new("CleanBrowsing", "cleanbrowsing-security", "Security Filter", "185.228.169.9", true, true, "Kimlik avı, kötü amaçlı yazılım ve zararlı alan adı filtrelemesi."),

        new("Control D", "controld-unfiltered", "Unfiltered", "76.76.2.0", true, true, "Filtresiz genel çözümleme profili."),
        new("Control D", "controld-unfiltered", "Unfiltered", "76.76.10.0", true, true, "Filtresiz genel çözümleme profili."),

        new("AdGuard", "adguard-default", "Default - filtreleme", "94.140.14.14", true, true, "Reklam, izleyici ve zararlı alan adı filtrelemesi."),
        new("AdGuard", "adguard-default", "Default - filtreleme", "94.140.15.15", true, true, "Reklam, izleyici ve zararlı alan adı filtrelemesi."),

        new("Neustar / UltraDNS", "ultradns-reliability-1", "Reliability and Performance 1", "156.154.70.1", true, false, "Hizmet sınıfı ve güncel uygulama koşulları ayrıca doğrulanmalı."),
        new("Neustar / UltraDNS", "ultradns-reliability-1", "Reliability and Performance 1", "156.154.71.1", true, false, "Hizmet sınıfı ve güncel uygulama koşulları ayrıca doğrulanmalı."),

        new("DNS.WATCH", "dns-watch", "DNS.WATCH Public", "84.200.69.80", true, true, "Filtresiz DNS çözümleme ve DNSSEC desteği."),
        new("DNS.WATCH", "dns-watch", "DNS.WATCH Public", "84.200.70.40", true, true, "Filtresiz DNS çözümleme ve DNSSEC desteği."),

        new("Dyn", "dyn-internet-guide-incomplete", "Internet Guide", "216.146.35.35", true, false, "Resmî eş sunucu 216.146.36.36 listede yok; eksik çift otomatik uygulanmaz."),

        new("Neustar / UltraDNS", "ultradns-reliability-2", "Reliability and Performance 2", "156.154.70.5", true, false, "Hizmet sınıfı ve güncel uygulama koşulları ayrıca doğrulanmalı."),
        new("Neustar / UltraDNS", "ultradns-reliability-2", "Reliability and Performance 2", "156.154.71.5", true, false, "Hizmet sınıfı ve güncel uygulama koşulları ayrıca doğrulanmalı."),

        new("NextDNS", "nextdns-linked-profile", "Profile-bağlantılı IPv4", "45.90.28.232", true, false, "Kullanıcıya ait NextDNS profili ve bağlı IP doğrulanmadan uygulanmaz."),
        new("NextDNS", "nextdns-linked-profile", "Profile-bağlantılı IPv4", "45.90.30.232", true, false, "Kullanıcıya ait NextDNS profili ve bağlı IP doğrulanmadan uygulanmaz."),

        new("NextDNS", "nextdns-standard", "NextDNS IPv4", "45.90.28.0", true, false, "Ağ/profil bağlantısı doğrulanmadan otomatik uygulanmaz."),
        new("NextDNS", "nextdns-standard", "NextDNS IPv4", "45.90.30.0", true, false, "Ağ/profil bağlantısı doğrulanmadan otomatik uygulanmaz."),

        new("AdGuard", "adguard-nonfiltering", "Non-filtering", "94.140.14.140", true, true, "Filtreleme uygulanmayan DNS profili."),
        new("AdGuard", "adguard-nonfiltering", "Non-filtering", "94.140.14.141", true, true, "Filtreleme uygulanmayan DNS profili."),

        new("Türk Telekom", "turktelekom-dns39-40", "DNS39 / DNS40", "195.175.39.39", true, true, "Türk Telekom DNS sunucusu. İçerik ve güvenli internet politikası kullanıcıya açıklanmalı."),
        new("Türk Telekom", "turktelekom-dns39-40", "DNS39 / DNS40", "195.175.39.40", true, true, "Türk Telekom DNS sunucusu. İçerik ve güvenli internet politikası kullanıcıya açıklanmalı."),

        new("Hurricane Electric", "he-single", "Public DNS", "74.82.42.42", true, false, "Listede aynı hizmete ait ikinci doğrulanmış sunucu bulunmadığından tek başına uygulanmaz."),

        new("Verisign", "verisign-public", "Verisign Public DNS", "64.6.64.6", true, true, "Genel DNS çözümleme hizmeti."),
        new("Verisign", "verisign-public", "Verisign Public DNS", "64.6.65.6", true, true, "Genel DNS çözümleme hizmeti."),

        new("Quad9", "quad9-unfiltered", "Unsecured - tehdit engellemesiz", "9.9.9.10", true, true, "Quad9 çözümleme hizmeti; tehdit engelleme listeleri uygulanmaz."),
        new("Quad9", "quad9-unfiltered", "Unsecured - tehdit engellemesiz", "149.112.112.10", true, true, "Quad9 çözümleme hizmeti; tehdit engelleme listeleri uygulanmaz."),

        new("Cisco", "cisco-unknown-role", "UDP DNS yanıtı veren Cisco adresleri", "146.112.41.2", true, false, "UDP/53 yanıtı ölçüldü; hizmet rolü ve politika doğrulanmadan uygulanmaz."),
        new("Cisco", "cisco-unknown-role", "UDP DNS yanıtı veren Cisco adresleri", "146.112.41.3", true, false, "UDP/53 yanıtı ölçüldü; hizmet rolü ve politika doğrulanmadan uygulanmaz.")
    };

    public static IReadOnlyList<DnsResolverDefinition> All { get; } =
        Array.AsReadOnly(Entries);
}