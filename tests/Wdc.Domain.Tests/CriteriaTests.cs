namespace Wdc.Domain.Tests;

public sealed class CriteriaTests
{
    [Fact]
    public void NullValues_AskNothing()
    {
        var c = new ProductCriteria()
            .Name.Eq(null)
            .Price.Gt(null)
            .ProductId.In((IEnumerable<long?>?)null)
            .Price.Between(null, null);

        Assert.False(c.HasFilter);
    }

    [Fact]
    public void Between_WithAnOpenEnd_BecomesGeOrLe()
    {
        var c = new ProductCriteria().Price.Between(10m, null);
        Assert.Equal(Operator.Ge, c.Price.Predicates.Single().Operator);

        c = new ProductCriteria().Price.Between(null, 50m);
        Assert.Equal(Operator.Le, c.Price.Predicates.Single().Operator);

        c = new ProductCriteria().Price.Between(10m, 50m);
        Assert.Equal([10m, 50m], c.Price.Predicates.Single().Values);
    }

    [Fact]
    public void In_EmptyList_IsARequestThatMatchesNothing()
    {
        var c = new ProductCriteria().ProductId.In();

        Assert.True(c.HasFilter);
        Assert.Equal(Operator.In, c.ProductId.Predicates.Single().Operator);
        Assert.Empty(c.ProductId.Predicates.Single().Values);
    }

    [Fact]
    public void SameField_CombinesWithAnd_OrAfterOr_AndChainingKeepsTheType()
    {
        var c = new ProductCriteria();
        c.Name.Or().StartingWith("Bola");
        c.Name.Containing("50%_off");

        Assert.True(c.Name.Disjunctive);
        Assert.Equal(["Bola%", "%50\\%\\_off%"], c.Name.Predicates.Select(p => (string)p.Values[0]!));
        Assert.All(c.Name.Predicates, p => Assert.Equal(Operator.ILike, p.Operator));
    }

    [Fact]
    public void OrderBy_TravelsWithTheKotlinNames()
    {
        var c = new ProductCriteria().WithOrderBy(ProductOrderBy.NameAToZ);

        Assert.Equal("NAME_A_TO_Z", c.OrderByName);
        Assert.Equal(["NAME_A_TO_Z", "CHEAPEST_FIRST"], c.AcceptedOrderBy);

        c.SetOrderByName("CHEAPEST_FIRST");
        Assert.Equal(ProductOrderBy.CheapestFirst, c.OrderBy);
    }

    [Fact]
    public void OrderBy_Unknown_IsRefusedNamingTheAcceptedOnes()
    {
        var error = Assert.Throws<InvalidRequestException>(() => new ProductCriteria().SetOrderByName("RANDOM"));

        Assert.Contains("RANDOM", error.Message, StringComparison.Ordinal);
        Assert.Contains("CHEAPEST_FIRST", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wire_WritesTheKotlinFormat()
    {
        var c = new ProductCriteria()
            .Price.Between(10.5m, 50.5m)
            .ProductId.Eq(3)
            .WithOrderBy(ProductOrderBy.CheapestFirst);
        c.Name.Or().Like("Bola%");
        c.Name.Eq("Fita");

        Assert.Equal(
            """{"productId":{"p":[{"o":"EQ","v":[3]}]},"name":{"or":true,"p":[{"o":"LIKE","v":["Bola%"]},{"o":"EQ","v":["Fita"]}]},"price":{"p":[{"o":"BETWEEN","v":[10.5,50.5]}]},"orderBy":"CHEAPEST_FIRST"}""",
            CriteriaCodec.ToJson(c));
    }

    [Fact]
    public void Wire_ReadsWhatKotlinWrites_IncludingALooseValueAsEquality()
    {
        // The example of wdc-cube-kotlin/docs/architecture-persistence.md
        const string json = """
            { "price": { "p": [ { "o": "GE", "v": [10.5] }, { "o": "LE", "v": [50.5] } ] },
              "name":  { "or": true, "p": [ { "o": "LIKE", "v": ["Bola%"] }, { "o": "EQ", "v": ["Fita"] } ] },
              "productId": 3,
              "orderBy": "CHEAPEST_FIRST" }
            """;

        var c = CriteriaCodec.FromJson(json, new ProductCriteria());

        Assert.Equal([(Operator.Ge, 10.5m), (Operator.Le, 50.5m)],
            c.Price.TypedPredicates.Select(p => (p.Operator, p.Values[0]!.Value)));
        Assert.True(c.Name.Disjunctive);
        Assert.Equal([(Operator.Like, "Bola%"), (Operator.Eq, "Fita")], c.Name.TypedPredicates.Select(p => (p.Operator, p.Values[0]!)));
        Assert.Equal((Operator.Eq, 3L), c.ProductId.TypedPredicates.Select(p => (p.Operator, p.Values[0]!.Value)).Single());
        Assert.Equal(ProductOrderBy.CheapestFirst, c.OrderBy);
    }

    [Theory]
    [InlineData("""{ "price": { "p": [ { "o": "GT", "v": [null] } ] } }""")]
    [InlineData("""{ "price": { "p": [ { "o": "BETWEEN", "v": [1] } ] } }""")]
    [InlineData("""{ "price": { "p": [ { "o": "LE", "v": [] } ] } }""")]
    [InlineData("""{ "name": { "p": [ { "o": "LIKE", "v": [null] } ] } }""")]
    [InlineData("""{ "price": { "p": [ { "o": "IS_NULL", "v": [1] } ] } }""")]
    public void Wire_ARequestThatCannotFilter_IsRefused_NotKeptAsAFilterThatTranslatesToNone(string json) =>
        Assert.Throws<InvalidRequestException>(() => CriteriaCodec.FromJson(json, new ProductCriteria()));

    [Fact]
    public void Wire_AnUnknownOperatorIsDropped_AnExplicitNullSurvives_AndUnknownKeysAreSkipped()
    {
        const string json = """
            { "price": { "p": [ { "o": "SOUNDS_LIKE", "v": [1] }, { "o": "GE", "v": ["7"] } ] },
              "name": { "p": [ { "o": "EQ", "v": [null] } ] },
              "productId": null,
              "somethingNew": { "deep": [1, 2] } }
            """;

        var c = CriteriaCodec.FromJson(json, new ProductCriteria());

        Assert.Equal((Operator.Ge, 7m), c.Price.TypedPredicates.Select(p => (p.Operator, p.Values[0]!.Value)).Single());
        Assert.Null(c.Name.TypedPredicates.Single().Values[0]); // "EQ null" from the other side is kept, not dropped
        Assert.False(c.ProductId.IsSet); // a loose null asks nothing
    }

    [Fact]
    public void Wire_RoundTrips()
    {
        var c = new ProductCriteria().Name.ILike("%x%").ProductId.In(1, 2, 3).ProductId.IsNotNull();

        var copy = CriteriaCodec.FromJson(CriteriaCodec.ToJson(c), new ProductCriteria());

        Assert.Equal(CriteriaCodec.ToJson(c), CriteriaCodec.ToJson(copy));
    }
}
