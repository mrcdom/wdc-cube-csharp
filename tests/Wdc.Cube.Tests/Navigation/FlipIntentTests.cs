namespace Wdc.Cube.Tests.Navigation;

/// <summary>Address format: FlipIntent to and from its string form.</summary>
public sealed class FlipIntentTests
{
    private static readonly Place s_root = Place.Create("root");

    [Fact]
    public void ToString_SimpleValues()
    {
        var intent = new FlipIntent(s_root);
        intent.SetParameter("p0", 1L);
        intent.SetParameter("p1", 1.1);
        intent.SetParameter("p2", true);
        intent.SetParameter("p3", "a b c");

        Assert.Equal("root?p0=1&p1=1.1&p2=true&p3=a+b+c", intent.ToString());
    }

    [Fact]
    public void Parse_SimpleValues_RoundTrips()
    {
        const string expected = "root?p0=1&p1=1.1&p2=true&p3=a+b+c";
        Assert.Equal(expected, FlipIntent.Parse(expected).ToString());
    }

    [Fact]
    public void ToString_MultipleValues()
    {
        var intent = new FlipIntent(s_root);
        intent.SetParameter("p0", [1L, 2L]);
        intent.SetParameter("p1", [1.1, 2.2]);
        intent.SetParameter("p2", [true, false]);
        intent.SetParameter("p3", ["a", "b", "c"]);

        Assert.Equal("root?p0=1&p0=2&p1=1.1&p1=2.2&p2=true&p2=false&p3=a&p3=b&p3=c", intent.ToString());
    }

    [Fact]
    public void Parse_MultipleValues_RoundTrips()
    {
        const string expected = "root?p0=1&p0=2&p1=1.1&p1=2.2&p2=true&p2=false&p3=a&p3=b&p3=c";
        Assert.Equal(expected, FlipIntent.Parse(expected).ToString());
    }

    [Fact]
    public void TypedGetters_ReadWhatWasWritten()
    {
        var intent = FlipIntent.Parse("root?n=42&d=2.5&b=true&s=a+b&m=1&m=2");

        Assert.Equal(42L, intent.GetInt64("n"));
        Assert.Equal(42, intent.GetInt32("n"));
        Assert.Equal(2.5, intent.GetDouble("d"));
        Assert.True(intent.GetBoolean("b"));
        Assert.Equal("a b", intent.GetString("s"));
        Assert.Equal(["1", "2"], intent.GetValues("m"));
        Assert.Equal(1L, intent.GetInt64("m")); // multivalued: the first one
    }

    [Fact]
    public void TypedGetters_UnreadableOrAbsent_GiveNullOrDefault()
    {
        // The address is outside input: anything can arrive in it.
        var intent = FlipIntent.Parse("root?n=abc&d=NaN&b=yes&big=99999999999");

        Assert.Null(intent.GetInt64("n"));
        Assert.Equal(7L, intent.GetInt64("n", 7));
        Assert.Null(intent.GetDouble("d"));
        Assert.Null(intent.GetBoolean("b"));
        Assert.Null(intent.GetInt32("big"));
        Assert.Equal(99999999999L, intent.GetInt64("big"));
        Assert.Null(intent.GetString("missing"));
        Assert.Empty(intent.GetValues("missing"));
    }

    [Fact]
    public void SetParameter_Null_RemovesIt()
    {
        var intent = new FlipIntent(s_root);
        intent.SetParameter("a", 1L);
        intent.SetParameter("b", "x");

        intent.SetParameter("a", (long?)null);
        intent.SetParameter("b", (string?)null);

        Assert.True(intent.HasNoParameters);
        Assert.Equal("root", intent.ToString());
    }

    [Fact]
    public void Encoding_IsFormUrlEncodedUtf8()
    {
        var intent = new FlipIntent(s_root);
        intent.SetParameter("q", "café & ação=1/2 😀");

        var text = intent.ToString();
        Assert.Equal("root?q=caf%C3%A9+%26+a%C3%A7%C3%A3o%3D1%2F2+%F0%9F%98%80", text);
        Assert.Equal("café & ação=1/2 😀", FlipIntent.Parse(text).GetString("q"));
    }

    [Fact]
    public void Parse_ToleratesMalformedPercentAndEmptyPieces()
    {
        var intent = FlipIntent.Parse("root?a=100%&&b=%zz&=x&c");

        Assert.Equal("100%", intent.GetString("a"));
        Assert.Equal("%zz", intent.GetString("b"));
        Assert.Equal(string.Empty, intent.GetString("c"));
    }

    [Fact]
    public void Parse_WithoutResolver_GivesADetachedPlaceCarryingThePath()
    {
        var intent = FlipIntent.Parse("orders/detail?id=1");

        Assert.True(intent.Place.IsDetached);
        Assert.Equal("orders/detail?id=1", intent.ToString());
        Assert.Same(Place.Unknown, FlipIntent.Parse("").Place);
    }

    [Fact]
    public void Redirect_CopiesParametersAndAttributes()
    {
        var other = Place.Create("other");
        var intent = new FlipIntent(s_root);
        intent.SetParameter("a", 1L);
        intent.SetAttribute("attr", "value");

        var redirected = intent.Redirect(other);

        Assert.Same(other, redirected.Place);
        Assert.Equal(1L, redirected.GetInt64("a"));
        Assert.Equal("value", redirected.GetAttribute<string>("attr"));
    }

    [Fact]
    public void ScopeSlot_MissingSlotIsANoop()
    {
        var intent = new FlipIntent(s_root);
        var slot = intent.GetScopeSlot("parent");

        slot(null); // does not throw
        Assert.Same(ScopeSlots.Noop, slot);
    }
}
