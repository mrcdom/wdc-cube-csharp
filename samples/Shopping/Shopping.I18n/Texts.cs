// Generated from Catalog/messages.*.json by samples/Shopping/generate.cs: edit the catalogs, not this file.
using Wdc.Cube;

namespace Shopping.I18n;

/// <summary>One message per key of the catalog, with an argument per placeholder.</summary>
public static class Texts
{
    /// <summary>corrected</summary>
    public static Message AdminCorrected() => new(MessageKeys.AdminCorrected);

    /// <summary>Customer name</summary>
    public static Message AdminCustomerName() => new(MessageKeys.AdminCustomerName);

    /// <summary>No purchases found</summary>
    public static Message AdminEmpty() => new(MessageKeys.AdminEmpty);

    /// <summary>Filter</summary>
    public static Message AdminFilter() => new(MessageKeys.AdminFilter);

    /// <summary>You do not have access to administration.</summary>
    public static Message AdminNotAllowed() => new(MessageKeys.AdminNotAllowed);

    /// <summary>#{id}</summary>
    public static Message AdminPurchaseNumber(object id) =>
        Message.Of(MessageKeys.AdminPurchaseNumber, ("id", id));

    /// <summary>Administration</summary>
    public static Message AdminTitle() => new(MessageKeys.AdminTitle);

    /// <summary>The purchase changed while you were correcting it. Check it and try again.</summary>
    public static Message AmendChangedMeanwhile() => new(MessageKeys.AmendChangedMeanwhile);

    /// <summary>Customer:</summary>
    public static Message AmendCustomer() => new(MessageKeys.AmendCustomer);

    /// <summary>The correction cannot remove every item.</summary>
    public static Message AmendEveryItemRemoved() => new(MessageKeys.AmendEveryItemRemoved);

    /// <summary>Previous corrections</summary>
    public static Message AmendHistory() => new(MessageKeys.AmendHistory);

    /// <summary>{product}: {before} → {after}</summary>
    public static Message AmendHistoryLine(object product, object before, object after) =>
        Message.Of(MessageKeys.AmendHistoryLine, ("product", product), ("before", before), ("after", after));

    /// <summary>New qty</summary>
    public static Message AmendNewQuantity() => new(MessageKeys.AmendNewQuantity);

    /// <summary>Lower or remove at least one item.</summary>
    public static Message AmendNothingToCorrect() => new(MessageKeys.AmendNothingToCorrect);

    /// <summary>The quantity can only go down (0 removes the item).</summary>
    public static Message AmendOnlyDown() => new(MessageKeys.AmendOnlyDown);

    /// <summary>Reason</summary>
    public static Message AmendReason() => new(MessageKeys.AmendReason);

    /// <summary>Enter the reason for the correction.</summary>
    public static Message AmendReasonRequired() => new(MessageKeys.AmendReasonRequired);

    /// <summary>Record correction</summary>
    public static Message AmendRecord() => new(MessageKeys.AmendRecord);

    /// <summary>Recording…</summary>
    public static Message AmendRecording() => new(MessageKeys.AmendRecording);

    /// <summary>Correction recorded.</summary>
    public static Message AmendSaved() => new(MessageKeys.AmendSaved);

    /// <summary>Purchase #{id}</summary>
    public static Message AmendTitle(object id) =>
        Message.Of(MessageKeys.AmendTitle, ("id", id));

    /// <summary>Buy</summary>
    public static Message CartBuy() => new(MessageKeys.CartBuy);

    /// <summary>Buying…</summary>
    public static Message CartBuying() => new(MessageKeys.CartBuying);

    /// <summary>The cart is empty: add products before buying.</summary>
    public static Message CartEmpty() => new(MessageKeys.CartEmpty);

    /// <summary>Add products to start shopping</summary>
    public static Message CartEmptyHint() => new(MessageKeys.CartEmptyHint);

    /// <summary>Your cart is empty</summary>
    public static Message CartEmptyTitle() => new(MessageKeys.CartEmptyTitle);

    /// <summary>Product not found in the cart.</summary>
    public static Message CartProductNotInCart() => new(MessageKeys.CartProductNotInCart);

    /// <summary>The quantity of items in the cart must be at least 1 (one).</summary>
    public static Message CartQuantityBelowOne() => new(MessageKeys.CartQuantityBelowOne);

    /// <summary>Remove</summary>
    public static Message CartRemove() => new(MessageKeys.CartRemove);

    /// <summary>Shopping Cart</summary>
    public static Message CartTitle() => new(MessageKeys.CartTitle);

    /// <summary>The purchase could not be recorded now. Please wait a moment and try again.</summary>
    public static Message CartUnavailable() => new(MessageKeys.CartUnavailable);

    /// <summary>Shopping</summary>
    public static Message CommonAppName() => new(MessageKeys.CommonAppName);

    /// <summary>Back</summary>
    public static Message CommonBack() => new(MessageKeys.CommonBack);

    /// <summary>Continue Shopping</summary>
    public static Message CommonContinueShopping() => new(MessageKeys.CommonContinueShopping);

