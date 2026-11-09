// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.
using System.Buffers.Binary;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SmartNetworkOptimizer;

internal sealed record DnsResolverBenchmarkResult(
    string Provider,
    string ProfileId,
    string Service,
    string Server,
    bool IsApplicationCandidate,
    string ApplicationNotice,
    int RequestedCount,
    int SuccessCount,
    int TimeoutCount,
    int FailureCount,
    double? MinimumMs,
    double? AverageMs,
    double? MedianMs,
    double? P95Ms,
    double? JitterMs,
    string FailureSummary,
    IReadOnlyList<DnsResolverSample> Samples);

internal sealed record DnsResolverSample(
    int Sequence,
    string Domain,
    bool Success,
    double? RoundtripMs,
    string Status);

internal static class DnsResolverBenchmark
{
    private const int RepetitionsPerDomain = 3;
    private const int QueryTimeoutMilliseconds = 1200;
    private const int MaximumConcurrentResolvers = 6;
    private const int MaximumDnsMessageLength = 4096;

    private static readonly string[] TestDomains =
    {
        "www.microsoft.com",
        "www.wikipedia.org",
        "www.mozilla.org",
        "www.github.com",
        "www.cloudflare.com"
    };

    internal static async Task<IReadOnlyList<DnsResolverBenchmarkResult>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = DnsResolverCatalog.All;

        using var semaphore = new SemaphoreSlim(MaximumConcurrentResolvers);

