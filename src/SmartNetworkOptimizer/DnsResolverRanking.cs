// Copyright (C) 2026 BAYAT
// SPDX-License-Identifier: GPL-3.0-only
// See LICENSE.txt for the full license text.

namespace SmartNetworkOptimizer;

internal sealed record DnsResolverBenchmarkSession(
    DateTimeOffset CompletedAt,
    IReadOnlyList<DnsResolverBenchmarkResult> Results);

internal sealed record DnsResolverProfileDefinition(
    string ProfileId,
    string Provider,
    string[] Ipv4Servers);

internal sealed record DnsResolverAggregateResult(
    string Provider,
    string ProfileId,
    string Server,
    bool IsApplicationCandidate,
    string ApplicationNotice,
    int SessionCount,
    int PassingSessionCount,
    int RequestedCount,
    int SuccessCount,
    double? MinimumMs,
    double? AverageMs,
    double? MedianMs,
    double? P95Ms,
    double? JitterMs,
    string FailureSummary)
{
    public double SuccessRate =>
        RequestedCount <= 0 ? 0d : SuccessCount / (double)RequestedCount;
}

internal sealed record DnsProfileRanking(
    string ProfileId,
    string Provider,
    double MedianAverageMs,
    double P95AverageMs,
    double JitterAverageMs,
    double WorstServerP95Ms,
    double WorstServerJitterMs,
    int SuccessCount,
    int RequestedCount,
    int SessionCount,
    int PassingSessionCount)
{
    public double SuccessRate =>
        RequestedCount <= 0 ? 0d : SuccessCount / (double)RequestedCount;
}

internal static class DnsResolverRanking
{
    public const int MinimumTrustedSessionCount = 3;
    public const int MaximumStoredSessions = 10;
    public const int SamplesPerResolverSession = 15;

    private const double MinimumAggregateSuccessRate = 0.90d;
    private const double MinimumPerSessionSuccessRate = 0.80d;

