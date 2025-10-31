namespace DocumentIntelligence.Core.Search;

/// <summary>An item after fusion, with the 1-based rank it held in each input list.</summary>
public sealed record FusedResult<TKey>(TKey Key, double Score, IReadOnlyList<int?> Ranks);

/// <summary>
/// Reciprocal Rank Fusion (Cormack, Clarke and Buettcher, 2009): every list contributes
/// <c>1 / (k + rank)</c> for each item it contains. Only ranks matter, so lists whose raw
/// scores live on different scales (cosine similarity, BM25) can be combined directly.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    /// <remarks>
    /// Ties are broken by the best rank an item achieved in any list, then by the order in
    /// which items were first seen, so the output is deterministic.
    /// </remarks>
    public static IReadOnlyList<FusedResult<TKey>> Fuse<TKey>(IReadOnlyList<IReadOnlyList<TKey>> rankings, int k = DefaultK)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(rankings);
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        var accumulators = new Dictionary<TKey, Accumulator>();
        var order = new List<TKey>();

        for (var list = 0; list < rankings.Count; list++)
        {
            for (var position = 0; position < rankings[list].Count; position++)
            {
                var key = rankings[list][position];
                if (!accumulators.TryGetValue(key, out var accumulator))
                {
                    accumulator = new Accumulator(new int?[rankings.Count]);
                    accumulators[key] = accumulator;
                    order.Add(key);
                }

                // A key repeated within one list only counts at its best position.
                if (accumulator.Ranks[list] is not null)
                {
                    continue;
                }

                var rank = position + 1;
                accumulator.Ranks[list] = rank;
                accumulator.Score += 1.0 / (k + rank);
            }
        }

        return order
            .Select(key => (Key: key, Accumulator: accumulators[key]))
            .OrderByDescending(item => item.Accumulator.Score)
            .ThenBy(item => item.Accumulator.Ranks.Min() ?? int.MaxValue)
            .Select(item => new FusedResult<TKey>(item.Key, item.Accumulator.Score, item.Accumulator.Ranks))
            .ToList();
    }

    private sealed class Accumulator(int?[] ranks)
    {
        public int?[] Ranks { get; } = ranks;

        public double Score { get; set; }
    }
}