        var tasks = definitions.Select(async resolver =>
        {
            await semaphore.WaitAsync(cancellationToken);

            try
            {
                return await MeasureResolverAsync(resolver, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        return await Task.WhenAll(tasks);
    }

    private static async Task<DnsResolverBenchmarkResult> MeasureResolverAsync(
        DnsResolverDefinition resolver,
        CancellationToken cancellationToken)
    {
        var samples = new List<DnsResolverSample>(
            TestDomains.Length * RepetitionsPerDomain);

        var domainOrder = TestDomains
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        var sequence = 0;

        for (var repetition = 0; repetition < RepetitionsPerDomain; repetition++)
        {
            foreach (var domain in domainOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();

                sequence++;

                var sample = await MeasureQueryAsync(
                    resolver.Server,
                    domain,
                    sequence,
                    cancellationToken);

                samples.Add(sample);

                if (sequence < TestDomains.Length * RepetitionsPerDomain)
                    await Task.Delay(35, cancellationToken);
            }
        }

        var successful = samples
            .Where(s => s.Success && s.RoundtripMs.HasValue)
            .Select(s => s.RoundtripMs!.Value)
            .OrderBy(value => value)
            .ToArray();

        var minimum = successful.Length == 0
            ? (double?)null
            : successful[0];

        var average = successful.Length == 0
            ? (double?)null
            : successful.Average();

        var median = CalculateMedian(successful);
        var p95 = CalculatePercentile(successful, 0.95);

        var adjacentDifferences = samples
            .Zip(samples.Skip(1), (first, second) => new { first, second })
            .Where(pair =>
                pair.second.Sequence == pair.first.Sequence + 1 &&
                pair.first.Success &&
                pair.second.Success &&
                pair.first.RoundtripMs.HasValue &&
                pair.second.RoundtripMs.HasValue)
            .Select(pair =>
                Math.Abs(pair.second.RoundtripMs!.Value - pair.first.RoundtripMs!.Value))
            .ToArray();

        var jitter = adjacentDifferences.Length == 0
            ? (double?)null
            : adjacentDifferences.Average();

        var failures = samples.Where(s => !s.Success).ToArray();

        var failureSummary = failures.Length == 0
            ? "Yok"
            : string.Join(", ",
                failures
                    .GroupBy(s => s.Status)
                    .OrderByDescending(group => group.Count())
                    .Take(5)
                    .Select(group => $"{group.Key}: {group.Count()}"));

        var successCount = successful.Length;

        return new DnsResolverBenchmarkResult(
            resolver.Provider,
            resolver.ProfileId,
            resolver.Service,
            resolver.Server,
            resolver.IsApplicationCandidate,
            resolver.ApplicationNotice,
            samples.Count,
            successCount,
            failures.Count(s => s.Status == "Timeout"),
            failures.Length,
            minimum,
            average,
            median,
            p95,
            jitter,
            failureSummary,
            samples.AsReadOnly());
    }

    internal static double? CalculateMedian(IEnumerable<double> values)
    {
        var sorted = values
            .Where(value => double.IsFinite(value) && value >= 0)
            .OrderBy(value => value)
            .ToArray();

        if (sorted.Length == 0)
            return null;

        var middle = sorted.Length / 2;

        if (sorted.Length % 2 == 1)
            return sorted[middle];

        return sorted[middle - 1] +
               ((sorted[middle] - sorted[middle - 1]) / 2d);
    }

    internal static double? CalculatePercentile(
        double[] sorted,
        double percentile)
    {
        if (sorted is null)
            throw new ArgumentNullException(nameof(sorted));

        if (!double.IsFinite(percentile) ||
            percentile < 0 ||
            percentile > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        var valid = sorted
            .Where(value => double.IsFinite(value) && value >= 0)
            .OrderBy(value => value)
            .ToArray();

        if (valid.Length == 0)
            return null;

        var index = Math.Clamp(
            (int)Math.Ceiling(percentile * valid.Length) - 1,
            0,
            valid.Length - 1);

        return valid[index];
    }
    private static async Task<DnsResolverSample> MeasureQueryAsync(
        string serverText,
        string domain,
        int sequence,
        CancellationToken cancellationToken)
    {
        var server = IPAddress.Parse(serverText);

        var transactionId = (ushort)RandomNumberGenerator.GetInt32(0, 65536);
        var query = BuildQuery(domain, transactionId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeout.CancelAfter(QueryTimeoutMilliseconds);

        var watch = Stopwatch.StartNew();

        try
        {
            byte[] response;

            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Connect(server, 53);

                await udp.SendAsync(query.AsMemory(), timeout.Token);

                var received = await udp.ReceiveAsync(timeout.Token);
                response = received.Buffer;
            }

            if (IsTruncated(response))
            {
                response = await QueryTcpAsync(
                    server,
                    query,
                    timeout.Token);
            }

            watch.Stop();

            if (!TryValidateResponse(response, transactionId, domain, out var status))
            {
                return new DnsResolverSample(
                    sequence,
                    domain,
                    false,
                    null,
                    status);
            }

            return new DnsResolverSample(
                sequence,
                domain,
                true,
                Math.Round(watch.Elapsed.TotalMilliseconds, 2),
                "Success");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            watch.Stop();

            return new DnsResolverSample(
                sequence, domain, false, null, "Timeout");
        }
        catch (SocketException ex)
        {
            watch.Stop();

            return new DnsResolverSample(
                sequence,
                domain,
                false,
                null,
                "Socket-" + ex.SocketErrorCode);
        }
        catch (IOException)
        {
            watch.Stop();

            return new DnsResolverSample(
                sequence, domain, false, null, "NetworkIO");
        }
        catch (Exception ex)
        {
            watch.Stop();

            return new DnsResolverSample(
                sequence,
                domain,
                false,
                null,
                "Error-" + ex.GetType().Name);
        }
    }

    internal static byte[] BuildQuery(string domain, ushort transactionId)
    {
        if (string.IsNullOrWhiteSpace(domain) ||
            domain.Length > 253 ||
            domain.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("DNS sorgu alan adı geçersiz.", nameof(domain));
        }

        string asciiDomain;

        try
        {
            asciiDomain = new IdnMapping().GetAscii(domain.Trim().TrimEnd('.'));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("DNS sorgu alan adı geçersiz.", nameof(domain));
        }

        var labels = asciiDomain.Split('.');

        if (labels.Length == 0 ||
            labels.Any(label =>
                label.Length is < 1 or > 63 ||
                label.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) ||
                      character == '-')) ||
                label[0] == '-' ||
                label[^1] == '-'))
        {
            throw new ArgumentException("DNS sorgu alan adı geçersiz.", nameof(domain));
        }

        var query = new List<byte>(512);
        var header = new byte[12];

        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(0, 2), transactionId);

        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(2, 2), 0x0100);

        BinaryPrimitives.WriteUInt16BigEndian(
            header.AsSpan(4, 2), 1);

