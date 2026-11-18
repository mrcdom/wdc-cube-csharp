using Shopping.Domain.Products;
using Shopping.Domain.Purchases;
using Shopping.Domain.Users;
using Wdc.Domain;

namespace Shopping.Domain;

/// <summary>The repositories and the transaction service, as one thing the presentation receives.</summary>
public sealed record ShoppingRepositories(
    IUserRepository Users,
    IProductRepository Products,
    IPurchaseRepository Purchases,
    IPurchaseItemRepository PurchaseItems,
    ITransactionService Transactions);

/// <summary>A rule of the business was broken; the message is meant for the user.</summary>
public class BusinessException : Exception
{
    public BusinessException()
    {
    }

    public BusinessException(string message)
        : base(message)
    {
    }

    public BusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A cart line cannot be bought (quantity below one).</summary>
public sealed class InvalidCartItemException : BusinessException
{
    public InvalidCartItemException()
    {
    }

    public InvalidCartItemException(string message)
        : base(message)
    {
    }

    public InvalidCartItemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
