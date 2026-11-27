namespace Wdc.Cube.Tests.Presentation;

/// <summary>A message is its key and arguments: equal by value, arguments written for the catalog.</summary>
public sealed class MessageTests
{
    [Fact]
    public void TwoMessages_WithTheSameKeyAndArguments_AreEqual()
    {
        var a = Message.Of("cart.total", ("total", "$1.00"), ("count", 2));
        var b = Message.Of("cart.total", ("count", 2), ("total", "$1.00"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Message.Of("cart.total", ("total", "$1.00"), ("count", 3)));
        Assert.NotEqual(a, new Message("cart.other"));
    }

    [Fact]
    public void Of_WritesNumbersInvariantly_NullAsEmpty_AndKeepsAMessage()
    {
        var date = new Message("format.date");

        var message = Message.Of("x", ("price", 1234.5m), ("none", null), ("when", date));

        Assert.Equal("1234.5", message.Args["price"]);
        Assert.Equal(string.Empty, message.Args["none"]); // never the text "null"
        Assert.Same(date, message.Args["when"]);
    }

    [Fact]
    public void AnArgument_IsAStringOrAMessage()
    {
        Assert.Throws<ArgumentException>(() => new Message("x", new Dictionary<string, object> { ["n"] = 1 }));
        Assert.Throws<ArgumentException>(() => new Message(" "));
        Assert.Equal("x(a=1, b=2)", Message.Of("x", ("b", 2), ("a", 1)).ToString());
    }
}