        query.AddRange(header);

        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            query.Add((byte)bytes.Length);
            query.AddRange(bytes);
        }

        query.Add(0);

        query.AddRange(new byte[]
        {
            0x00, 0x01,
            0x00, 0x01
        });

        return query.ToArray();
    }

    private static bool IsTruncated(byte[] response)
    {
        if (response.Length < 4)
            return false;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(2, 2));

        return (flags & 0x0200) != 0;
    }

    private static async Task<byte[]> QueryTcpAsync(
        IPAddress server,
        byte[] query,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);

        await client.ConnectAsync(server, 53, cancellationToken);

        using var stream = client.GetStream();

        var lengthPrefix = new byte[2];

        BinaryPrimitives.WriteUInt16BigEndian(
            lengthPrefix.AsSpan(),
            (ushort)query.Length);

        await stream.WriteAsync(lengthPrefix.AsMemory(), cancellationToken);
        await stream.WriteAsync(query.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);

        await stream.ReadExactlyAsync(
            lengthPrefix.AsMemory(),
            cancellationToken);

        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(
            lengthPrefix.AsSpan());

        if (responseLength is < 12 or > MaximumDnsMessageLength)
            throw new InvalidDataException("TCP DNS yanıt uzunluğu geçersiz.");

        var response = new byte[responseLength];

        await stream.ReadExactlyAsync(
            response.AsMemory(),
            cancellationToken);

        return response;
    }

    internal static bool TryValidateResponse(
        byte[] response,
        ushort expectedTransactionId,
        string expectedDomain,
        out string status)
    {
        status = "InvalidPacket";

        if (response is null || response.Length < 12 ||
            response.Length > MaximumDnsMessageLength)
        {
            return false;
        }

        var transactionId = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(0, 2));

        if (transactionId != expectedTransactionId)
        {
            status = "TransactionIdMismatch";
            return false;
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(2, 2));

        if ((flags & 0x8000) == 0)
        {
            status = "NotResponse";
            return false;
        }

        if ((flags & 0x7800) != 0)
        {
            status = "UnsupportedOpcode";
            return false;
        }

        if ((flags & 0x0200) != 0)
        {
            status = "Truncated";
            return false;
        }

        var responseCode = flags & 0x000F;

        if (responseCode != 0)
        {
            status = responseCode switch
            {
                2 => "SERVFAIL",
                3 => "NXDOMAIN",
                5 => "REFUSED",
                _ => "DNS-RCODE-" + responseCode.ToString(CultureInfo.InvariantCulture)
            };

            return false;
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(4, 2));

        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(6, 2));

        if (questionCount != 1)
        {
            status = "InvalidQuestionCount";
            return false;
        }

        var offset = 12;

        if (!TryReadName(response, ref offset, out var questionName) ||
            offset + 4 > response.Length)
        {
            status = "MalformedQuestion";
            return false;
        }

        string expectedAscii;

        try
        {
            expectedAscii = new IdnMapping()
                .GetAscii(expectedDomain.Trim().TrimEnd('.'));
        }
        catch (ArgumentException)
        {
            status = "InvalidExpectedDomain";
            return false;
        }

        if (!questionName.Equals(
                expectedAscii,
                StringComparison.OrdinalIgnoreCase))
        {
            status = "QuestionNameMismatch";
            return false;
        }

        var questionType = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(offset, 2));

        var questionClass = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(offset + 2, 2));

        offset += 4;

        if (questionType != 1 || questionClass != 1)
        {
            status = "UnexpectedQuestionType";
            return false;
        }

        var hasARecord = false;

        for (var i = 0; i < answerCount; i++)
        {
            if (!TryReadName(response, ref offset, out _) ||
                offset + 10 > response.Length)
            {
                status = "MalformedAnswer";
                return false;
            }

            var recordType = BinaryPrimitives.ReadUInt16BigEndian(
                response.AsSpan(offset, 2));

            var recordClass = BinaryPrimitives.ReadUInt16BigEndian(
                response.AsSpan(offset + 2, 2));

            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(
                response.AsSpan(offset + 8, 2));

            offset += 10;

            if (offset + dataLength > response.Length)
            {
                status = "MalformedAnswerData";
                return false;
            }

            if (recordType == 1 && recordClass == 1 && dataLength == 4)
                hasARecord = true;

            offset += dataLength;
        }

        if (!hasARecord)
        {
            status = "NoAAnswer";
            return false;
        }

        status = "Success";
        return true;
    }

    private static bool TryReadName(
        byte[] message,
        ref int offset,
        out string name)
    {
        name = string.Empty;

        if (offset < 0 || offset >= message.Length)
            return false;

        var labels = new List<string>();
        var cursor = offset;
        var jumped = false;
        var steps = 0;

        while (cursor < message.Length && steps++ < 128)
        {
            var length = message[cursor];

            if (length == 0)
            {
                if (!jumped)
                    offset = cursor + 1;

                name = string.Join(".", labels);
                return true;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor + 1 >= message.Length)
                    return false;

                var pointer = ((length & 0x3F) << 8) |
                    message[cursor + 1];

                if (pointer >= message.Length)
                    return false;

                if (!jumped)
                    offset = cursor + 2;

                cursor = pointer;
                jumped = true;
                continue;
            }

            if ((length & 0xC0) != 0 ||
                length > 63 ||
                cursor + 1 + length > message.Length)
            {
                return false;
            }

            var label = Encoding.ASCII.GetString(
                message,
                cursor + 1,
                length);

            if (label.Length == 0)
                return false;

            labels.Add(label);
            cursor += length + 1;

            if (!jumped)
                offset = cursor;
        }

        return false;
    }
}