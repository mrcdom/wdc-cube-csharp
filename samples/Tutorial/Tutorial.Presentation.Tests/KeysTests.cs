using System.Reflection;
using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Tests;

/// <summary>The keys classes: one per place, their class tree is the place tree, and their prefixes do not clash.</summary>
public sealed class KeysTests
{
    private static readonly Type[] s_keys =
        [.. typeof(MainKeys).Assembly.GetTypes().Where(t => typeof(CubeKeys).IsAssignableFrom(t))];

    [Fact]
    public void KeyPrefixes_AreUniqueAcrossTheApplication()
    {
        var prefixes = s_keys.Select(t => (string)Declared(t, "Prefix")!.GetRawConstantValue()!).ToList();

        Assert.Equal(prefixes.Count, prefixes.Distinct().Count());
        Assert.All(prefixes, p => Assert.Equal(2, p.Length));
    }

    [Fact]
    public void EachPlace_IsDeclaredByOneKeysClass_ChildOfTheKeysOfItsParent()
    {
        var placeOf = s_keys.ToDictionary(t => t, PlaceOf);

        Assert.Equal(placeOf.Count, placeOf.Values.Distinct().Count()); // one keys class per place
        Assert.True(placeOf.Values.ToHashSet().SetEquals([.. MainPresenter.AllPlaces, Place.Root]));
        foreach (var (type, place) in placeOf.Where(p => p.Key != typeof(MainKeys)))
        {
            Assert.Same(place.Parent, PlaceOf(type.BaseType!));
        }
    }

    private static Place PlaceOf(Type keys) =>
        (Place)keys.GetProperty("Place", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.GetValue(null)!;

    private static FieldInfo? Declared(Type type, string name) =>
        type.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
}
