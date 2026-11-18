using System.Security.Cryptography;
using System.Text;

namespace Shopping.Domain.Users;

/// <summary>
/// The address key of each user (decision D-22): derived from a master key the server keeps, so nothing is stored and
/// the same user always gets the same key (their links keep opening) while another user's key does not open them.
/// </summary>
public static class HistoryKeys
{
    /// <summary>32 bytes for <paramref name="userId"/>, as Base64url text: <c>HKDF-SHA256(master, info = "wdc-shopping-history:" + id)</c>.</summary>
    public static string Derive(byte[] master, long userId)
    {
        ArgumentNullException.ThrowIfNull(master);
        var info = Encoding.UTF8.GetBytes("wdc-shopping-history:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, salt: [], info: info);
        return Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
