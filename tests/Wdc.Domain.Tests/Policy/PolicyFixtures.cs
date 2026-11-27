namespace Wdc.Domain.Tests.Policy;

// A neutral domain for the policy tests: customers, their orders and the order lines.

internal sealed class Customer
{
    public static readonly EntityModel<Customer> Model = EntityModel.For(() => new Customer(), "customer")
        .Key(ValueKinds.Int64, "id", c => c.Id, (c, v) => c.Id = v)
        .Field(ValueKinds.String, "login", c => c.Login, (c, v) => c.Login = v)
        .Field(ValueKinds.String, "name", c => c.Name, (c, v) => c.Name = v)
        .Field(ValueKinds.String, "secret", c => c.Secret, (c, v) => c.Secret = v)
        .Build();

    public long? Id { get; set; }

    public string? Login { get; set; }

    public string? Name { get; set; }

    public string? Secret { get; set; }
}

internal enum CustomerOrderBy
{
    OldestFirst,
    ByName,
}

internal sealed class CustomerCriteria : Criteria<CustomerCriteria, Customer, CustomerOrderBy>
{
    public CustomerCriteria()
    {
        CustomerId = Comparable(ValueKinds.Int64, "customerId");
        Login = Text("login");
        Name = Text("name");
    }

    public ComparableCriterion<CustomerCriteria, long?> CustomerId { get; }

    public TextCriterion<CustomerCriteria> Login { get; }

    public TextCriterion<CustomerCriteria> Name { get; }
}

internal sealed class Order
{
    public static readonly EntityModel<Order> Model = EntityModel.For(() => new Order(), "order")
        .Key(ValueKinds.Int64, "id", o => o.Id, (o, v) => o.Id = v)
        .Field(ValueKinds.String, "status", o => o.Status, (o, v) => o.Status = v)
        .Reference("customer", o => o.Customer, (o, v) => o.Customer = v, () => Customer.Model)
        .Collection("lines", o => o.Lines, (o, v) => o.Lines = v, () => OrderLine.Model, () => new OrderLineCriteria())
        .Build();

    public long? Id { get; set; }

    public string? Status { get; set; }

    public Customer? Customer { get; set; }

    public List<OrderLine>? Lines { get; set; }
}

internal sealed class OrderCriteria : Criteria<OrderCriteria, Order>
{
    public OrderCriteria()
    {
        OrderId = Comparable(ValueKinds.Int64, "orderId");
        CustomerId = Comparable(ValueKinds.Int64, "customerId");
        CustomerName = Text("customerName");
        Status = Text("status");
    }

    public ComparableCriterion<OrderCriteria, long?> OrderId { get; }

    public ComparableCriterion<OrderCriteria, long?> CustomerId { get; }

    public TextCriterion<OrderCriteria> CustomerName { get; }

    public TextCriterion<OrderCriteria> Status { get; }
}

internal sealed class OrderLine
{
    public static readonly EntityModel<OrderLine> Model = EntityModel.For(() => new OrderLine(), "order-line")
        .Key(ValueKinds.Int64, "id", l => l.Id, (l, v) => l.Id = v)
        .Field(ValueKinds.Int32, "quantity", l => l.Quantity, (l, v) => l.Quantity = v)
        .Reference("order", l => l.Order, (l, v) => l.Order = v, () => Order.Model)
        .Build();

    public long? Id { get; set; }

    public int? Quantity { get; set; }

    public Order? Order { get; set; }
}

internal sealed class OrderLineCriteria : Criteria<OrderLineCriteria, OrderLine>
{
    public OrderLineCriteria() => CustomerId = Comparable(ValueKinds.Int64, "customerId");

    public ComparableCriterion<OrderLineCriteria, long?> CustomerId { get; }
}

/// <summary>The actor's own customer row.</summary>
internal sealed class OwnCustomer : IRowScope<Customer, CustomerCriteria>
{
    public static readonly OwnCustomer Scope = new();

    public void Restrict(CustomerCriteria group, Actor actor) => group.CustomerId.Eq(long.Parse(actor.Id, System.Globalization.CultureInfo.InvariantCulture));

