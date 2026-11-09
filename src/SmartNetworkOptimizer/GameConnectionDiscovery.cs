// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace SmartNetworkOptimizer;

/// <summary>
/// Windows IP Helper API kullanarak salt okunur oyun süreci ve bağlantı envanteri.
/// Paket içeriği yakalamaz, süreç belleğini incelemez ve yönetici yetkisi istemez.
/// </summary>
internal static class GameConnectionDiscovery
{
    internal sealed record GameProcessInfo(
        string Profile,
        string ProcessName,
        int ProcessId,
        double WorkingSetMb,
        string ExecutablePath);

    internal sealed record TcpEndpoint(
        int ProcessId,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort,
        string State);

    internal sealed record UdpEndpoint(
        int ProcessId,
        string LocalAddress,
        int LocalPort);

    internal sealed record Snapshot(
        IReadOnlyList<GameProcessInfo> Processes,
        IReadOnlyList<TcpEndpoint> TcpEndpoints,
        IReadOnlyList<UdpEndpoint> UdpEndpoints);

    private const uint AfInet = 2;
    private const uint AfInet6 = 23;

    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;

    private const uint ErrorSuccess = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint ErrorNoData = 232;

    private const int MaxTableBytes = 64 * 1024 * 1024;
    private const int MaxTableRetries = 5;

    private delegate uint NativeTableReader(
        IntPtr table,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        uint addressFamily,
        int tableClass,
        uint reserved);

