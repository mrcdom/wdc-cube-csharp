namespace Wdc.Domain.Tests;

public sealed class EntityCodecTests
{
    private static readonly JsonEntityCodec<Product> s_products = new(Product.Model);
    private static readonly JsonEntityCodec<Purchase> s_purchases = new(Purchase.Model);
    private static readonly JsonEntityCodec<User> s_users = new(User.Model);

    [Fact]
    public void WithoutProjection_WritesTheFieldsThatHoldAValue()
    {
        var json = s_products.ToJson(new Product { Id = 1, Name = "Bola", Price = 10.5m });

        Assert.Equal("""{"id":1,"name":"Bola","price":10.5}""", json);
    }

    [Fact]
    public void WithProjection_WritesTheMarkedFields_WithExplicitNulls()
    {
        var json = s_products.ToJson(
            new Product { Id = 1, Name = "Bola", Description = "ignored" },
            new Product { Id = Mark.Int64, Name = Mark.String, Price = Mark.Decimal });

        Assert.Equal("""{"id":1,"name":"Bola","price":null}""", json); // price: "set to null"
    }

    [Fact]
    public void ReadForUpdate_MarksEveryKeyPresent_IncludingNulls_AndOnlyThose()
    {
        var (entity, presence) = s_products.ReadForUpdate("""{"id":1,"name":null,"price":"9.9","unknown":true}""");

        Assert.Equal(1, entity.Id);
        Assert.Null(entity.Name);
        Assert.Equal(9.9m, entity.Price); // a number sent as text is tolerated
        Assert.NotNull(presence.Id);
        Assert.NotNull(presence.Name); // present as null: "clear it"
        Assert.NotNull(presence.Price);
        Assert.Null(presence.Description); // absent: "leave it"
    }

    [Fact]
    public void Graph_ACycleIsWrittenAsTheKeyOnly()
    {
        var purchase = new Purchase { Id = 7, User = new User { Id = 3 } };
        purchase.Items = [new PurchaseItem { Id = 70, Amount = 2, Purchase = purchase }];

        var json = s_purchases.ToJson(purchase);

        Assert.Equal("""{"id":7,"user":{"id":3},"items":[{"id":70,"amount":2,"purchase":{"id":7}}]}""", json);
    }

    [Fact]
    public void Read_RebuildsNestedReferencesAndCollections()
    {
        var purchase = s_purchases.FromJson(
            """{"id":7,"buyDate":"2026-11-18T09:00:00-03:00","user":{"id":3,"name":"Ana"},"items":[{"id":70,"product":{"id":5}}]}""");

        Assert.Equal(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero), purchase.BuyDate);
        Assert.Equal("Ana", purchase.User!.Name);
        Assert.Equal(5, purchase.Items!.Single().Product!.Id);
    }

    [Fact]
    public void ReadForUpdate_ACollectionsPresenceIsTheUnionOfWhatItsChildrenCarried()
    {
        var (purchase, presence) = s_purchases.ReadForUpdate("""{"id":1,"items":[{"id":5,"amount":2},{"price":3}]}""");

        Assert.Equal(2, purchase.Items!.Count);
        var shape = Assert.IsType<ProjectionList<PurchaseItem>>(presence.Items).Shape;
        Assert.NotNull(shape.Id);
        Assert.NotNull(shape.Amount);
        Assert.NotNull(shape.Price);
        Assert.Null(shape.Product); // not sent: not written
    }

    [Fact]
    public void ProjectionEnvelope_CarriesTheChildFilterAndSlice()
    {
        var projection = new Purchase
        {
            Id = Mark.Int64,
            Items = Mark.List(new PurchaseItem { Id = Mark.Int64, Price = Mark.Decimal },
                new PurchaseItemCriteria().Amount.Ge(2).WithOrderBy(PurchaseItemOrderBy.MostExpensiveFirst), limit: 3, offset: 6),
        };

        var json = s_purchases.ToJson(projection);
        Assert.Equal(
            """{"id":1,"items":{"shape":{"id":1,"price":1},"where":{"amount":{"p":[{"o":"GE","v":[2]}]},"orderBy":"MOST_EXPENSIVE_FIRST"},"limit":3,"offset":6}}""",
            json);

        var read = s_purchases.FromJson(json);
        var items = Assert.IsType<ProjectionList<PurchaseItem>>(read.Items);
        Assert.Equal(3, items.Limit);
        Assert.Equal(6, items.Offset);
        Assert.Equal(2, Assert.Single(Assert.Single(((PurchaseItemCriteria)items.Criteria!).Amount.Predicates).Values));
        var where = Assert.IsType<PurchaseItemCriteria>(items.Criteria);
        Assert.Equal(PurchaseItemOrderBy.MostExpensiveFirst, where.OrderBy);
        Assert.NotNull(items.Shape.Price);
    }

    [Theory]
    [InlineData("""{"items":{"limit":-1}}""")]
    [InlineData("""{"items":{"offset":-5}}""")]
    [InlineData("""{"items":{"limit":99999999999}}""")]
    public void Projection_ACollectionSliceOutOfRange_IsAnInvalidRequest(string json) =>
        Assert.Throws<InvalidRequestException>(() => s_purchases.FromJson(json));

    [Fact]
    public void Projection_TheMarkerOfAnEnumWithNoZeroMember_ReadsBackAsMarked()
    {
        var kind = ValueKinds.Enum<Level>();
        var marker = kind.Marker!.Value;
        Assert.True(Enum.IsDefined(marker));

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            kind.Write(writer, marker);
        }

        var reader = new System.Text.Json.Utf8JsonReader(buffer.WrittenSpan);
        reader.Read();
        Assert.Equal(marker, kind.Read(ref reader));
    }

    private enum Level
    {
        Low = 1,
        High = 2,
    }

    [Fact]
    public void Values_TravelInTheSharedFormats()
    {
        var instant = s_purchases.ToJson(new Purchase { BuyDate = new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.FromHours(-3)) });
        Assert.Equal("""{"buyDate":"2026-11-18T12:30:00Z"}""", instant); // UTC, no zero fraction

        var user = s_users.FromJson("""{"role":"Admin"}""");
        Assert.Equal(Role.Admin, user.Role);
        Assert.Null(s_users.FromJson("""{"role":"Nobody"}""").Role); // an unknown enum name reads as absent

        var product = s_products.FromJson("{\"image\":\"AQID\\nBA==\"}");
        Assert.Equal([1, 2, 3, 4], product.Image!); // Base64 with the line breaks PostgreSQL inserts
    }
}
