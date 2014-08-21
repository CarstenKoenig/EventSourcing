# EventSourcing

EventSourcing library with support for applicative projection definitions

100% F# and functional style

## background

** CAUTION: quasi-theoretic semi-nonsense ahead - feel free to skip **

**EventSourcing** is sometimes called *functional-databse*. If you look at how current data/state is recustructed from a sequence of events it nothing less than a **functional left-fold**.

My idea was to make these **projections** from the event-sequence to data a first-class concept in this library.
For this I wraped the function you fold over (`state -> event -> state`) together with a final projection (`state -> output`) in a *projection-type* [Projection.T](/EventSourcing/Projections.fs).

Thanks to the final projection this is obviously a functor. And even it's not really a applicative-functor, as the types from the internal-fold-state mess up most of the laws, it's an applicative functor by behaviour and I added the common operations.

## projections

Instead of defining your aggregates or projections in classes and class-methods where you basically do the *left-fold* time and time again on your own you can use the primitive combinators from [Projection.T](/EventSourcing/Projections.fs) to define projections for data-parts you need. Those lego-bricks can be combined and reused - using the operators `<?>` and `<*>` - to get more complex projections.

Here is an example from the [ConsoleSample](/ConsoleSample/Program.fs)-project:

```
/// the netto-weight, assuming a container itself is 2.33t
let nettoWeight = 
    ((+) 2.33<t>) <?> Projection.sumBy (
        function
        | Loaded (_,w)   -> Some w
        | Unloaded (_,w) -> Some (-w)
        | _              -> None )
```

Here we are using `Projection.sumBy` to keep track of the content-weight of our container (we add weight if something was loaded and subtract it if something got unloaded).
Finally we are using `<?>` to apply this projection to the function `((+) 2.33<t>)`, which of course just adds 2.33t for the container-weight itself. 

**remark:** You might know `<?>` as `<$>` of `fmap` from Haskell or Scalaz but the `<$>` is reserved in F# for further use ... so let's keep hoping ;)

If you look further in the sample you will see this:

```
    type ContainerInfo = { id : Id; location : Location; netto : Weight; overloaded : bool; goods : (Goods * Weight) list }
    let createInfo i l n o g = { id = i; location = l; netto = n; overloaded = o; goods = g }
 
    /// current container-info
    let containerInfo =
        createInfo <?> id <*> location <*> nettoWeight <*> isOverloaded <*> goods
```

This is a good example we can use  `<?>` and `<*>` in a clever way to build up projections for a complex structure.

Here we use a curried constructor for `ContainerInfo` (a function with 5 arguments) and pass those in one-by-one using the applicative operators `<?>` and `<*>` - btw: I learnded this trick from WebSharper!

You can understand this if you think of the first applicative-law (ignoring the state-types that get's in the way) like this:

```
pure f <*> x == f <?> x
```
(*remark* in this library `pure` is named `constant`)

now let's give the projections an simplified type `P<'a>` meaning a projection that yields an `'a`.

Then we can see that  `pure containerInfo` has type `P<Id -> Location -> Weigth -> Bool -> (Goods * Weight) list -> ContainerInfo>`.
And because `<*>` has type `P<'a -> 'b> -> P<'a> -> P<'a>` we see that `createInfo <?> id` plugs in the id into the constructor (in the final projection - that's how `fmap` is defined) and has type `P<Location -> Weigth -> Bool -> (Goods * Weight) list -> ContainerInfo>`.

Now of course each `<*>` will just plug in another argument.
