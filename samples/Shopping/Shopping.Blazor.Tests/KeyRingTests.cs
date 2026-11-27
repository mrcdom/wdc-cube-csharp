using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Blazor.Infra;
using Shopping.Presentation.Tests;

namespace Shopping.Blazor.Tests;

/// <summary>Two instances of the host over the same database read what the other protected (a reload on another one).</summary>
public sealed class KeyRingTests(ShoppingDatabaseFixture db) : IClassFixture<ShoppingDatabaseFixture>
{
    private IDataProtector Instance()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("wdc-shopping")
            .AddKeyManagementOptions(o => o.XmlRepository = new ShoppingKeyRing(db.Store!.Security));
        return services.BuildServiceProvider().GetDataProtector("session");
    }

    [Fact]
    public void WhatOneInstanceProtects_AnotherReads()
    {
        Assert.SkipWhen(db.Store is null, db.SkipReason);
        var protectedText = Instance().Protect("cart of fulano");

        Assert.Equal("cart of fulano", Instance().Unprotect(protectedText));
    }
}
