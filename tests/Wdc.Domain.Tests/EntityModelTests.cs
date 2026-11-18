namespace Wdc.Domain.Tests;

public sealed class EntityModelTests
{
    [Fact]
    public void OwnFieldsProjection_OwnScalarsAndReferenceKeys_WithoutBlobsSecretsOrCollections()
    {
        var product = Product.Model.OwnFieldsProjection();
        Assert.NotNull(product.Name);
        Assert.Null(product.Image);

        var user = User.Model.OwnFieldsProjection();
        Assert.Null(user.Password);

        var purchase = Purchase.Model.OwnFieldsProjection();
        Assert.Null(purchase.Items);
        Assert.True(User.Model.IsKeyOnly(purchase.User!));
    }

    [Fact]
    public void ChangedFields_MarksWhatDiffers_AndAlwaysTheKey()
    {
        var before = new Product { Id = 1, Name = "Bola", Price = 10m, Image = [1, 2] };
        var after = new Product { Id = 1, Name = "Bola", Price = 12m, Image = [1, 2] };

        var changed = Product.Model.ChangedFields(after, before);

        Assert.NotNull(changed.Id);
        Assert.NotNull(changed.Price);
        Assert.Null(changed.Name);
        Assert.Null(changed.Image); // same bytes
    }

    [Fact]
    public void ChangedFields_ComparesReferencesByKey()
    {
        var before = new Purchase { Id = 1, User = new User { Id = 3, Name = "a" } };
        var sameUser = new Purchase { Id = 1, User = new User { Id = 3, Name = "b" } };
        var otherUser = new Purchase { Id = 1, User = new User { Id = 4 } };

        Assert.Null(Purchase.Model.ChangedFields(sameUser, before).User);
        Assert.NotNull(Purchase.Model.ChangedFields(otherUser, before).User);
    }

    [Fact]
    public void Model_RefusesDuplicateFieldsAndKeys()
    {
        Assert.Throws<ArgumentException>(() => EntityModel.For(() => new Product())
            .Field(ValueKinds.String, "name", p => p.Name, (p, v) => p.Name = v)
            .Field(ValueKinds.String, "name", p => p.Name, (p, v) => p.Name = v));
        Assert.Throws<ArgumentException>(() => EntityModel.For(() => new Product())
            .Key(ValueKinds.Int64, "id", p => p.Id, (p, v) => p.Id = v)
            .Key(ValueKinds.String, "name", p => p.Name, (p, v) => p.Name = v));
    }
}
