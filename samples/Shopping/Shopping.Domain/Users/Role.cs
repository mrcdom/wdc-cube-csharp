namespace Shopping.Domain.Users;

/// <summary>
/// The roles. <see cref="Super"/> is for the maintenance of the solution; the others are the roles of the application's
/// users, and they follow the business rules in every case (decision D-24).
/// </summary>
public enum Role
{
    Admin,
    Customer,
    Manager,
    Super,
}
