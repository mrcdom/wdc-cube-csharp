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
            var admin = await NewUser(repositories, "admin", "admin", "João da Silva", "Admin", cancellationToken);
            await NewUser(repositories, "fulano", "fulano", "Fulano de Tal", "Customer", cancellationToken);
            await NewUser(repositories, "beotrano", "beotrano", "Beotrano de Alguma Coisa", "Customer", cancellationToken);
            await NewUser(repositories, "gerente", "gerente", "Gerente de Loja", "Manager", cancellationToken);
            await NewUser(repositories, "suporte", "suporte", "Suporte Técnico", "Super", cancellationToken); // maintenance

            var coffee = await NewProduct(repositories, "Cafeteira design italiano", 199.99m, CoffeeMaker, "cafeteira.png", cancellationToken);
            var ball = await NewProduct(repositories, "Bola de vôlei", 45.30m, VolleyBall, "bola.png", cancellationToken);
            var tape = await NewProduct(repositories, "Fita veda rosca", 2.67m, ThreadTape, "vedarosca.png", cancellationToken);
            await NewProduct(repositories, "Pen Drive 2GB", 16.00m, PenDrive, "pendrive2gb.png", cancellationToken);

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
        <li>Prepara até 30 xícaras pequenas</li>
        <li>Jarra de vidro com alça</li>
        <li>Filtro permanente removível</li>
        <li>Reservatório de água com marcação de nível</li>
        <li>Luz indicadora de ligada</li>
        <li>Peças removíveis fáceis de lavar</li>
        </ul>
        """;

    private const string VolleyBall = """
        <ul>
        <li>Tamanho e peso oficiais.</li>
        <li>Garantia contra defeito de fabricação.</li>
        <li>Revestimento sintético.</li>
        </ul>
        """;

    private const string ThreadTape = """
        <ul>
        <li>Fita de vedação para roscas.</li>
        <li>Medida: 18 mm x 10 m.</li>
        <li>Material: PTFE.</li>
        <li>Para juntas roscáveis de água e gás.</li>
        </ul>
        """;

    private const string PenDrive =
        "Pen drive de 2 GB para levar documentos, fotos e músicas no bolso. Leve, com luz que indica o uso e conexão " +
        "USB.";
}
