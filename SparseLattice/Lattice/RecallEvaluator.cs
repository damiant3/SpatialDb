using System.Numerics;
using SparseLattice.Math;
/////////////////////////////////
namespace SparseLattice.Lattice;

/// <summary>
/// Recall evaluation results for a single query.
/// </summary>
public readonly struct RecallResult
{
    /// <summary>Number of true nearest neighbors found by the index.</summary>
    public int TruePositives { get; init; }

    /// <summary>K value the query was evaluated at.</summary>
    public int K { get; init; }

    /// <summary>Recall@K = TruePositives / K. Range [0, 1].</summary>
    public double RecallAtK => K == 0 ? 0.0 : (double)TruePositives / K;
}

/// <summary>
/// Aggregated recall statistics across many queries.
/// </summary>
public readonly struct AggregateRecallStats
{
    /// <summary>Mean recall@K across all queries.</summary>
    public double MeanRecallAtK { get; init; }

    /// <summary>Minimum recall@K observed across all queries.</summary>
    public double MinRecallAtK { get; init; }

    /// <summary>Maximum recall@K observed across all queries.</summary>
    public double MaxRecallAtK { get; init; }

    /// <summary>Number of queries evaluated.</summary>
    public int QueryCount { get; init; }

    /// <summary>K value the evaluation was run at.</summary>
    public int K { get; init; }

    public override string ToString()
        => $"Recall@{K}: mean={MeanRecallAtK:P2} min={MinRecallAtK:P2} max={MaxRecallAtK:P2} over {QueryCount} queries";
}

/// <summary>
/// Computes recall by comparing lattice KNN results against brute-force ground truth.
/// This is the core harness for Phase 10 quantization experiments.
/// </summary>
public sealed class RecallEvaluator
{
    private RecallEvaluator() { }

    /// <summary>
    /// Evaluates recall@K for a single query against a known ground-truth ranked list.
    /// <paramref name="groundTruth"/> must be ordered nearest-first and contain at least K entries.
    /// <paramref name="candidates"/> is the set returned by the index (order does not matter).
    /// Equality is determined by comparing <see cref="SparseVector"/> positions.
    /// </summary>
    public static RecallResult EvaluateQuery<TPayload>(
        IReadOnlyList<SparseOccupant<TPayload>> groundTruth,
        IReadOnlyList<SparseOccupant<TPayload>> candidates,
        int k)
    {
        int limit = System.Math.Min(k, groundTruth.Count);
        if (limit == 0)
            return new RecallResult { K = k, TruePositives = 0 };

        System.Collections.Generic.HashSet<SparseVector> truthSet = [];
        for (int i = 0; i < limit; i++)
            truthSet.Add(groundTruth[i].Position);

        int truePositives = 0;
        foreach (SparseOccupant<TPayload> candidate in candidates)
            if (truthSet.Contains(candidate.Position))
                truePositives++;

        return new RecallResult { K = limit, TruePositives = truePositives };
    }

    /// <summary>
    /// Computes brute-force L2 ground truth for <paramref name="query"/> against the full corpus,
    /// then evaluates how many of those appear in <paramref name="indexResults"/>.
    /// </summary>
    public static RecallResult EvaluateL2<TPayload>(
        SparseVector query,
        IReadOnlyList<SparseOccupant<TPayload>> corpus,
        IReadOnlyList<SparseOccupant<TPayload>> indexResults,
        int k)
    {
        List<SparseOccupant<TPayload>> groundTruth = BruteForceKNearestL2(query, corpus, k);
        return EvaluateQuery(groundTruth, indexResults, k);
    }

    /// <summary>
    /// Computes brute-force L1 ground truth for <paramref name="query"/> against the full corpus,
    /// then evaluates how many of those appear in <paramref name="indexResults"/>.
    /// </summary>
    public static RecallResult EvaluateL1<TPayload>(
        SparseVector query,
        IReadOnlyList<SparseOccupant<TPayload>> corpus,
        IReadOnlyList<SparseOccupant<TPayload>> indexResults,
        int k)
    {
        List<SparseOccupant<TPayload>> groundTruth = BruteForceKNearestL1(query, corpus, k);
        return EvaluateQuery(groundTruth, indexResults, k);
    }

    /// <summary>
    /// Aggregates recall@K across multiple queries against the same corpus.
    /// </summary>
    public static AggregateRecallStats AggregateL2<TPayload>(
        IReadOnlyList<SparseVector> queries,
        IReadOnlyList<SparseOccupant<TPayload>> corpus,
        System.Func<SparseVector, IReadOnlyList<SparseOccupant<TPayload>>> indexQuery,
        int k)
    {
        if (queries.Count == 0)
            return new AggregateRecallStats { K = k };

        double totalRecall = 0.0;
        double minRecall = double.MaxValue;
        double maxRecall = 0.0;

        foreach (SparseVector query in queries)
        {
            IReadOnlyList<SparseOccupant<TPayload>> indexResults = indexQuery(query);
            RecallResult result = EvaluateL2(query, corpus, indexResults, k);
            double recall = result.RecallAtK;
            totalRecall += recall;
            if (recall < minRecall) minRecall = recall;
            if (recall > maxRecall) maxRecall = recall;
        }

        return new AggregateRecallStats
        {
            K = k,
            QueryCount = queries.Count,
            MeanRecallAtK = totalRecall / queries.Count,
            MinRecallAtK = minRecall == double.MaxValue ? 0.0 : minRecall,
            MaxRecallAtK = maxRecall,
        };
    }

    /// <summary>
    /// Returns the K nearest entries in <paramref name="corpus"/> to <paramref name="query"/>
    /// by squared L2 distance, sorted nearest-first. O(n) scan — ground truth only.
    /// </summary>
    public static List<SparseOccupant<TPayload>> BruteForceKNearestL2<TPayload>(
        SparseVector query,
        IReadOnlyList<SparseOccupant<TPayload>> corpus,
        int k)
    {
        List<(SparseOccupant<TPayload> occupant, BigInteger distance)> all = new(corpus.Count);
        foreach (SparseOccupant<TPayload> item in corpus)
            all.Add((item, query.DistanceSquaredL2(item.Position)));
        all.Sort((a, b) => a.distance.CompareTo(b.distance));

        int take = System.Math.Min(k, all.Count);
        List<SparseOccupant<TPayload>> result = new(take);
        for (int i = 0; i < take; i++)
            result.Add(all[i].occupant);
        return result;
    }

    /// <summary>
    /// Returns the K nearest entries in <paramref name="corpus"/> to <paramref name="query"/>
    /// by L1 distance, sorted nearest-first. O(n) scan — ground truth only.
    /// </summary>
    public static List<SparseOccupant<TPayload>> BruteForceKNearestL1<TPayload>(
        SparseVector query,
        IReadOnlyList<SparseOccupant<TPayload>> corpus,
        int k)
    {
        List<(SparseOccupant<TPayload> occupant, BigInteger distance)> all = new(corpus.Count);
        foreach (SparseOccupant<TPayload> item in corpus)
            all.Add((item, query.DistanceL1(item.Position)));
        all.Sort((a, b) => a.distance.CompareTo(b.distance));

        int take = System.Math.Min(k, all.Count);
        List<SparseOccupant<TPayload>> result = new(take);
        for (int i = 0; i < take; i++)
            result.Add(all[i].occupant);
        return result;
    }
}