    internal static IReadOnlyList<DnsResolverAggregateResult> AggregateSessions(
        IEnumerable<DnsResolverBenchmarkSession> sessions)
    {
        if (sessions is null)
            throw new ArgumentNullException(nameof(sessions));

        var sessionArray = sessions
            .TakeLast(MaximumStoredSessions)
            .ToArray();

        var rows = new List<(int SessionIndex, DnsResolverBenchmarkResult Result)>();

        for (var sessionIndex = 0; sessionIndex < sessionArray.Length; sessionIndex++)
        {
            var session = sessionArray[sessionIndex];

            if (session.Results is null)
                continue;

            foreach (var result in session.Results)
                rows.Add((sessionIndex, result));
        }

        var aggregates = new List<DnsResolverAggregateResult>();

        foreach (var group in rows.GroupBy(row => new
        {
            row.Result.ProfileId,
            row.Result.Server
        }))
        {
            var entries = group
                .GroupBy(row => row.SessionIndex)
                .Select(sessionGroup => sessionGroup.First().Result)
                .ToArray();

            if (entries.Length == 0)
                continue;

            var samples = entries
                .Where(entry => entry.Samples is not null)
                .SelectMany(entry => entry.Samples)
                .ToArray();

            var successfulSamples = samples
                .Where(sample =>
                    sample is not null &&
                    sample.Success &&
                    sample.RoundtripMs.HasValue &&
                    double.IsFinite(sample.RoundtripMs.Value) &&
                    sample.RoundtripMs.Value >= 0)
                .ToArray();

            var latencies = successfulSamples
                .Select(sample => sample.RoundtripMs!.Value)
                .OrderBy(value => value)
                .ToArray();

            var requestedCount = entries.Sum(entry => entry.RequestedCount);
            var successCount = successfulSamples.Length;

            var passingSessionCount = entries.Count(entry =>
            {
                if (entry.RequestedCount <= 0 || entry.Samples is null)
                    return false;

                var sessionSuccessCount = entry.Samples.Count(sample =>
                    sample is not null &&
                    sample.Success &&
                    sample.RoundtripMs.HasValue &&
                    double.IsFinite(sample.RoundtripMs.Value) &&
                    sample.RoundtripMs.Value >= 0);

                return sessionSuccessCount / (double)entry.RequestedCount
                    >= MinimumPerSessionSuccessRate;
            });

            var jitterValues = new List<double>();

            foreach (var entry in entries)
            {
                if (entry.Samples is null ||
                    entry.Samples.Any(sample => sample is null))
                {
                    continue;
                }

                var orderedSamples = entry.Samples
                    .OrderBy(sample => sample.Sequence)
                    .ToArray();

                for (var index = 1; index < orderedSamples.Length; index++)
                {
                    var previous = orderedSamples[index - 1];
                    var current = orderedSamples[index];

                    if (current.Sequence != previous.Sequence + 1 ||
                        !previous.Success ||
                        !current.Success ||
                        !previous.RoundtripMs.HasValue ||
                        !current.RoundtripMs.HasValue ||
                        !double.IsFinite(previous.RoundtripMs.Value) ||
                        !double.IsFinite(current.RoundtripMs.Value))
                    {
                        continue;
                    }

                    jitterValues.Add(Math.Abs(
                        current.RoundtripMs.Value - previous.RoundtripMs.Value));
                }
            }

            var failures = samples
                .Where(sample => sample is not null && !sample.Success)
                .ToArray();

            var failureSummary = failures.Length == 0
                ? "Yok"
                : string.Join(", ",
                    failures
                        .GroupBy(sample => sample.Status)
                        .OrderByDescending(statusGroup => statusGroup.Count())
                        .Take(5)
                        .Select(statusGroup =>
                            $"{statusGroup.Key}: {statusGroup.Count()}"));

            aggregates.Add(new DnsResolverAggregateResult(
                entries[0].Provider,
                entries[0].ProfileId,
                entries[0].Server,
                entries.All(entry => entry.IsApplicationCandidate),
                entries[0].ApplicationNotice,
                entries.Length,
                passingSessionCount,
                requestedCount,
                successCount,
                latencies.Length == 0 ? null : latencies[0],
                latencies.Length == 0 ? null : latencies.Average(),
                DnsResolverBenchmark.CalculateMedian(latencies),
                DnsResolverBenchmark.CalculatePercentile(latencies, 0.95d),
                jitterValues.Count == 0 ? null : jitterValues.Average(),
                failureSummary));
        }

        return aggregates
            .OrderBy(result => result.MedianMs ?? double.MaxValue)
            .ThenBy(result => result.Provider, StringComparer.Ordinal)
            .ThenBy(result => result.Server, StringComparer.Ordinal)
            .ToArray();
    }

    internal static DnsResolverAggregateResult? SelectBestReliableIndividual(
        IEnumerable<DnsResolverBenchmarkSession> sessions)
    {
        if (sessions is null)
            throw new ArgumentNullException(nameof(sessions));

        var sessionArray = sessions
            .TakeLast(MaximumStoredSessions)
            .ToArray();

        if (sessionArray.Length < MinimumTrustedSessionCount)
            return null;

        var requiredPassingSessions = (int)Math.Ceiling(
            sessionArray.Length * (2d / 3d));

        return AggregateSessions(sessionArray)
            .Where(result =>
                result.IsApplicationCandidate &&
                result.SessionCount == sessionArray.Length &&
                result.RequestedCount == sessionArray.Length * SamplesPerResolverSession &&
                result.SuccessRate >= MinimumAggregateSuccessRate &&
                result.PassingSessionCount >= requiredPassingSessions &&
                result.MedianMs.HasValue &&
                result.P95Ms.HasValue &&
                result.JitterMs.HasValue)
            .OrderBy(result => result.P95Ms)
            .ThenBy(result => result.JitterMs)
            .ThenBy(result => result.MedianMs)
            .ThenByDescending(result => result.SuccessRate)
            .FirstOrDefault();
    }