    public ValueTask<bool> Covers(Customer row, Actor actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(row.Id == Policies.IdOf(actor));
}

/// <summary>Rows named "Team": a scope that is not about the key, to test moving a row out of reach.</summary>
internal sealed class Team : IRowScope<Customer, CustomerCriteria>
{
    public static readonly Team Scope = new();

    public void Restrict(CustomerCriteria group, Actor actor) => group.Name.Eq("Team");

    public ValueTask<bool> Covers(Customer row, Actor actor, CancellationToken cancellationToken) => ValueTask.FromResult(row.Name == "Team");

    public Customer? Projection() => new() { Name = Mark.String };
}

internal sealed class OwnOrders : IRowScope<Order, OrderCriteria>
{
    public static readonly OwnOrders Scope = new();

    public void Restrict(OrderCriteria group, Actor actor) => group.CustomerId.Eq(Policies.IdOf(actor));

    public ValueTask<bool> Covers(Order row, Actor actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(row.Customer?.Id == Policies.IdOf(actor));

    public Order? Projection() => new() { Customer = new Customer { Id = Mark.Int64 } };
}

internal sealed class OwnLines : IRowScope<OrderLine, OrderLineCriteria>
{
    public static readonly OwnLines Scope = new();

    public void Restrict(OrderLineCriteria group, Actor actor) => group.CustomerId.Eq(Policies.IdOf(actor));

    public ValueTask<bool> Covers(OrderLine row, Actor actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(row.Order?.Customer?.Id == Policies.IdOf(actor));

    public OrderLine? Projection() => new() { Order = new Order { Customer = new Customer { Id = Mark.Int64 } } };
}

internal static class Policies
{
    public const long Me = 7;

    public static Actor As(params string[] roles) => new(Me.ToString(System.Globalization.CultureInfo.InvariantCulture), roles);

    public static long IdOf(Actor actor) => long.Parse(actor.Id, System.Globalization.CultureInfo.InvariantCulture);

    public static DataPolicyBuilder<Customer, CustomerCriteria> CustomerBase() =>
        DataPolicy.For(Customer.Model, () => new CustomerCriteria())
            .WriteOnly("secret")
            .Criterion("customerId", "id")
            .Orderings(c => c.OrderBy == CustomerOrderBy.ByName ? ["name"] : []);

    public static readonly DataPolicy<Customer, CustomerCriteria> CustomerPolicy = CustomerBase()
        .Grant(["ADMIN"], g => g.Read("login", "name").Create("login", "name", "secret").Update("login", "name", "secret").Delete())
        .Grant(["CLIENT"], g => g.Rows(OwnCustomer.Scope).Read("login", "name"))
        .Build();

    public static readonly DataPolicy<Order, OrderCriteria> OrderPolicy = DataPolicy.For(Order.Model, () => new OrderCriteria())
        .Reference("customer", () => CustomerPolicy)
        .Collection("lines", inverse: "order", () => LinePolicy!) // declared below: read when a request reaches the lines
        .Criterion("orderId", "id")
        .Criterion("customerId", "customer")
        .Criterion("customerName", "customer.name")
        .DefaultShape(() => new Order
        {
            Status = Mark.String,
            Customer = new Customer { Name = Mark.String, Login = Mark.String },
            Lines = Mark.List(new OrderLine { Quantity = Mark.Int32 }),
        })
        .Grant(["ADMIN"], g => g.Read("status", "customer", "lines").Create("status", "customer", "lines").Update("status", "customer").Delete())
        .Grant(["CLIENT"], g => g.Rows(OwnOrders.Scope).Read("status", "customer", "lines").Create("status", "customer", "lines"))
        .Build();

    public static readonly DataPolicy<OrderLine, OrderLineCriteria> LinePolicy = DataPolicy.For(OrderLine.Model, () => new OrderLineCriteria())
        .Reference("order", () => OrderPolicy)
        .Criterion("customerId", "order")
        .Grant(["ADMIN"], g => g.Read("quantity", "order").Create("quantity", "order").Delete())
        .Grant(["CLIENT"], g => g.Rows(OwnLines.Scope).Read("quantity", "order").Create("quantity", "order"))
        .Build();
}
