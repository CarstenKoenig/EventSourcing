# EventSourcing

EventSourcing library with support for applicative projection definitions

100% F# and functional style

## background

**CAUTION: quasi-theoretic semi-nonsense ahead - feel free to skip**

**EventSourcing** is sometimes called *functional-databse*. If you look at how current data/state is recustructed from a sequence of events it nothing less than a **functional left-fold**.

The main idea behind this project is to make these **projections** from the event-sequence to data first-class objects.
For this I wraped the function you fold over (`state -> event -> state`) together with a final projection (`state -> output`) in a *projection-type* [Projection.T](/EventSourcing/Projections.fs).

Thanks to the final projection this is obviously a functor. And even it's not really a applicative-functor, as the types from the internal-fold-state mess up most of the laws, it's an applicative functor by behaviour and I added the common operations.

## projections

Instead of defining your aggregates or projections in classes and class-methods where you basically do the *left-fold* time and time again on your own you can use the primitive combinators from [Projection.T](/EventSourcing/Projections.fs) to define projections for data-parts you need. Those lego-bricks can be combined and reused - using the operators `<?>` and `<*>` - to get more complex projections.

Here is an example from the [ConsoleSample](/ConsoleSample/Program.fs)-project:

```
/// the netto-weight, assuming a container itself is 2.33t
let nettoWeight = 
    ((+) 2.33<t>) $ Projection.sumBy (
        function
        | Loaded (_,w)   -> Some w
        | Unloaded (_,w) -> Some (-w)
        | _              -> None )
```

Here we are using `Projection.sumBy` to keep track of the content-weight of our container (we add weight if something was loaded and subtract it if something got unloaded).
Finally we are using `$` to apply this projection to the function `((+) 2.33<t>)`, which of course just adds 2.33t for the container-weight itself. 

**remark:** You might know `$` as `<$>` of `fmap` from Haskell or Scalaz but the `<$>` is reserved in F# for further use ... so let's keep hoping ;)

If you look further in the sample you will see this:

```
    type ContainerInfo = { id : Id; location : Location; netto : Weight; overloaded : bool; goods : (Goods * Weight) list }
    let createInfo i l n o g = { id = i; location = l; netto = n; overloaded = o; goods = g }
 
    /// current container-info
    let containerInfo =
        createInfo $ id <*> location <*> nettoWeight <*> isOverloaded <*> goods
```

This is a good example we can use  `$` and `<*>` in a clever way to build up projections for a complex structure.

Here we use a curried constructor for `ContainerInfo` (a function with 5 arguments) and pass those in one-by-one using the applicative operators `<?>` and `<*>` - btw: I learnded this trick from WebSharper!

Remember: `pure f <*> x == f $ x` (**remark** in this library `pure` is named `constant`).

Now let's give the projections a simplified type: `P<'a>` (think: "projection that yields an `'a`").

Then we can see that  `pure containerInfo` has type `P<Id -> Location -> Weigth -> Bool -> (Goods * Weight) list -> ContainerInfo>`.
And because `<*>` has type `P<'a -> 'b> -> P<'a> -> P<'a>` we see that `createInfo <?> id` plugs in the id into the constructor (in the final projection - that's how `fmap` is defined) and has type `P<Location -> Weigth -> Bool -> (Goods * Weight) list -> ContainerInfo>`.

Now of course each `<*>` will just plug in another argument.

## repositories

These are where events are stored to - a repository has methods to check if a entity exisist (`EntityId -> Bool`), 
add a event to a entity, some stuff to support transactions and a *restore* function to use a projection to get some value out of the store.

You can optionally give the latest expceted version-value of a entity to the `add` function to support concurrency checks too.

But normaly you should not access repositories directly - you should use an `EventStore` to interact with the system.

Included are a in-memory repository `EventSourcing.Repositories.InMemory` and a Model-First Entity-Framework based repository in `EventSourcing.Repositories.EntityFramework`.

## event stores
An event-store is basically a repository that publishes new events using the observable pattern.
But instead of just wrapping the primitive operations it will use store-computations (see next section) to execute queries and commands.

The main functions are:

    EventStore.subscribe (h : 'e EventHandler) (es : IEventStore) : System.IDisposable
Subscribes an event-handler `h` to the event-store `es`. If you dispose the result the handler will be unsubscribed.

    EventStore.execute (es : IEventStore) (comp : StoreComputation.T<'a>)
Executes an store-computation `comp` within the store `es` returing it's result.
If there is an exception thrown while running the computation `rollback` at the underlying repository will be called
and the exception will be passed to the caller.

        
    EventStore.add (id : EntityId) (e : 'e) (es : IEventStore)
Adds an event `e` to the entity with id `id` using the event-store `es`

    EventStore.restore (p : Projection.T<_,_,'a>) (id : EntityId) (es : IEventStore) : 'a
Queries data from the event-source for the entity with id `id` from the event-store `es` using a projection `p`

    EventStore.exists (id : EntityId) (es : IEventStore)
Checks if an event with id `id` exists in the event-store `es`

    EventStore.fromRepository (rep : IEventRepository) : IEventStore
Creates an event-store from a repositorty `rep` - all queries and commands will use this repostiory and it's
`commit` and `rollback` will be called accordingly.

## store computations

This is an abstraction around inserting and querying data from an `EventStore` - it includes functions and a Monad-Builder to define queries against a store.
This mechanism will keep Entity-Versions in check and try to ensure concurency issues.

The primitive building blocks are:

    StoreComputation.exists (id : EntityId) : T<bool>
Checks if there is an entity with this id in the store.

    StoreComputation.restore (p : Projection.T<'e,_,'a>) (id : EntityId) : T<'a>
Uses a projection `p` to query data from the event-source of an entity with id `id`.

    StoreComputation.add (id : EntityId) (event : 'e) : T<unit>
Adds an event`event` to the entity with id `id`

    StoreComutation.executeIn (rep : IEventRepository) (comp : T<'a>) : 'a
Executes an computation `comp` using the `rep` repository returing the computations result.
This will take care of the event-version and call the repositories `comit` on success or `rollback` if an exception occured.

You can use the `store` computational-expression to build up more complexe computations.

### Example

    let assertExists (id : Id) : StoreComputation.T<unit> =
        store {
            let! containerExists = StoreComputation.exists id
            if not containerExists then failwith "container not found" }

    let shipTo (l : Location) (id : Id) : StoreComputation.T<unit> =
        store {
            do! assertExists id
            let ev = MovedTo l
            do! StoreComputation.add id ev }