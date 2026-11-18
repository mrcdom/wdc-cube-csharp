using Wdc.Domain;

namespace Shopping.Domain.Users;

public sealed class User : IKeyedEntity
{
    public static readonly EntityModel<User> Model = EntityModel.For(() => new User())
        .Key(ValueKinds.Int64, "id", u => u.Id, (u, v) => u.Id = v)
        .Field(ValueKinds.String, "userName", u => u.UserName, (u, v) => u.UserName = v)
        .Field(ValueKinds.String, "password", u => u.Password, (u, v) => u.Password = v, inDefaultProjection: false)
        .Field(ValueKinds.String, "name", u => u.Name, (u, v) => u.Name = v)
        .Field(ValueKinds.String, "roles", u => u.Roles, (u, v) => u.Roles = v)
        .Build();

    public long? Id { get; set; }

    /// <summary>The login.</summary>
    public string? UserName { get; set; }

    /// <summary>The password hash (<see cref="PasswordHash"/>). Never in the default projection.</summary>
    public string? Password { get; set; }

    /// <summary>The display name.</summary>
    public string? Name { get; set; }

    /// <summary>Comma-separated <see cref="Role"/> names.</summary>
    public string? Roles { get; set; }

    object? IKeyedEntity.Key => Id;
}
