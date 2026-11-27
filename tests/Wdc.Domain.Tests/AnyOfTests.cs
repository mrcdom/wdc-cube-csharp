namespace Wdc.Domain.Tests;

/// <summary>
/// Alternative groups: what counts as a filter, and that they never travel (refused when written and when read, so
/// neither side answers with a looser filter than the one built).
/// </summary>
public sealed class AnyOfTests
{
    [Fact]
    public void ACriteria_StartsWithoutGroups()
    {
        var criteria = new ProductCriteria();

        Assert.Empty(criteria.AnyOf);
        Assert.Empty(((ICriteria)criteria).AnyOf);
        Assert.False(criteria.HasFilter);
    }

    [Fact]
    public void Groups_KeepTheirOrderAndType_AndReplaceThePreviousOnes()
    {
        var cheap = new ProductCriteria().Price.Lt(10m);
        var ball = new ProductCriteria().Name.Eq("Ball");

        var criteria = new ProductCriteria().WithAnyOf(new ProductCriteria()).WithAnyOf(cheap, ball);

        Assert.Equal([cheap, ball], criteria.AnyOf);
        Assert.Same(ball, ((ICriteria)criteria).AnyOf[1]);
        Assert.Empty(criteria.WithAnyOf().AnyOf);
    }

    [Fact]
    public void ACriteria_CannotBeItsOwnGroup()
    {
        var criteria = new ProductCriteria();

        Assert.Throws<ArgumentException>(() => criteria.WithAnyOf(criteria));
    }

    [Fact]
    public void HasFilter_WhenEveryGroupFilters_NotWhenOneMatchesEverything()
    {
        var cheap = new ProductCriteria().Price.Lt(10m);

        Assert.True(new ProductCriteria().WithAnyOf(cheap, new ProductCriteria().Name.Eq("Ball")).HasFilter);
        Assert.False(new ProductCriteria().WithAnyOf(cheap, new ProductCriteria()).HasFilter);
        Assert.True(new ProductCriteria().Name.Eq("Ball").WithAnyOf(cheap, new ProductCriteria()).HasFilter);
    }

    [Fact]
    public void ToString_ShowsTheGroups()
    {
        var criteria = new ProductCriteria().Name.Eq("Ball").WithAnyOf(new ProductCriteria().Price.Lt(10m));

        Assert.Contains("anyOf [", criteria.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Writing_ACriteriaWithGroups_IsRefused()
    {
        var criteria = new ProductCriteria().Name.Eq("Ball").WithAnyOf(new ProductCriteria().Price.Lt(10m));

        var refused = Assert.Throws<InvalidRequestException>(() => CriteriaCodec.ToJson(criteria));

        Assert.Contains("anyOf", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Ball", CriteriaCodec.ToJson(new ProductCriteria().Name.Eq("Ball")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"anyOf":[{"price":3}]}""")]
    [InlineData("""{"name":"Ball","anyOf":[]}""")]
    [InlineData("""{"anyOf":null}""")]
    public void Reading_AnAnyOfKey_IsABadRequest(string json)
    {
        var refused = Assert.Throws<InvalidRequestException>(() => CriteriaCodec.FromJson(json, new ProductCriteria()));

        Assert.Contains("do not travel", refused.Message, StringComparison.Ordinal);
    }
}
