# 0017. Keys classes declare their places

## Status

Accepted (2026-11-22).

## Context

A place carries a name, a parent and the factory of its presenter; a keys class carries the names a place reads and
writes in an intent. Both follow the same tree: the keys of a deeper place extend the keys of the place above, to
read what the levels above hand down. Kept apart (a static table of places, and keys classes that receive their
place as an argument), the two trees could drift: a keys class could extend the keys of a place that is not its
parent, and navigation was written two ways (`Flip(place)` and `keys.Flip()`). Every application also rewrote the
same keys base (the application, the intent, a way to flip).

## Decision

- The framework provides `CubeKeys`, the base of an application's keys: it holds the application and the intent
  (a new one from the current state when none is given) and exposes `Flip()`.
- **Each place is declared by its keys class**, as `public static new Place Place { get; }`, built from the place of
  the keys it extends: `HomeKeys.Place.Child<RootPresenter>("product", app => new ProductPresenter(app))`. The root
  keys hold `Place.Root`. So the class tree is the place tree, by construction.
- Every place has its keys class, even one with no names of its own: the keys declare the place and navigate to it.
- Navigation goes through the keys: `await new CartKeys(App).Flip();`,
  `await new ProductKeys(App) { ProductId = id }.Flip();`. A redirect inside `ApplyParameters` uses the intent in
  transit (`intent.Redirect(HomeKeys.Place)`).
- The application presenter lists the places an address can name (`AllPlaces`), explicitly: building the list by
  scanning types would need reflection.
- The level of the application is named for the tree: `RootKeys`, `RootPresenter`, `RootScope`, `RootView`, as
  `Place.Root`, rather than "main", which in .NET also reads as the program's entry point or the main window.
- The folder tree is the place tree too: the root module lives directly in `Modules/`, and each place's module is a
  subfolder of its parent's (`Modules/Home/Cart` for `home/cart`).
- The navigation method is `Flip`, the vocabulary of `FlipIntent` and `FlipToIntent`, as in the other
  implementations.

## Alternatives considered

- **A static table of places, separate from the keys** (the previous convention): one more file to keep in step with
  the keys hierarchy, with nothing to catch a keys class extending the wrong parent.
- **`Go()` as the navigation method**: shorter, but a second word for what the rest of the API calls a flip.
- **Each application writes its own keys base**: no framework type to learn, but the same code in every application.

## Consequences

- A keys class that extends the wrong keys puts its place under the wrong parent, which shows in its path and in a
  simple test (`KeysTests` in the samples: one keys class per place, child of the keys of its parent, unique
  prefixes).
- Places are created when their keys type is first used; listing them in `AllPlaces` creates them all before the
  first navigation.
- The framework's own tests keep plain place tables: `CubeKeys` is a convention the applications adopt, not
  something the navigation requires.

See [Architecture](../concepts/architecture.md#keys-classes).
