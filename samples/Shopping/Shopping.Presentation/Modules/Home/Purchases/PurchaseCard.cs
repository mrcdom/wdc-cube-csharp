namespace Shopping.Presentation.Modules.Home.Purchases;

/// <summary>A purchase card: date, what was bought, total.</summary>
public sealed record PurchaseCard(long Id, string DateLabel, string ItemsLabel, string TotalLabel);