    public static Task<Snapshot> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => DiscoverCore(cancellationToken),
            cancellationToken);
    }

    private static Snapshot DiscoverCore(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Oyun bağlantı envanteri yalnızca Windows üzerinde kullanılabilir.");

        var processes = Process.GetProcesses();

        try
        {
            var tracked = new List<(Process Handle, GameProcessInfo Info)>();

            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var processName = process.ProcessName;
                    var profile = IdentifyProfile(processName);

                    if (profile is null)
                        continue;

                    // Süreç kimliğini doğrulayamıyorsak PID'yi envantere almayız.
                    // Aynı Process nesnesi tarama bitene kadar açık tutulur.
                    _ = process.StartTime.ToUniversalTime();

                    string executablePath = string.Empty;

                    try
                    {
                        executablePath = process.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        // Bazı korumalı süreçlerde yürütülebilir dosya yolu okunamayabilir.
                    }

                    double memoryMb = 0;

                    try
                    {
                        memoryMb = process.WorkingSet64 / 1024d / 1024d;
                    }
                    catch
                    {
                        // Süreç bellek bilgisi okunamıyorsa yalnızca bu alan boş kalır.
                    }

                    tracked.Add((
                        process,
                        new GameProcessInfo(
                            profile,
                            processName,
                            process.Id,
                            memoryMb,
                            executablePath)));
                }
                catch
                {
                    // Kapanan veya bilgileri okunamayan süreçler atlanır.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (tracked.Count == 0)
            {
                return new Snapshot(
                    Array.Empty<GameProcessInfo>(),
                    Array.Empty<TcpEndpoint>(),
                    Array.Empty<UdpEndpoint>());
            }

            var tcp4 = ParseTcp4Table(
                GetNativeTableBytes(
                    GetExtendedTcpTable,
                    AfInet,
                    TcpTableOwnerPidAll,
                    cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            var tcp6 = ParseTcp6Table(
                GetNativeTableBytes(
                    GetExtendedTcpTable,
                    AfInet6,
                    TcpTableOwnerPidAll,
                    cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            var udp4 = ParseUdp4Table(
                GetNativeTableBytes(
                    GetExtendedUdpTable,
                    AfInet,
                    UdpTableOwnerPid,
                    cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            var udp6 = ParseUdp6Table(
                GetNativeTableBytes(
                    GetExtendedUdpTable,
                    AfInet6,
                    UdpTableOwnerPid,
                    cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();

            // Aynı Process nesnesi üzerinden kontrol yapılır.
            // Eski süreç kapanmışsa PID yeniden kullanılmış olsa bile kayıt dışarıda kalır.
            var alivePids = new HashSet<int>();
            var foundProcesses = new List<GameProcessInfo>();

            foreach (var item in tracked)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (item.Handle.HasExited)
                        continue;

                    alivePids.Add(item.Info.ProcessId);
                    foundProcesses.Add(item.Info);
                }
                catch
                {
                    // Kimliği artık doğrulanamayan süreç sonuçlara alınmaz.
                }
            }

            var tcpEndpoints = tcp4
                .Concat(tcp6)
                .Where(endpoint => alivePids.Contains(endpoint.ProcessId))
                .ToArray();

            var udpEndpoints = udp4
                .Concat(udp6)
                .Where(endpoint => alivePids.Contains(endpoint.ProcessId))
                .ToArray();

            return new Snapshot(
                foundProcesses
                    .OrderBy(process => process.Profile)
                    .ThenBy(process => process.ProcessName)
                    .ToArray(),
                tcpEndpoints,
                udpEndpoints);
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static byte[] GetNativeTableBytes(
        NativeTableReader reader,
        uint addressFamily,
        int tableClass,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        uint size = 0;

        var status = reader(
            IntPtr.Zero,
            ref size,
            false,
            addressFamily,
            tableClass,
            0);

        if (status == ErrorNoData)
            return Array.Empty<byte>();

        if (status != ErrorInsufficientBuffer && status != ErrorSuccess)
            throw new Win32Exception(
                unchecked((int)status),
                "Windows bağlantı tablosu boyutu alınamadı.");

        if (size == 0)
            return Array.Empty<byte>();

        for (var attempt = 0; attempt < MaxTableRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (size < sizeof(uint) || size > MaxTableBytes)
                throw new InvalidDataException(
                    "Windows bağlantı tablosu geçersiz veya beklenenden büyük.");

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));

            try
            {
                uint returnedSize = size;

                status = reader(
                    buffer,
                    ref returnedSize,
                    false,
                    addressFamily,
                    tableClass,
                    0);

                if (status == ErrorNoData)
                    return Array.Empty<byte>();

                if (status == ErrorSuccess)
                {
                    if (returnedSize < sizeof(uint) || returnedSize > size)
                        throw new InvalidDataException(
                            "Windows bağlantı tablosunun döndürdüğü boyut geçersiz.");

                    var result = new byte[checked((int)returnedSize)];
                    Marshal.Copy(buffer, result, 0, result.Length);

                    return result;
                }

                if (status != ErrorInsufficientBuffer)
                    throw new Win32Exception(
                        unchecked((int)status),
                        "Windows bağlantı tablosu okunamadı.");

                if (returnedSize <= size)
                    throw new InvalidDataException(
                        "Windows bağlantı tablosu boyutu güvenli biçimde güncellenemedi.");

                size = returnedSize;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException(
            "Bağlantı tablosu sürekli değiştiği için güvenli biçimde okunamadı.");
    }

    internal static IReadOnlyList<TcpEndpoint> ParseTcp4Table(byte[] table)
    {
        ArgumentNullException.ThrowIfNull(table);

        const int rowSize = 24;
        var count = GetRowCount(table, rowSize);
        var result = new List<TcpEndpoint>(count);

        for (var i = 0; i < count; i++)
        {
            var offset = sizeof(uint) + i * rowSize;

            var state = ReadUInt32(table, offset);

            // Yalnızca Established TCP bağlantıları.
            if (state != 5)
                continue;

            var pid = ReadUInt32(table, offset + 20);

            if (pid == 0 || pid > int.MaxValue)
                continue;

            result.Add(new TcpEndpoint(
                (int)pid,
                ReadIPv4(table, offset + 4),
                DecodePort(ReadUInt32(table, offset + 8)),
                ReadIPv4(table, offset + 12),
                DecodePort(ReadUInt32(table, offset + 16)),
                "Established"));
        }

        return result;
    }

    internal static IReadOnlyList<TcpEndpoint> ParseTcp6Table(byte[] table)
    {
        ArgumentNullException.ThrowIfNull(table);

        const int rowSize = 56;
        var count = GetRowCount(table, rowSize);
        var result = new List<TcpEndpoint>(count);

        for (var i = 0; i < count; i++)
        {
            var offset = sizeof(uint) + i * rowSize;

            var state = ReadUInt32(table, offset + 48);

            if (state != 5)
                continue;

            var pid = ReadUInt32(table, offset + 52);

            if (pid == 0 || pid > int.MaxValue)
                continue;

            result.Add(new TcpEndpoint(
                (int)pid,
                ReadIPv6(
                    table,
                    offset,
                    ReadUInt32(table, offset + 16)),
                DecodePort(ReadUInt32(table, offset + 20)),
                ReadIPv6(
                    table,
                    offset + 24,
                    ReadUInt32(table, offset + 40)),
                DecodePort(ReadUInt32(table, offset + 44)),
                "Established"));
        }

        return result;
    }

    internal static IReadOnlyList<UdpEndpoint> ParseUdp4Table(byte[] table)
    {
        ArgumentNullException.ThrowIfNull(table);

        const int rowSize = 12;
        var count = GetRowCount(table, rowSize);
        var result = new List<UdpEndpoint>(count);

        for (var i = 0; i < count; i++)
        {
            var offset = sizeof(uint) + i * rowSize;
            var pid = ReadUInt32(table, offset + 8);

            if (pid == 0 || pid > int.MaxValue)
                continue;

            result.Add(new UdpEndpoint(
                (int)pid,
                ReadIPv4(table, offset),
                DecodePort(ReadUInt32(table, offset + 4))));
        }

        return result;
    }

    internal static IReadOnlyList<UdpEndpoint> ParseUdp6Table(byte[] table)
    {
        ArgumentNullException.ThrowIfNull(table);

        const int rowSize = 28;
        var count = GetRowCount(table, rowSize);
        var result = new List<UdpEndpoint>(count);

        for (var i = 0; i < count; i++)
        {
            var offset = sizeof(uint) + i * rowSize;
            var pid = ReadUInt32(table, offset + 24);

            if (pid == 0 || pid > int.MaxValue)
                continue;

            result.Add(new UdpEndpoint(
                (int)pid,
                ReadIPv6(
                    table,
                    offset,
                    ReadUInt32(table, offset + 16)),
                DecodePort(ReadUInt32(table, offset + 20))));
        }

        return result;
    }

    private static int GetRowCount(byte[] table, int rowSize)
    {
        if (table.Length == 0)
            return 0;

        if (table.Length < sizeof(uint))
            throw new InvalidDataException("Bağlantı tablosu başlığı eksik.");

        var count = ReadUInt32(table, 0);
        var availableRows = (table.Length - sizeof(uint)) / rowSize;

        if (count > availableRows)
            throw new InvalidDataException(
                "Bağlantı tablosundaki kayıt sayısı ayrılan veri boyutunu aşıyor.");

        return checked((int)count);
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - sizeof(uint))
            throw new InvalidDataException("Bağlantı kaydı sınırların dışında.");

        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.AsSpan(offset, sizeof(uint)));
    }

    private static int DecodePort(uint networkOrderPort)
    {
        return BinaryPrimitives.ReverseEndianness(
            (ushort)(networkOrderPort & 0xFFFF));
    }

    private static uint DecodeNetworkUInt32(uint value)
    {
        return BinaryPrimitives.ReverseEndianness(value);
    }

    private static string ReadIPv4(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4)
            throw new InvalidDataException("IPv4 adres alanı geçersiz.");

        return new IPAddress(data.AsSpan(offset, 4)).ToString();
    }

    private static string ReadIPv6(byte[] data, int offset, uint scopeId)
    {
        if (offset < 0 || offset > data.Length - 16)
            throw new InvalidDataException("IPv6 adres alanı geçersiz.");

        return new IPAddress(
            data.AsSpan(offset, 16),
            scopeId).ToString();
    }

    private static string? IdentifyProfile(string processName)
    {
        if (processName.Equals(
                "VALORANT-Win64-Shipping",
                StringComparison.OrdinalIgnoreCase) ||
            processName.Equals(
                "VALORANT",
                StringComparison.OrdinalIgnoreCase))
            return "Valorant";

        if (processName.Equals(
                "cs2",
                StringComparison.OrdinalIgnoreCase))
            return "Counter-Strike 2";

        if (processName.Equals(
                "TslGame",
                StringComparison.OrdinalIgnoreCase))
            return "PUBG";

        if (processName.StartsWith(
                "FortniteClient-Win64-Shipping",
                StringComparison.OrdinalIgnoreCase))
            return "Fortnite";

        return null;
    }
}