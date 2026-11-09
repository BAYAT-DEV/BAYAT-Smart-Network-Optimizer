// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.Text;

namespace SmartNetworkOptimizer;

/// <summary>One driver property changed by the application, with its exact before/after values.</summary>
internal sealed record NicPropertyBackup(
    string RegistryKeyword,
    string DisplayName,
    string OriginalDisplayValue,
    string OriginalRegistryValue,
    string TargetDisplayValue,
    string TargetRegistryValue);

/// <summary>Persistent rollback point for a single adapter. v0.7 intentionally changes one property per profile.</summary>
internal sealed record NicSettingsBackup(
    int SchemaVersion,
    string InterfaceGuid,
    string InterfaceName,
    string InterfaceDescription,
    DateTimeOffset SavedAt,
    List<NicPropertyBackup> Properties);

internal static class NicSettingsWorkflow
{
    public static string BuildSnapshotScript(string adapterName)
    {
        var name = Quote(adapterName);
        return "$ErrorActionPreference = 'Stop'\n" +
               "$ProgressPreference = 'SilentlyContinue'\n" +
               "Import-Module NetAdapter -ErrorAction Stop\n" +
               "$adapter = Get-NetAdapter -Name " + name + " -ErrorAction Stop | Select-Object -First 1 Name, InterfaceGuid, InterfaceDescription, Status, LinkSpeed, MediaType\n" +
               "$physical = @(Get-NetAdapter -Name " + name + " -Physical -ErrorAction SilentlyContinue)\n" +
               "$adapter | Add-Member -NotePropertyName IsPhysical -NotePropertyValue ($physical.Count -gt 0) -Force\n" +
               "$properties = @(Get-NetAdapterAdvancedProperty -Name " + name + " -AllProperties -ErrorAction Stop | Select-Object DisplayName, DisplayValue, RegistryKeyword, RegistryValue, ValidDisplayValues, ValidRegistryValues)\n" +
               "[pscustomobject]@{ Adapter = $adapter; Properties = $properties } | ConvertTo-Json -Depth 7 -Compress\n";
    }

    public static string BuildApplyScript(NicSettingsBackup backup)
    {
        ValidateBackup(backup);
        var property = backup.Properties.Single();
        var script = AdapterLookup(backup.InterfaceGuid);
        script.AppendLine("$property = Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword " + Quote(property.RegistryKeyword) + " -AllProperties -ErrorAction Stop | Select-Object -First 1");
        script.AppendLine("if ($null -eq $property) { throw 'Yedeklenecek NIC özelliği artık bulunamıyor.' }");
        script.AppendLine("if ([string]$property.RegistryValue -ne " + Quote(property.OriginalRegistryValue) + ") { throw 'NIC özelliği analizden sonra değişmiş. Güvenlik için işlem durduruldu; yeniden analiz edin.' }");
        script.AppendLine("$valid = @($property.ValidRegistryValues | ForEach-Object { [string]$_ })");
        script.AppendLine("if ($valid -notcontains " + Quote(property.TargetRegistryValue) + ") { throw 'Hedef değer sürücünün geçerli değerleri arasında değil.' }");
        script.AppendLine("Set-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword " + Quote(property.RegistryKeyword) + " -RegistryValue " + Quote(property.TargetRegistryValue) + " -NoRestart -Confirm:$false -ErrorAction Stop | Out-Null");
        script.AppendLine("$after = Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword " + Quote(property.RegistryKeyword) + " -AllProperties -ErrorAction Stop | Select-Object -First 1");
        script.AppendLine("if ($null -eq $after -or [string]$after.RegistryValue -ne " + Quote(property.TargetRegistryValue) + ") { throw 'Sürücü yeni değeri doğrulamadı.' }");
        script.AppendLine("exit 0");
        return script.ToString();
    }

    public static string BuildRestoreScript(NicSettingsBackup backup, bool requireTargetCurrent = true)
    {
        ValidateBackup(backup);
        var script = AdapterLookup(backup.InterfaceGuid);
        foreach (var property in backup.Properties)
        {
            script.AppendLine("$property = Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword " + Quote(property.RegistryKeyword) + " -AllProperties -ErrorAction Stop | Select-Object -First 1");
            script.AppendLine("if ($null -eq $property) { throw 'Geri yüklenecek NIC özelliği bulunamadı: " + EscapePowerShellMessage(property.DisplayName) + "' }");
            if (requireTargetCurrent)
                script.AppendLine("if ([string]$property.RegistryValue -ne " + Quote(property.TargetRegistryValue) + ") { throw 'NIC özelliği uygulamadan sonra dışarıdan değiştirilmiş. Yedek korunuyor; üzerine yazılmadı.' }");
            script.AppendLine("Set-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword " + Quote(property.RegistryKeyword) + " -RegistryValue " + Quote(property.OriginalRegistryValue) + " -NoRestart -Confirm:$false -ErrorAction Stop | Out-Null");
            script.AppendLine("$restored = Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword " + Quote(property.RegistryKeyword) + " -AllProperties -ErrorAction Stop | Select-Object -First 1");
            script.AppendLine("if ($null -eq $restored -or [string]$restored.RegistryValue -ne " + Quote(property.OriginalRegistryValue) + ") { throw 'Önceki NIC değeri doğrulanamadı: " + EscapePowerShellMessage(property.DisplayName) + "' }");
        }
        script.AppendLine("exit 0");
        return script.ToString();
    }

    private static StringBuilder AdapterLookup(string interfaceGuid)
    {
        if (!Guid.TryParse(interfaceGuid, out var guid))
            throw new InvalidOperationException("Ağ bağdaştırıcısının benzersiz GUID bilgisi geçerli değil.");
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("Import-Module NetAdapter -ErrorAction Stop");
        script.AppendLine("$targetGuid = [guid]" + Quote(guid.ToString("D")));
        script.AppendLine("$adapter = Get-NetAdapter -IncludeHidden -ErrorAction Stop | Where-Object { $_.InterfaceGuid -eq $targetGuid } | Select-Object -First 1");
        script.AppendLine("if ($null -eq $adapter) { throw 'Yedek alınan ağ bağdaştırıcısı bulunamadı. Yedek dosyası korundu.' }");
        return script;
    }

    public static void ValidateBackup(NicSettingsBackup backup)
    {
        if (backup.SchemaVersion != 1 || !Guid.TryParse(backup.InterfaceGuid, out _))
            throw new InvalidOperationException("NIC yedek dosyasının sürümü veya bağdaştırıcı kimliği geçersiz.");
        if (backup.Properties is null || backup.Properties.Count != 1)
            throw new InvalidOperationException("v0.7 NIC yedeği tam olarak bir hedef özellik içermelidir.");
        foreach (var property in backup.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.RegistryKeyword) ||
                string.IsNullOrWhiteSpace(property.OriginalRegistryValue) ||
                string.IsNullOrWhiteSpace(property.TargetRegistryValue))
                throw new InvalidOperationException("NIC yedeğinde gerekli RegistryKeyword/RegistryValue bulunmuyor.");
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string EscapePowerShellMessage(string value) => value.Replace("'", "''", StringComparison.Ordinal).Replace("\r", " ").Replace("\n", " ");
}