    /// <summary>Item</summary>
    public static Message CommonItem() => new(MessageKeys.CommonItem);

    /// <summary>{count} items</summary>
    public static Message CommonItemCount(object count) =>
        Message.Of(MessageKeys.CommonItemCount, ("count", count));

    /// <summary>Next</summary>
    public static Message CommonNext() => new(MessageKeys.CommonNext);

    /// <summary>{page} / {pages}</summary>
    public static Message CommonPageOf(object page, object pages) =>
        Message.Of(MessageKeys.CommonPageOf, ("page", page), ("pages", pages));

    /// <summary>Previous</summary>
    public static Message CommonPrevious() => new(MessageKeys.CommonPrevious);

    /// <summary>Price</summary>
    public static Message CommonPrice() => new(MessageKeys.CommonPrice);

    /// <summary>{count} purchases</summary>
    public static Message CommonPurchaseCount(object count) =>
        Message.Of(MessageKeys.CommonPurchaseCount, ("count", count));

    /// <summary>Qty</summary>
    public static Message CommonQuantity() => new(MessageKeys.CommonQuantity);

    /// <summary>Total:</summary>
    public static Message CommonTotal() => new(MessageKeys.CommonTotal);

    /// <summary>Product code not found.</summary>
    public static Message ErrorProductNotFound() => new(MessageKeys.ErrorProductNotFound);

    /// <summary>Receipt code not found.</summary>
    public static Message ErrorReceiptNotFound() => new(MessageKeys.ErrorReceiptNotFound);

    /// <summary>An unexpected error occurred. Please try again shortly.</summary>
    public static Message ErrorUnexpected() => new(MessageKeys.ErrorUnexpected);

    /// <summary>{month}/{day}/{year}</summary>
    public static Message FormatDate(object month, object day, object year) =>
        Message.Of(MessageKeys.FormatDate, ("month", month), ("day", day), ("year", year));

    /// <summary>Cart</summary>
    public static Message HomeCart() => new(MessageKeys.HomeCart);

    /// <summary>Hello, {name}</summary>
    public static Message HomeGreeting(object name) =>
        Message.Of(MessageKeys.HomeGreeting, ("name", name));

    /// <summary>Sign out</summary>
    public static Message HomeSignOut() => new(MessageKeys.HomeSignOut);

    /// <summary>Could not reach the server. Check your connection.</summary>
    public static Message LoginCommunicationFailure() => new(MessageKeys.LoginCommunicationFailure);

    /// <summary>Username or password not recognized!</summary>
    public static Message LoginNotRecognized() => new(MessageKeys.LoginNotRecognized);

    /// <summary>Password</summary>
    public static Message LoginPassword() => new(MessageKeys.LoginPassword);

    /// <summary>Sign in</summary>
    public static Message LoginSignIn() => new(MessageKeys.LoginSignIn);

    /// <summary>Signing in…</summary>
    public static Message LoginSigningIn() => new(MessageKeys.LoginSigningIn);

    /// <summary>Sign in to continue</summary>
    public static Message LoginSubtitle() => new(MessageKeys.LoginSubtitle);

    /// <summary>Too many failed attempts. Try again in {seconds} seconds.</summary>
    public static Message LoginTooManyAttempts(object seconds) =>
        Message.Of(MessageKeys.LoginTooManyAttempts, ("seconds", seconds));

    /// <summary>Too many failed attempts. Try again later.</summary>
    public static Message LoginTooManyAttemptsLater() => new(MessageKeys.LoginTooManyAttemptsLater);

    /// <summary>Username</summary>
    public static Message LoginUsername() => new(MessageKeys.LoginUsername);

    /// <summary>Add to Cart</summary>
    public static Message ProductAddToCart() => new(MessageKeys.ProductAddToCart);

    /// <summary>Qty:</summary>
    public static Message ProductQuantity() => new(MessageKeys.ProductQuantity);

    /// <summary>No products available</summary>
    public static Message ProductsEmpty() => new(MessageKeys.ProductsEmpty);

    /// <summary>Loading…</summary>
    public static Message ProductsLoading() => new(MessageKeys.ProductsLoading);

    /// <summary>Products</summary>
    public static Message ProductsTitle() => new(MessageKeys.ProductsTitle);

    /// <summary>No purchases yet</summary>
    public static Message PurchasesEmpty() => new(MessageKeys.PurchasesEmpty);

    /// <summary>Purchases</summary>
    public static Message PurchasesTitle() => new(MessageKeys.PurchasesTitle);

    /// <summary>Amount</summary>
    public static Message ReceiptAmount() => new(MessageKeys.ReceiptAmount);

    /// <summary>Purchase corrected on {date}: {reason}</summary>
    public static Message ReceiptCorrection(object date, object reason) =>
        Message.Of(MessageKeys.ReceiptCorrection, ("date", date), ("reason", reason));

    /// <summary>Purchase completed successfully!</summary>
    public static Message ReceiptPurchaseCompleted() => new(MessageKeys.ReceiptPurchaseCompleted);

    /// <summary>Receipt</summary>
    public static Message ReceiptTitle() => new(MessageKeys.ReceiptTitle);
}
