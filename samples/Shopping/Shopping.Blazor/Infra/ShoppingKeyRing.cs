using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Shopping.Domain.Security;

namespace Shopping.Blazor.Infra;

/// <summary>The data protection key ring in the Shopping database, shared by every instance of the host.</summary>
/// <remarks>
/// The keys are stored as data protection writes them, without an XML encryptor: whoever reads the table can read
/// the session storage of the browsers. Data protection calls this rarely (start, key rotation) and synchronously.
/// </remarks>
public sealed class ShoppingKeyRing(ISecurityStore store) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements() =>
        [.. store.Keys().GetAwaiter().GetResult().Select(xml => XElement.Parse(xml))];

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        store.SaveKey(friendlyName, element.ToString(SaveOptions.DisableFormatting)).GetAwaiter().GetResult();
    }
}
