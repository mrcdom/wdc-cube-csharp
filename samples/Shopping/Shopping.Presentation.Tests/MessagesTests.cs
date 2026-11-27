using Shopping.Generators;
using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Presentation.Tests;

/// <summary>The catalogs agree with each other, and a view translates a message into its language.</summary>
public sealed class MessagesTests
{
    [Fact]
    public void TheCatalogs_HaveTheSameKeysAndPlaceholders()
    {
        Assert.Empty(MessageCatalog.Check(GeneratedFilesTests.Catalogs()));
    }

    [Fact]
    public void ALanguage_IsChosenByTag_ThenByLanguage_ThenTheDefault()
    {
        Assert.Equal("pt-BR", Messages.For("pt-BR").Language);
        Assert.Equal("pt-BR", Messages.For("pt-PT").Language);
        Assert.Equal("pt-BR", Messages.For("PT").Language);
        Assert.Equal("en-US", Messages.For("fr-FR").Language);
        Assert.Equal("en-US", Messages.For(null).Language);
    }

    [Fact]
    public void Placeholders_AreFilled_AMessageArgumentIsTranslatedToo()
    {
        var date = Texts.FormatDate(year: 2026, month: "11", day: "27");
        var correction = Texts.ReceiptCorrection(date: date, reason: "Short delivery");

        Assert.Equal("Purchase corrected on 11/27/2026: Short delivery", Messages.For("en-US").Text(correction));
        Assert.Equal("Compra corrigida em 27/11/2026: Short delivery", Messages.For("pt-BR").Text(correction));
    }

    [Theory]
    [InlineData(1, "1 item", "1 item")]
    [InlineData(2, "2 items", "2 itens")]
    [InlineData(0, "0 items", "0 itens")]
    public void ACount_ChoosesOneOrOther(int count, string english, string portuguese)
    {
        Assert.Equal(english, Messages.For("en-US").Text(Texts.CommonItemCount(count: count)));
        Assert.Equal(portuguese, Messages.For("pt-BR").Text(Texts.CommonItemCount(count: count)));
    }

    [Fact]
    public void AnUnknownKey_ShowsTheKey_AMissingArgument_ThePlaceholder_AndNoMessage_Nothing()
    {
        Assert.Equal("no.such.key", Messages.Default.Text(new Message("no.such.key")));
        Assert.Equal("Hello, {name}", Messages.Default.Text(new Message(MessageKeys.HomeGreeting)));
        Assert.Equal(string.Empty, Messages.Default.Text(null));
    }
}
