using Shopping.Domain;
using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Persistence;

/// <summary>The demo data: users of each role, products with images, two purchases of the admin.</summary>
public static class ShoppingDemoData
{
    public static async Task Load(ShoppingRepositories repositories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        await repositories.Transactions.Required(async _ =>
        {
            var admin = await NewUser(repositories, "admin", "admin", "John Smith", "Admin", cancellationToken);
            await NewUser(repositories, "fulano", "fulano", "John Doe", "Customer", cancellationToken);
            await NewUser(repositories, "beotrano", "beotrano", "Jane Roe", "Customer", cancellationToken);
            await NewUser(repositories, "gerente", "gerente", "Store Manager", "Manager", cancellationToken);
            await NewUser(repositories, "suporte", "suporte", "Support Team", "Super", cancellationToken); // maintenance

            var coffee = await NewProduct(repositories, "Italian Design Coffee Maker", 199.99m, CoffeeMaker, "coffeemaker.png", cancellationToken);
            var ball = await NewProduct(repositories, "Volleyball", 45.30m, VolleyBall, "volleyball.png", cancellationToken);
            var tape = await NewProduct(repositories, "Thread Seal Tape", 2.67m, ThreadTape, "threadsealtape.png", cancellationToken);
            await NewProduct(repositories, "USB Flash Drive 2GB", 16.00m, FlashDrive, "flashdrive2gb.png", cancellationToken);

            await repositories.Purchases.Insert(new Purchase
            {
                User = admin,
                BuyDate = new DateTimeOffset(2010, 1, 1, 14, 30, 0, TimeSpan.Zero),
                Items = [new PurchaseItem { Product = coffee, Amount = 1, Price = 200.00m }],
            }, cancellationToken: cancellationToken);
            await repositories.Purchases.Insert(new Purchase
            {
                User = admin,
                BuyDate = new DateTimeOffset(2011, 4, 3, 9, 15, 0, TimeSpan.Zero),
                Items =
                [
                    new PurchaseItem { Product = ball, Amount = 1, Price = 45.30m },
                    new PurchaseItem { Product = tape, Amount = 1, Price = 2.67m },
                ],
            }, cancellationToken: cancellationToken);
        }, cancellationToken);
    }

    private static async Task<User> NewUser(ShoppingRepositories r, string login, string password, string name, string roles,
        CancellationToken cancellationToken)
    {
        var user = new User { UserName = login, Password = PasswordHash.Hash(password), Name = name, Roles = roles };
        await r.Users.Insert(user, cancellationToken: cancellationToken);
        return user;
    }

    private static async Task<Product> NewProduct(ShoppingRepositories r, string name, decimal price, string description,
        string image, CancellationToken cancellationToken)
    {
        var product = new Product { Name = name, Price = price, Description = description, Image = Image(image) };
        await r.Products.Insert(product, cancellationToken: cancellationToken);
        return product;
    }

    private static byte[] Image(string file)
    {
        using var stream = typeof(ShoppingDemoData).Assembly.GetManifestResourceStream($"Shopping.Persistence.Images.{file}")
                           ?? throw new InvalidOperationException($"Missing embedded image {file}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private const string CoffeeMaker = """
        <ul>
        <li>Capacity for 30 cups (50ml each) or 24 cups (62ml each)</li>
        <li>Drip-stop system</li>
        <li>Comes with a removable permanent filter and a measuring spoon</li>
        <li>Paper filters can also be used</li>
        <li>Graduated water reservoir</li>
        <li>Illuminated on/off switch</li>
        <li>Easy to clean</li>
        <li>Dishwasher-safe parts (except the motor base)</li>
        <li>Power: 1000W - equivalent to 1 kWh (kilowatt-hour).</li>
        </ul>
        """;

    private const string VolleyBall = """
        <ul>
        <li>Volleyball, official size and weight.</li>
        <li>Warranty: against manufacturing defects.</li>
        <li>Origin: imported.</li>
        </ul>
        """;

    private const string ThreadTape = """
        <ul>
        <li>Size: 18 mm x 10 m.</li>
        <li>Material: PTFE.</li>
        <li>Use: sealing threaded joints.</li>
        </ul>
        """;

    private const string FlashDrive =
        "Ideal for carrying data files, audio, video, photos and much more. Best value for storing and transferring " +
        "information. Portable, easy to use and super light, it keeps your data safe, has an LED that shows when it is " +
        "in use, and is drop resistant. A flash drive with 2 GB of storage capacity: convenience and quality for your files!";
}
