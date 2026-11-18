using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Shopping.Server;

/// <summary>Token settings (decision D-19).</summary>
public sealed class ShoppingAuthOptions
{
    public string Issuer { get; set; } = "wdc-shopping";

    /// <summary>The HMAC key that signs the tokens (32 bytes or more).</summary>
    public byte[] Key { get; set; } = RandomNumberGenerator.GetBytes(32);

    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshLifetime { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The master key the users' address keys derive from (decision D-22).</summary>
    public byte[] HistoryMasterKey { get; set; } = RandomNumberGenerator.GetBytes(32);

    public SymmetricSecurityKey SigningKey => new(Key);
}
