namespace Wdc.Domain.Tests;

public sealed class User : IKeyedEntity
{
    public static readonly EntityModel<User> Model = EntityModel.For(() => new User())
        .Key(ValueKinds.Int64, "id", u => u.Id, (u, v) => u.Id = v)
        .Field(ValueKinds.String, "userName", u => u.UserName, (u, v) => u.UserName = v)
        .Field(ValueKinds.String, "password", u => u.Password, (u, v) => u.Password = v, inDefaultProjection: false)
        .Field(ValueKinds.String, "name", u => u.Name, (u, v) => u.Name = v)
        .Field(ValueKinds.Enum<Role>(), "role", u => u.Role, (u, v) => u.Role = v)
        .Build();

    public long? Id { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? Name { get; set; }

    public Role? Role { get; set; }

    object? IKeyedEntity.Key => Id;
}
