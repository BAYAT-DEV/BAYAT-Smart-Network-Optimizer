// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using Microsoft.Win32;

namespace SmartNetworkOptimizer;

/// <summary>
/// Provides transparent platform support notices. This class never blocks startup
/// and does not modify Windows configuration.
/// </summary>
internal static class PlatformCompatibility
{
    public static string? GetWindows10Notice()
    {
        if (!OperatingSystem.IsWindows())
            return "Bu uygulama yalnızca Windows üzerinde çalışmak üzere tasarlanmıştır.";

        var version = Environment.OSVersion.Version;
        // Windows 11 reports version 10.0 with build number 22000 or later.
        if (version.Major != 10 || version.Build >= 22000)
            return null;

        string editionId = string.Empty;
        string productName = string.Empty;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            editionId = key?.GetValue("EditionID") as string ?? string.Empty;
            productName = key?.GetValue("ProductName") as string ?? string.Empty;
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (System.IO.IOException) { }

        var edition = (editionId + " " + productName).Trim();
        var enterpriseOrLtsc = edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("LTSC", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("IoT", StringComparison.OrdinalIgnoreCase);

        if (enterpriseOrLtsc)
        {
            return "Windows 10 Enterprise/LTSC algılandı. .NET 10 uyumluluğu, tam Windows sürümünün Microsoft yaşam döngüsüne bağlıdır; bu cihazda ayrıca doğrulanmalıdır.";
        }

        var knownHomeOrPro = edition.Contains("Home", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Core", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Professional", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Pro", StringComparison.OrdinalIgnoreCase);

        if (knownHomeOrPro)
        {
            return "Windows 10 Home/Pro için Microsoft desteği Ekim 2025'te sona erdi. Modern .NET 10'un Windows 10 desteği LTSC/Enterprise sürümleriyle sınırlıdır. Uygulama bazı sistemlerde çalışabilse de bu kombinasyon resmi destekli veya test edilmiş olarak sunulamaz. Güvenlik açısından desteklenen Windows sürümüne geçmeniz önerilir.";
        }

        return "Windows 10 algılandı ancak sürüm/edition bilgisi kesin belirlenemedi. .NET 10'un resmi Windows 10 desteği LTSC/Enterprise sürümleriyle sınırlıdır; bu sistemde çalışma garantisi verilmez.";
    }
}
