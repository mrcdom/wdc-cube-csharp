namespace Shopping.Presentation.Modules.Home.Products;

/// <summary>A product card. Read-only data: replaced as a whole when the list reloads.</summary>
public sealed record ProductCard(long Id, string Name, string PriceLabel, string ImageUrl);
