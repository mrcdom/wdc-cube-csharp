using Shopping.Persistence;

namespace Shopping.Presentation.Tests;

public sealed class ShoppingSqlServerFixture() : ShoppingDatabaseFixture(DatabaseProvider.SqlServer, "WDC_TEST_SQLSERVER");
