namespace Wdc.Cube.Tests.Navigation;

public sealed class PlaceTests
{
    [Fact]
    public void Path_GoesFromTheRootToThePlace_WithoutDetachedPlaces()
    {
        var orders = Place.Create("orders");
        var detail = orders.Child("detail");

        Assert.Equal([orders, detail], detail.Path);
        Assert.Equal("orders/detail", detail.PathName);
        Assert.Same(Place.Root, orders.Parent);
        Assert.Empty(Place.Root.Path);
    }

    [Fact]
    public void ChildIds_AreGreaterThanTheirAncestors()
    {
        // Releasing in descending id order relies on this to release leaves first.
        var a = Place.Create("a");
        var b = a.Child("b");
        var c = b.Child("c");
        var a2 = Place.Create("a2");

        Assert.True(a.Id < b.Id && b.Id < c.Id);
        Assert.True(a.Id >= 0 && a2.Id >= 0);
        Assert.True(Place.Root.IsDetached && Place.Unknown.IsDetached);
    }

    [Fact]
    public void Contains_IsTrueForItselfAndDescendants()
    {
        var a = Place.Create("a");
        var b = a.Child("b");
        var other = Place.Create("x");

        Assert.True(a.Contains(a));
        Assert.True(a.Contains(b));
        Assert.False(b.Contains(a));
        Assert.False(a.Contains(other));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a?b")]
    [InlineData("a&b")]
    [InlineData("a#b")]
    [InlineData("a=b")]
    [InlineData(" ")]
    public void Name_CannotBreakTheAddress(string name) =>
        Assert.ThrowsAny<ArgumentException>(() => Place.Create(name));
}
