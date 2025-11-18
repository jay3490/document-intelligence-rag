using DocumentIntelligence.Core.Search;

namespace DocumentIntelligence.UnitTests.Search;

public sealed class ReciprocalRankFusionTests
{
    [Fact]
    public void Each_list_contributes_the_reciprocal_of_k_plus_rank()
    {
        var fused = ReciprocalRankFusion.Fuse<string>([["a", "b"], ["b"]], k: 60);

        var b = fused.Single(r => r.Key == "b");
        var a = fused.Single(r => r.Key == "a");
        Assert.Equal((1.0 / 62) + (1.0 / 61), b.Score, precision: 12);
        Assert.Equal(1.0 / 61, a.Score, precision: 12);
    }

    [Fact]
    public void Agreement_between_lists_beats_a_single_first_place()
    {
        var fused = ReciprocalRankFusion.Fuse<string>([["a", "b", "c"], ["d", "b"]]);

        Assert.Equal("b", fused[0].Key);
        Assert.Equal(["b", "a", "d", "c"], fused.Select(r => r.Key));
    }

    [Fact]
    public void Ranks_are_reported_per_list_and_null_when_absent()
    {
        var fused = ReciprocalRankFusion.Fuse<string>([["a", "b"], ["b", "c"]]);

        Assert.Equal([2, 1], fused.Single(r => r.Key == "b").Ranks);
        Assert.Equal([1, null], fused.Single(r => r.Key == "a").Ranks);
        Assert.Equal([null, 2], fused.Single(r => r.Key == "c").Ranks);
    }

    [Fact]
    public void Ties_keep_the_order_items_were_first_seen()
    {
        var fused = ReciprocalRankFusion.Fuse<string>([["x"], ["y"]]);

        Assert.Equal(["x", "y"], fused.Select(r => r.Key));
        Assert.Equal(fused[0].Score, fused[1].Score);
    }

    [Fact]
    public void A_duplicate_within_one_list_counts_once_at_its_best_rank()
    {
        var fused = ReciprocalRankFusion.Fuse<string>([["a", "a", "b"]], k: 0);

        Assert.Equal(1.0, fused.Single(r => r.Key == "a").Score, precision: 12);
        Assert.Equal(1.0 / 3, fused.Single(r => r.Key == "b").Score, precision: 12);
    }

    [Fact]
    public void Larger_k_flattens_the_gap_between_ranks()
    {
        static double Gap(int k)
        {
            var fused = ReciprocalRankFusion.Fuse<string>([["first", "second"]], k);
            return fused[0].Score / fused[1].Score;
        }

        Assert.True(Gap(1) > Gap(60));
    }

    [Fact]
    public void No_lists_fuse_to_nothing()
    {
        Assert.Empty(ReciprocalRankFusion.Fuse<string>([]));
        Assert.Empty(ReciprocalRankFusion.Fuse<string>([[], []]));
    }
}
