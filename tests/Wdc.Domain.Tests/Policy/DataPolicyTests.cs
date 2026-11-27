using static Wdc.Domain.Tests.Policy.Policies;

namespace Wdc.Domain.Tests.Policy;

/// <summary>
/// The rules of <see cref="DataAccess{TEntity, TCriteria}"/>, on policies written for the test and no database: how a
/// request is refused or rewritten, and how a row is shaped.
/// </summary>
public sealed class DataPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Customer LoginAndName() => new() { Login = Mark.String, Name = Mark.String };

    private static DataPolicy<Customer, CustomerCriteria> Policy(Func<DataPolicyBuilder<Customer, CustomerCriteria>, DataPolicyBuilder<Customer, CustomerCriteria>> declare) =>
        declare(CustomerBase()).Build();

    // :: Filtering

    [Fact]
    public void FilteringByAFieldNoGrantReads_IsRefusedByName()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Read("name")));

        var e = Assert.Throws<AccessDeniedException>(() => policy.AccessFor(As("R")).PrepareRead(new CustomerCriteria().Login.StartingWith("a")));

        Assert.Contains("[login]", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FilteringByTwoFieldsThatNoSingleGrantReads_IsRefused()
    {
        // Each field is read by one grant, over different rows: filtering by both at once would reveal the pair.
        var policy = Policy(p => p
            .Grant(["R"], g => g.Read("name"))
            .Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("login")));

        var e = Assert.Throws<AccessDeniedException>(() =>
            policy.AccessFor(As("R")).PrepareRead(new CustomerCriteria().Name.Eq("x").Login.Eq("y")));

        Assert.Contains("login", e.Message, StringComparison.Ordinal);
        Assert.Contains("name", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FilteringInsideAnAlternativeGroup_IsCheckedToo()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Read("name")));
        var criteria = new CustomerCriteria().WithAnyOf(new CustomerCriteria().Login.Containing("adm"));

        Assert.Throws<AccessDeniedException>(() => policy.AccessFor(As("R")).PrepareRead(criteria));
    }

    [Fact]
    public void SortingCountsAsReading()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Read("login")));

        var e = Assert.Throws<AccessDeniedException>(() =>
            policy.AccessFor(As("R")).PrepareRead(new CustomerCriteria().WithOrderBy(CustomerOrderBy.ByName)));

        Assert.Contains("[name]", e.Message, StringComparison.Ordinal);
        // An ordering by the key reveals nothing.
        policy.AccessFor(As("R")).PrepareRead(new CustomerCriteria().WithOrderBy(CustomerOrderBy.OldestFirst));
    }

    [Fact]
    public void FilteringByAFieldOfAnotherEntity_TakesThatEntitysPolicyReadingIt()
    {
        var orders = DataPolicy.For(Order.Model, () => new OrderCriteria())
            .Reference("customer", () => Policy(p => p
                .Grant(["R"], g => g.Read("login"))
                .Grant(["S"], g => g.Read("login").Mask(Mask.KeepFirst(1), "name"))))
            .Collection("lines", inverse: "order", () => LinePolicy)
            .Criterion("customerId", "customer")
            .Criterion("orderId", "id")
            .Criterion("customerName", "customer.name")
            .Grant(["R", "S"], g => g.Read("status", "customer"))
            .Build();

        // The order reads its customer, but the customer's policy does not let R read the name, nor S unmasked.
        var e = Assert.Throws<AccessDeniedException>(() =>
            orders.AccessFor(As("R")).PrepareRead(new OrderCriteria().CustomerName.Containing("Doe")));
        Assert.Contains("customer", e.Message, StringComparison.Ordinal);
        Assert.Contains("[name]", e.Message, StringComparison.Ordinal);
        Assert.Throws<AccessDeniedException>(() => orders.AccessFor(As("S")).PrepareRead(new OrderCriteria().CustomerName.Containing("Doe")));

        // The admin of the shared policies reads it.
        OrderPolicy.AccessFor(As("ADMIN")).PrepareRead(new OrderCriteria().CustomerName.Containing("Doe"));
    }

    // :: Rows

    [Fact]
    public void AGrantOverEveryRow_AddsNoRestriction()
    {
        var policy = Policy(p => p
            .Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("login", "name"))
            .Grant(["R"], g => g.Read("login", "name")));
        var criteria = new CustomerCriteria();

        policy.AccessFor(As("R")).PrepareRead(criteria);

        Assert.Empty(criteria.AnyOf);
    }

    [Fact]
    public void ScopedGrants_BecomeAlternativeGroups_CarryingTheServerGroups()
    {
        var policy = Policy(p => p
            .Grant(["A"], g => g.Rows(OwnCustomer.Scope).Read("login", "name"))
            .Grant(["B"], g => g.Rows(Team.Scope).Read("login", "name")));
        var serverGroup = new CustomerCriteria().Login.Eq("jdoe");
        var criteria = new CustomerCriteria().WithAnyOf(serverGroup);

        policy.AccessFor(As("A", "B")).PrepareRead(criteria);

        Assert.Equal(2, criteria.AnyOf.Count); // one group per scope
        Assert.All(criteria.AnyOf, g => Assert.Equal([serverGroup], g.AnyOf)); // the alternatives already there hold inside each
        Assert.Contains(criteria.AnyOf, g => g.CustomerId.IsSet);
        Assert.Contains(criteria.AnyOf, g => g.Name.IsSet);
    }

    [Fact]
    public void AGrantSharedByTwoRoles_CountsOnce()
    {
        var policy = Policy(p => p.Grant(["A", "B"], g => g.Rows(OwnCustomer.Scope).Read("name")));
        var criteria = new CustomerCriteria();

        policy.AccessFor(As("A", "B")).PrepareRead(criteria);

        Assert.Single(criteria.AnyOf);
    }

    [Fact]
    public async Task TheRowCheckFields_AreFetched_ButNotReturned()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Rows(Team.Scope).Read("login")));
        var criteria = new CustomerCriteria().WithProjection(new Customer { Login = Mark.String });

        var plan = policy.AccessFor(As("R")).PrepareRead(criteria);

        Assert.Equal(Mark.String, criteria.Projection!.Name); // fetched for the row check
        var row = await plan.Apply(new Customer { Id = 1, Login = "jdoe", Name = "Team" }, Ct);
        Assert.Equal("jdoe", row.Login);
        Assert.Null(row.Name); // and cleared before the rows leave
    }

    [Fact]
    public async Task EachRow_ShowsWhatTheGrantsThatReachItAllow()
    {
        var policy = Policy(p => p
            .Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("login", "name"))
            .Grant(["R"], g => g.Read("name").Mask(Mask.KeepLast(2), "login")));
        var plan = policy.AccessFor(As("R")).PrepareRead(new CustomerCriteria().WithProjection(LoginAndName()));

        var own = await plan.Apply(new Customer { Id = Me, Login = "myself", Name = "Me" }, Ct);
        var other = await plan.Apply(new Customer { Id = 1, Login = "someone", Name = "Other" }, Ct);

        Assert.Equal("myself", own.Login);
        Assert.Equal("*****ne", other.Login);
        Assert.Equal("Other", other.Name);
    }

    [Fact]
    public async Task ARowNoGrantReaches_KeepsOnlyItsKey()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("login", "name")));
        var plan = policy.AccessFor(As("R")).PrepareRead(new CustomerCriteria().WithProjection(LoginAndName()));

        var other = await plan.Apply(new Customer { Id = 1, Login = "x", Name = "X" }, Ct);

        Assert.Equal(1, other.Id);
        Assert.Null(other.Login);
        Assert.Null(other.Name);
    }

    [Fact]
    public void ARoleWithoutGrants_ReadsNothing()
    {
        var e = Assert.Throws<AccessDeniedException>(() => CustomerPolicy.AccessFor(As("GUEST")).PrepareRead(new CustomerCriteria()));

        Assert.Contains("customer", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Counting_RestrictsToTheSameRowsAsReading()
    {
        var criteria = new CustomerCriteria();

        CustomerPolicy.AccessFor(As("CLIENT")).RestrictCount(criteria);

        Assert.True(Assert.Single(criteria.AnyOf).CustomerId.IsSet);
    }

    // :: Projection

    [Fact]
    public void ProjectingAFieldNoGrantReads_IsRefusedByName()
    {
        var e = Assert.Throws<AccessDeniedException>(() => CustomerPolicy.AccessFor(As("ADMIN"))
            .PrepareRead(new CustomerCriteria().WithProjection(new Customer { Name = Mark.String, Secret = Mark.String })));

        Assert.Contains("[secret]", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutProjection_TheDefaultShapeIsTrimmed_NotRefused()
    {
        // The default shape has every field but the write-only ones, the login included, which this role does not read.
        // The client asked for nothing in particular, so nothing it asked for is refused.
        var policy = Policy(p => p.Grant(["R"], g => g.Read("name")));
        var criteria = new CustomerCriteria();

        policy.AccessFor(As("R")).PrepareRead(criteria);

        Assert.Equal(Mark.String, criteria.Projection!.Name);
        Assert.Null(criteria.Projection.Login);
        Assert.Null(criteria.Projection.Secret);
    }

    [Fact]
    public async Task InsideAReference_TheRelatedPolicyDecides()
    {
        var access = OrderPolicy.AccessFor(As("CLIENT"));
        // The order reads its customer, and the customer's policy lets a client read its own login.
        var criteria = new OrderCriteria().WithProjection(new Order { Status = Mark.String, Customer = new Customer { Login = Mark.String } });

        var plan = access.PrepareRead(criteria);

        Assert.Single(criteria.AnyOf); // a client reaches its own orders
        var row = await plan.Apply(new Order { Id = 1, Status = "open", Customer = new Customer { Id = Me, Login = "me", Name = "Me" } }, Ct);
        Assert.Equal("me", row.Customer!.Login);
        Assert.Null(row.Customer.Name); // not asked

        // The secret is out of every grant: asking for it inside the reference is refused too.
        var e = Assert.Throws<AccessDeniedException>(() =>
            access.PrepareRead(new OrderCriteria().WithProjection(new Order { Customer = new Customer { Secret = Mark.String } })));
        Assert.Contains("[secret]", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACollection_IsRestrictedAndShapedByItsPolicy()
    {
        var criteria = new OrderCriteria();
        var plan = OrderPolicy.AccessFor(As("CLIENT")).PrepareRead(criteria);

        var fetched = Assert.IsType<ProjectionList<OrderLine>>(criteria.Projection!.Lines);
        var lineCriteria = Assert.IsType<OrderLineCriteria>(fetched.Criteria);
        Assert.Single(lineCriteria.AnyOf); // the lines of the default shape are restricted by their own policy
        Assert.NotNull(fetched.Shape.Order?.Customer?.Id); // and fetch what their row check reads

        var row = new Order
        {
            Id = 1,
            Status = "open",
            Customer = new Customer { Id = Me, Name = "Me", Login = "me" },
            Lines = [new OrderLine { Id = 10, Quantity = 2, Order = new Order { Id = 1, Customer = new Customer { Id = Me } } }],
        };
        await plan.Apply(row, Ct);

        Assert.Equal(2, row.Lines.Single().Quantity);
        Assert.Null(row.Lines.Single().Order); // the row check's fields leave the response
        Assert.Equal("Me", row.Customer.Name);
    }

    // :: Writing

    [Fact]
    public async Task AnUpdateThatMovesTheRowOutOfReach_IsRefused()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Rows(Team.Scope).Read("login", "name").Update("login", "name")));
        var access = policy.AccessFor(As("R"));
        var stored = new Customer { Id = 1, Name = "Team" };

        // Within reach: allowed.
        await access.CheckUpdate(new Customer { Id = 1, Login = "renamed" }, new Customer { Login = Mark.String }, (_, _, _) => Task.FromResult<Customer?>(stored), Ct);

        // Out of it: refused.
        await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await access.CheckUpdate(new Customer { Id = 1, Name = "Elsewhere" }, new Customer { Name = Mark.String },
                (_, _, _) => Task.FromResult<Customer?>(stored), Ct));
    }

    [Fact]
    public async Task AFieldOutsideTheGrant_CannotBeUpdated()
    {
        var policy = Policy(p => p.Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("login", "name").Update("name")));

        var e = await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await policy.AccessFor(As("R")).CheckUpdate(new Customer { Id = Me, Login = "admin" }, new Customer { Login = Mark.String },
                (_, _, _) => Task.FromResult<Customer?>(new Customer { Id = Me }), Ct));

        Assert.Contains("[login]", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpdateOfARowOutOfReach_IsNotFound_UnlessItIsReadable()
    {
        var policy = Policy(p => p
            .Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("name").Update("name"))
            .Grant(["R"], g => g.Rows(Team.Scope).Read("name")));
        var access = policy.AccessFor(As("R"));
        var change = new Customer { Name = Mark.String };

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await access.CheckUpdate(new Customer { Id = 1, Name = "x" }, change,
                (_, _, _) => Task.FromResult<Customer?>(new Customer { Id = 1, Name = "Other" }), Ct));
        // A row the actor reads, but does not update: refused, since its existence is no secret.
        await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await access.CheckUpdate(new Customer { Id = 2, Name = "x" }, change,
                (_, _, _) => Task.FromResult<Customer?>(new Customer { Id = 2, Name = "Team" }), Ct));
        // Without the key.
        await Assert.ThrowsAsync<InvalidRequestException>(async () =>
            await access.CheckUpdate(new Customer { Name = "x" }, change, (_, _, _) => Task.FromResult<Customer?>(null), Ct));
    }

    [Fact]
    public async Task ARoleThatDoesNotUpdate_OrDelete_IsRefused()
    {
        var access = CustomerPolicy.AccessFor(As("CLIENT"));

        await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await access.CheckUpdate(new Customer { Id = Me, Name = "x" }, new Customer { Name = Mark.String },
                (_, _, _) => Task.FromResult<Customer?>(null), Ct));
        Assert.Throws<AccessDeniedException>(() => access.RestrictDelete(new CustomerCriteria().CustomerId.Eq(Me)));
    }

    [Fact]
    public async Task Create_NeedsOneGrantForEveryField_AndReachingTheNewRow()
    {
        var access = OrderPolicy.AccessFor(As("CLIENT"));
        await access.CheckCreate(new Order { Status = "new", Customer = new Customer { Id = Me } }, Ct);

        // On someone else's behalf: the grant does not reach the new row.
        await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await access.CheckCreate(new Order { Status = "new", Customer = new Customer { Id = 1 } }, Ct));
        // A reference is written by its key only.
        var e = await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await access.CheckCreate(new Order { Status = "new", Customer = new Customer { Id = Me, Name = "Renamed" } }, Ct));
        Assert.Contains("written by its key only", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ChecksTheRowsOfACollection_AsRowsOfTheNewOne()
    {
        var access = OrderPolicy.AccessFor(As("CLIENT"));
        var line = new OrderLine { Quantity = 1 };
        var order = new Order { Status = "new", Customer = new Customer { Id = Me }, Lines = [line] };

        // The line belongs to the new order while it is checked: the line policy reaches it through the order's customer.
        await access.CheckCreate(order, Ct);
        Assert.Null(line.Order); // what the line carried is given back

        // A line sent on its own, into an order the scope does not see as the actor's, is not reached.
        await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            await LinePolicy.AccessFor(As("CLIENT")).CheckCreate(new OrderLine { Quantity = 1, Order = new Order { Id = 9 } }, Ct));
    }

    [Fact]
    public async Task ReadBack_DropsWhatTheActorDoesNotRead()
    {
        var written = new Customer { Id = 5, Login = "new", Name = "New", Secret = "digest" };
        await CustomerPolicy.AccessFor(As("ADMIN")).ReadBack(written, Ct);
        Assert.Equal("new", written.Login);
        Assert.Null(written.Secret);

        // An actor that reads nothing of the entity gets the key only.
        var other = new Customer { Id = 6, Login = "x", Name = "X" };
        await CustomerPolicy.AccessFor(As("GUEST")).ReadBack(other, Ct);
        Assert.Equal(6, other.Id);
        Assert.Null(other.Login);
    }

    [Fact]
    public async Task ReadBack_FollowsAGraphWithBackReferences()
    {
        var order = new Order { Id = 1, Status = "new", Customer = new Customer { Id = Me } };
        order.Lines = [new OrderLine { Id = 2, Quantity = 1, Order = order }];

        await OrderPolicy.AccessFor(As("CLIENT")).ReadBack(order, Ct);

        Assert.Equal("new", order.Status);
        Assert.Equal(1, order.Lines.Single().Quantity);
        Assert.Same(order, order.Lines.Single().Order);
    }

    [Fact]
    public void Delete_IsRestrictedToTheRowsOfTheGrantsThatDelete()
    {
        var policy = Policy(p => p
            .Grant(["R"], g => g.Rows(OwnCustomer.Scope).Read("name").Delete())
            .Grant(["R"], g => g.Read("name")));
        var criteria = new CustomerCriteria().Name.Eq("x");

        policy.AccessFor(As("R")).RestrictDelete(criteria);

        Assert.True(Assert.Single(criteria.AnyOf).CustomerId.IsSet);
    }

    // :: Declaration

    [Fact]
    public void AMaskedFieldCannotBeWrittenByTheSameGrant() =>
        Assert.Throws<InvalidOperationException>(() => Policy(p => p.Grant(["R"], g => g.Mask(Mask.KeepFirst(1), "login").Update("login"))));

    [Fact]
    public void TheKeyCannotBeMasked_NorAWriteOnlyFieldRead()
    {
        Assert.Throws<InvalidOperationException>(() => Policy(p => p.Grant(["R"], g => g.Mask(Mask.KeepFirst(1), "id"))));
        Assert.Throws<InvalidOperationException>(() => Policy(p => p.Grant(["R"], g => g.Read("secret"))));
        Assert.Throws<InvalidOperationException>(() => Policy(p => p.Grant(["R"], g => g.Update("id"))));
    }

    [Fact]
    public void AnUnknownFieldInAGrant_FailsWhenThePolicyIsBuilt() =>
        Assert.Throws<ArgumentException>(() => Policy(p => p.Grant(["R"], g => g.Read("nickname"))));

    [Fact]
    public void ACriteriaFieldWithoutAnEntityField_FailsWhenThePolicyIsBuilt()
    {
        var e = Assert.Throws<InvalidOperationException>(() => DataPolicy.For(Customer.Model, () => new CustomerCriteria()).Build());

        Assert.Contains("customerId", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceWithoutAPolicy_FailsWhenThePolicyIsBuilt()
    {
        var e = Assert.Throws<InvalidOperationException>(() => DataPolicy.For(OrderLine.Model, () => new OrderLineCriteria())
            .Criterion("customerId", "order").Build());

        Assert.Contains("order", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Masks()
    {
        Assert.Equal("j***", Mask.KeepFirst(1).Apply("jdoe"));
        Assert.Equal("*********09", Mask.KeepLast(2).Apply("12345678909"));
        Assert.Equal("ab", Mask.KeepFirst(5).Apply("ab"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mask.KeepFirst(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mask.KeepLast(-1));
    }

    // :: Security off

    [Fact]
    public async Task WithoutSecurity_EveryRowAndField_ButTheWriteOnlyOnes()
    {
        var access = CustomerPolicy.AccessFor(null);
        var criteria = new CustomerCriteria().Login.Eq("jdoe");

        var plan = access.PrepareRead(criteria);

        Assert.Empty(criteria.AnyOf);
        Assert.Null(criteria.Projection); // without a projection, the repository's own default
        var row = await plan.Apply(new Customer { Id = 1, Login = "jdoe", Name = "J", Secret = "digest" }, Ct);
        Assert.Equal("jdoe", row.Login);
        Assert.Null(row.Secret); // a write-only field never goes out

        var asked = new CustomerCriteria().WithProjection(new Customer { Name = Mark.String });
        access.PrepareRead(asked);
        Assert.Equal(Mark.String, asked.Projection!.Name);

        var e = Assert.Throws<AccessDeniedException>(() => access.PrepareRead(new CustomerCriteria().WithProjection(new Customer { Secret = Mark.String })));
        Assert.Contains("[secret]", e.Message, StringComparison.Ordinal);

        // Writes are not checked.
        await access.CheckCreate(new Customer { Secret = "x" }, Ct);
        await access.CheckUpdate(new Customer { Id = 1, Secret = "x" }, new Customer { Secret = Mark.String },
            (_, _, _) => throw new InvalidOperationException("not loaded"), Ct);
        access.RestrictDelete(new CustomerCriteria().CustomerId.Eq(1));
    }
}
