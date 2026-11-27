// Generated from Catalog/messages.*.json by samples/Shopping/generate.cs: edit the catalogs, not this file.
using Wdc.Cube;

namespace Shopping.I18n;

/// <summary>One message per key of the catalog, with an argument per placeholder.</summary>
public static class Texts
{
    /// <summary>You do not have access to administration.</summary>
    public static Message AdminNotAllowed() => new(MessageKeys.AdminNotAllowed);

    /// <summary>#{id}</summary>
    public static Message AdminPurchaseNumber(object id) =>
        Message.Of(MessageKeys.AdminPurchaseNumber, ("id", id));

    /// <summary>The purchase changed while you were correcting it. Check it and try again.</summary>
    public static Message AmendChangedMeanwhile() => new(MessageKeys.AmendChangedMeanwhile);

    /// <summary>The correction cannot remove every item.</summary>
    public static Message AmendEveryItemRemoved() => new(MessageKeys.AmendEveryItemRemoved);

    /// <summary>{product}: {before} → {after}</summary>
    public static Message AmendHistoryLine(object product, object before, object after) =>
        Message.Of(MessageKeys.AmendHistoryLine, ("product", product), ("before", before), ("after", after));

    /// <summary>Lower or remove at least one item.</summary>
    public static Message AmendNothingToCorrect() => new(MessageKeys.AmendNothingToCorrect);

    /// <summary>The quantity can only go down (0 removes the item).</summary>
    public static Message AmendOnlyDown() => new(MessageKeys.AmendOnlyDown);

    /// <summary>Enter the reason for the correction.</summary>
    public static Message AmendReasonRequired() => new(MessageKeys.AmendReasonRequired);

    /// <summary>Correction recorded.</summary>
    public static Message AmendSaved() => new(MessageKeys.AmendSaved);

    /// <summary>Purchase #{id}</summary>
    public static Message AmendTitle(object id) =>
        Message.Of(MessageKeys.AmendTitle, ("id", id));

    /// <summary>The cart is empty: add products before buying.</summary>
    public static Message CartEmpty() => new(MessageKeys.CartEmpty);

    /// <summary>Product not found in the cart.</summary>
    public static Message CartProductNotInCart() => new(MessageKeys.CartProductNotInCart);

    /// <summary>The quantity of items in the cart must be at least 1 (one).</summary>
    public static Message CartQuantityBelowOne() => new(MessageKeys.CartQuantityBelowOne);

    /// <summary>The purchase could not be recorded now. Please wait a moment and try again.</summary>
    public static Message CartUnavailable() => new(MessageKeys.CartUnavailable);

    /// <summary>{count} items</summary>
    public static Message CommonItemCount(object count) =>
        Message.Of(MessageKeys.CommonItemCount, ("count", count));

    /// <summary>{page} / {pages}</summary>
    public static Message CommonPageOf(object page, object pages) =>
        Message.Of(MessageKeys.CommonPageOf, ("page", page), ("pages", pages));

    /// <summary>{count} purchases</summary>
    public static Message CommonPurchaseCount(object count) =>
        Message.Of(MessageKeys.CommonPurchaseCount, ("count", count));

    /// <summary>Product code not found.</summary>
    public static Message ErrorProductNotFound() => new(MessageKeys.ErrorProductNotFound);

    /// <summary>Receipt code not found.</summary>
    public static Message ErrorReceiptNotFound() => new(MessageKeys.ErrorReceiptNotFound);

    /// <summary>An unexpected error occurred. Please try again shortly.</summary>
    public static Message ErrorUnexpected() => new(MessageKeys.ErrorUnexpected);

    /// <summary>{month}/{day}/{year}</summary>
    public static Message FormatDate(object month, object day, object year) =>
        Message.Of(MessageKeys.FormatDate, ("month", month), ("day", day), ("year", year));

    /// <summary>Hello, {name}</summary>
    public static Message HomeGreeting(object name) =>
        Message.Of(MessageKeys.HomeGreeting, ("name", name));

    /// <summary>Could not reach the server. Check your connection.</summary>
    public static Message LoginCommunicationFailure() => new(MessageKeys.LoginCommunicationFailure);

    /// <summary>Username or password not recognized!</summary>
    public static Message LoginNotRecognized() => new(MessageKeys.LoginNotRecognized);

    /// <summary>Too many failed attempts. Try again in {seconds} seconds.</summary>
    public static Message LoginTooManyAttempts(object seconds) =>
        Message.Of(MessageKeys.LoginTooManyAttempts, ("seconds", seconds));

    /// <summary>Too many failed attempts. Try again later.</summary>
    public static Message LoginTooManyAttemptsLater() => new(MessageKeys.LoginTooManyAttemptsLater);

    /// <summary>Purchase corrected on {date}: {reason}</summary>
    public static Message ReceiptCorrection(object date, object reason) =>
        Message.Of(MessageKeys.ReceiptCorrection, ("date", date), ("reason", reason));
}
