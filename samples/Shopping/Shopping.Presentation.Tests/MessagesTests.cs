using System.Globalization;
using System.Text.RegularExpressions;
using Shopping.Generators;
using Shopping.I18n;
using Wdc.Cube;

namespace Shopping.Presentation.Tests;

/// <summary>The catalogs agree with each other, and a view translates a message into its language.</summary>
public sealed partial class MessagesTests
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
        Assert.Equal(string.Empty, Messages.Default.Text((Message?)null));
    }

    [Fact]
    public void TheCurrentCatalog_IsTheOneOfTheUICulture()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
            Assert.Equal("Carrinho de compras", Messages.Current.Text(MessageKeys.CartTitle));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal("Shopping Cart", Messages.Current.Text(MessageKeys.CartTitle));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void TheDesktopViews_UseOnlyKeysOfTheCatalog()
    {
        // The web views name the keys by MessageKeys (the compiler checks them); the desktop markup names them as text.
        var keys = GeneratedFilesTests.Catalogs()[MessageCatalog.DefaultLanguage];
        var used = Directory.GetFiles(Path.Combine(GeneratedFilesTests.Shopping, "Shopping.Avalonia"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => FixedText().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(used);
        Assert.Empty(used.Where(u => !keys.ContainsKey(u.Key)).Select(u => $"{u.File}: {u.Key}"));
    }

    [GeneratedRegex(@"\{v:T\s+([^}\s]+)\s*\}")]
    private static partial Regex FixedText();
}