    internal static DnsProfileRanking? SelectBestProfile(
        IEnumerable<DnsResolverBenchmarkSession> sessions,
        IEnumerable<DnsResolverProfileDefinition> profiles)
    {
        if (sessions is null)
            throw new ArgumentNullException(nameof(sessions));

        if (profiles is null)
            throw new ArgumentNullException(nameof(profiles));

        var sessionArray = sessions
            .TakeLast(MaximumStoredSessions)
            .ToArray();

        if (sessionArray.Length < MinimumTrustedSessionCount)
            return null;

        var aggregates = AggregateSessions(sessionArray);

        var requiredPassingSessions = (int)Math.Ceiling(
            sessionArray.Length * (2d / 3d));

        var rankings = new List<DnsProfileRanking>();

        foreach (var profile in profiles)
        {
            if (profile.Ipv4Servers is null ||
                profile.Ipv4Servers.Length != 2 ||
                profile.Ipv4Servers.Distinct(StringComparer.Ordinal).Count() != 2)
            {
                continue;
            }

            var first = aggregates.FirstOrDefault(result =>
                result.ProfileId == profile.ProfileId &&
                result.Server == profile.Ipv4Servers[0]);

            var second = aggregates.FirstOrDefault(result =>
                result.ProfileId == profile.ProfileId &&
                result.Server == profile.Ipv4Servers[1]);

            if (first is null || second is null)
                continue;

            if (!first.IsApplicationCandidate ||
                !second.IsApplicationCandidate ||
                !string.Equals(first.Provider, profile.Provider, StringComparison.Ordinal) ||
                !string.Equals(second.Provider, profile.Provider, StringComparison.Ordinal))
            {
                continue;
            }

            if (first.SessionCount != sessionArray.Length ||
                second.SessionCount != sessionArray.Length ||
                first.RequestedCount != sessionArray.Length * SamplesPerResolverSession ||
                second.RequestedCount != sessionArray.Length * SamplesPerResolverSession)
            {
                continue;
            }

            if (first.SuccessRate < MinimumAggregateSuccessRate ||
                second.SuccessRate < MinimumAggregateSuccessRate ||
                first.PassingSessionCount < requiredPassingSessions ||
                second.PassingSessionCount < requiredPassingSessions)
            {
                continue;
            }

            if (!first.MedianMs.HasValue ||
                !second.MedianMs.HasValue ||
                !first.P95Ms.HasValue ||
                !second.P95Ms.HasValue ||
                !first.JitterMs.HasValue ||
                !second.JitterMs.HasValue)
            {
                continue;
            }

            var medianAverage = (first.MedianMs.Value + second.MedianMs.Value) / 2d;
            var p95Average = (first.P95Ms.Value + second.P95Ms.Value) / 2d;
            var jitterAverage = (first.JitterMs.Value + second.JitterMs.Value) / 2d;

            // İyi sunucunun sonucu kötü sunucuyu ortalamada gizleyemez.
            var worstServerP95 = Math.Max(first.P95Ms.Value, second.P95Ms.Value);
            var worstServerJitter = Math.Max(first.JitterMs.Value, second.JitterMs.Value);

            if (!double.IsFinite(medianAverage) ||
                !double.IsFinite(p95Average) ||
                !double.IsFinite(jitterAverage) ||
                !double.IsFinite(worstServerP95) ||
                !double.IsFinite(worstServerJitter) ||
                medianAverage < 0 ||
                p95Average < 0 ||
                jitterAverage < 0 ||
                worstServerP95 < 0 ||
                worstServerJitter < 0)
            {
                continue;
            }

            rankings.Add(new DnsProfileRanking(
                profile.ProfileId,
                profile.Provider,
                medianAverage,
                p95Average,
                jitterAverage,
                worstServerP95,
                worstServerJitter,
                first.SuccessCount + second.SuccessCount,
                first.RequestedCount + second.RequestedCount,
                sessionArray.Length,
                Math.Min(first.PassingSessionCount, second.PassingSessionCount)));
        }

        // Medyan ana ölçüttür; P95 ve jitter eşitlikte sıralamayı belirler.
        // Önce iki sunucunun daha kötü P95 ve jitter değerleri,
        // ardından profilin medyan hızı karşılaştırılır.
        return rankings
            .OrderBy(ranking => ranking.WorstServerP95Ms)
            .ThenBy(ranking => ranking.WorstServerJitterMs)
            .ThenBy(ranking => ranking.MedianAverageMs)
            .ThenBy(ranking => ranking.P95AverageMs)
            .ThenBy(ranking => ranking.JitterAverageMs)
            .ThenByDescending(ranking => ranking.SuccessRate)
            .FirstOrDefault();
    }
}