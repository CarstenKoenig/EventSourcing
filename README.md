# EventSourcing

EventSourcing library with support for applicative projection definitions

100% F# and functional style

#### Build
* Mono: Run *buildMono.sh*  ![Travis build status](https://travis-ci.org/CarstenKoenig/EventSourcing.svg)


## usage

### projections

Instead of defining your aggregates or projections in classes and class-methods where you basically do the *left-fold* time and time again on your own you can use the primitive combinators from [Projection.T](/src/EventSourcing/Projections.fs) to define projections for data-parts you need. Those lego-bricks can be combined and reused - using the operators `<?>` and `<*>` - to get more complex projections.

#### API

##### create a constant *Projection*

    Projection.constant (a : 'a)
    
creates a *Projection* that will just return a constant value.

##### `$`: map the result of a *Projection*

    Projection.map (f : 'a -> 'b) (p : T<'e,'i,'a>) : T<'e,'i,'b>
    
or just `$`:

Uses `f` to map the final outcome of the projection `p` into another result.

##### `<*>` sequential application
 
    Projection.sequence (f : T<'e,'i1,('a -> 'b)>) (p : T<'e,'i2,'a>) : T<'e, 'i1*'i2,'b>
    
or just `<*>`:

Use this together with `$` to build complexe *Projections* from simple ones (see below).

This is the applicative-sequence operator - it will fold the inner states of it's two opperants using tuples, so there is no need to enumerate the event-sequence more than once.

##### create a *projection* with full control

    Projection.createWithProjection (p : 'i -> 'a) (i : 'i) (f : 'i -> 'e -> 'i)
    
using this function you have full control on the inner-fold `f`, the initial value `i` and the final projection `p` used in the *Projection*

##### create a *Projection*

    Projection.create (init : 'a) (f : 'a -> 'e -> 'a)

using the same as `createWithProjection` but using `id` for the final projection.

##### aggregates some events into a sum

    Projection.sumBy (f : 'e -> ^a option)
    
Applies `f` to all events and sums up all of those values returning `Some number`, ignoring those returning `None`

##### find the latest value

    Projection.latest (f : 'e -> 'a option)
    
Returns the latest `value` of an event `e` where `f e` returns `Some value`. Here latest is refering to the event with the highest *Version*-number.

##### finds a single value

    Projection.single (f : 'e -> 'a option)
    
Returns `value` of the only event `e` where `f e` returns `Some value`. Throws an exception if there is more than one such event or when no such event was found.

#### Example

Here is an example from the [ConsoleSample](/src/ConsoleSample/Program.fs)-project:

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

**remark:** You might know `$` as `<$>` or `fmap` from Haskell or Scalaz but the `<$>` is reserved in F# for further use ... so let's keep hoping ;)

If you look further in the sample you will see this:

```
    type ContainerInfo = { id : Id; location : Location; netto : Weight; overloaded : bool; goods : (Goods * Weight) list }
    let createInfo i l n o g = { id = i; location = l; netto = n; overloaded = o; goods = g }
 
    /// current container-info
    let containerInfo =
        createInfo $ id <*> location <*> nettoWeight <*> isOverloaded <*> goods
```

This is a good example of how you can use  `$` and `<*>` in a clever way to build up projections for a complex structure.

All you need is a curried constructor for `ContainerInfo` (a function with 5 arguments) and pass those in one-by-one using the applicative operators `$` and `<*>` - btw: I learnded to appreciate this trick from WebSharper!

#### How does this work?
Let's follow the types.

Remember: `pure f <*> x == f $ x` (**remark** in this library `pure` is named `constant`).

Now let's give the projections a simplified type: `P<'a>` (think: "projection that yields an `'a`").

Then we can see that  `pure containerInfo` has type `P<Id -> Location -> Weigth -> Bool -> (Goods * Weight) list -> ContainerInfo>`.
And because `<*>` has type `P<'a -> 'b> -> P<'a> -> P<'b>` we see that `createInfo $ id` plugs in the id into the constructor (in the final projection - that's how `fmap` is defined) and has type `P<Location -> Weigth -> Bool -> (Goods * Weight) list -> ContainerInfo>`.

Now of course each `<*>` will just plug in another argument.

### repositories

These are where events are stored to - a repository has methods to check if an entity exists (`EntityId -> Bool`), 
add an event to an entity, some stuff to support transactions and a *restore* function to use a projection to get some value out of the store.

You can optionally give the latest excepted version-value of a entity to the `add` function to support concurrency checks too.

But normally you should not access repositories directly - you should use an `EventStore` to interact with the system.

Included are an in-memory repository `EventSourcing.Repositories.InMemory` and a Model-First Entity-Framework based repository in `EventSourcing.Repositories.EntityFramework`.

### event stores
An event-store is basically a repository that publishes new events using the observable pattern.
But instead of just wrapping the primitive operations it will use store-computations (see next section) to execute queries and commands.

#### API
The main functions are:

##### subscribe an event-handler

    EventStore.subscribe (h : 'e EventHandler) (es : IEventStore) : System.IDisposable
Subscribes an event-handler `h` to the event-store `es`. If you dispose the result the handler will be unsubscribed.

##### execute a store-computation

    EventStore.execute (es : IEventStore) (comp : Computation.T<'a>)
Executes an store-computation `comp` within the store `es` returning its result.
If there is an exception thrown while running the computation `rollback` at the underlying repository will be called
and the exception will be passed to the caller.

##### adding an event

    EventStore.add (id : EntityId) (e : 'e) (es : IEventStore)
Adds an event `e` to the entity with id `id` using the event-store `es`.

##### restoring from a projection

    EventStore.restore (p : Projection.T<_,_,'a>) (id : EntityId) (es : IEventStore) : 'a
Queries data for the entity with id `id` from the event-store `es` using a projection `p`.

##### check if an entity exists

    EventStore.exists (id : EntityId) (es : IEventStore)
Checks if an event with id `id` exists in the event-store `es`.

##### create an store from a repository

##### getting all EntityIds in store

    EventStore.allIds (es : IEventStore) : EntityId seq
Returns a sequence of all known entity-ids in the store.

    EventStore.fromRepository (rep : IEventRepository) : IEventStore
Creates an event-store from a repositorty `rep` - all queries and commands will use this repository and it's
`commit` and `rollback` will be called accordingly.

### store computations

This is an abstraction around inserting and querying data from an `EventStore` - it includes functions and a Monad-Builder to define queries against a store.
This mechanism will keep Entity-Versions in check and try to ensure concurrency issues.

#### API
The primitive building blocks are:

##### check if an entity exists

    Computation.exists (id : EntityId) : T<bool>
Checks if there is an entity with id `id` in the store.

##### get all entity-ids

    Computation.allIds : T<EntityId seq>
When run returns all entity-ids currently in the store

##### restoring data using a projection

    Computation.restore (p : Projection.T<'e,_,'a>) (id : EntityId) : T<'a>
Uses a projection `p` to query data from the event-source of an entity with id `id`.

##### adding an event 

    Computation.add (id : EntityId) (event : 'e) : T<unit>
Adds an event `event` to the entity with id `id`

##### ignoring the next concurrency check for an entity

    Computation.ignoreNextConcurrencyCheckFor (id : EntityId) : T<unit>
Normaly each `add` will give the currently known version of the entity to the repository 
(which should check if this is the same as the last events-version).
If another event got inserted concurrently this will yield an exception and the transaction will be rolled-back.

You can disable this behaviour by using this function - it will remove the known entity-version so that the next `add` will ignore
any concurrency issues.

##### executing a computation using a repository

    Computation.executeIn (rep : IEventRepository) (comp : T<'a>) : 'a
Executes an computation `comp` using the `rep` repository returning the computation result.
This will take care of the event-version and call the repositories `commit` on success or `rollback` if an exception occured.

You should not call this method yourself - instead you should use `EventStore.execute`

##### monadic builder support

You can use the `store` computational-expression to build up more complex computations.

#### Example

    let assertExists (id : Id) : Computation.T<unit> =
        Computation.Do {
            let! containerExists = Computation.exists id
            if not containerExists then failwith "container not found" }

    let shipTo (l : Location) (id : Id) : Computation.T<unit> =
        Computation.Do {
            do! assertExists id
            let ev = MovedTo l
            do! Computation.add id ev }

### experimental support for CQRS

I added some support for CQRS-pattern support in the module `CQRS` (see also the tests in `CqrsTests.fs`).

Right now it's just a record parametrized over a command (you should implement as an ADT) containing:

- an event-store
- a command-handler to run commands
- a list of registered sinks for read-models.

#### What is a command-handler?
A command-handler will translate a command (remember: your ADT) into a store-computation.
If a command is executed in the CQRS-model this will be called to create a computation that in turn will be run against the store.

#### What are sinks?
**Sinks** are just subscribtions to the stores event-stream that are using `Computation`s to update external read-models.
Of course those read-models should use some kind of database to store their values - for the test it's just a simple dictionary.

If a new event is added to the store those sinks will receive those events together with the Id of the entity that caused the event
and can then decide to `update` their external data using the `store` and it's capabilities to run computations. 

#### Example
This is how the Console-Sample programm defines it's CQRS model:

    // create the CQRS model
    let model =
        CQRS.create rep (function
            | CreateContainer id ->
                    Computation.add id (Created id)
            | ShipTo (id, l) ->
                Computation.Do {
                    do! assertExists id
                    do! Computation.add id (MovedTo l) }
            | Load (id, g, w) ->
                Computation.Do {
                    do! assertExists id
                    do! Computation.add id (Loaded (g,w)) }
            | Unload (id, g, w) ->
                Computation.Do {
                    do! assertExists id
                    do! Computation.add id (Unloaded (g,w)) }
            )
            
This just translates commands like `ShipTo` into events like `MovedTo` and stores but asserts first that the container already exists.
       
To create an sink that just updates a dictionary if the location changed the code looks like this:
            
    // register a sink for the location-dictionary:
    model 
    |> CQRS.registerReadModelSink 
        (fun _ (eId, ev) ->
            match ev with
            | MovedTo l -> locations.[eId] <- l
            | _ -> ())
        

As you can see this just updates the dictionary whenever there is a new location set
within a `MovedTo` event.

## background

**CAUTION: quasi-theoretic semi-nonsense ahead - feel free to skip**

**EventSourcing** is sometimes called *functional-database*. If you look at how current data/state is recustructed from a sequence of events it nothing less than a **functional left-fold**.

The main idea behind this project is to make these **projections** from the event-sequence to data first-class objects.
For this I wraped the function you fold over (`state -> event -> state`) together with a final projection (`state -> output`) in a *projection-type* [Projection.T](/src/EventSourcing/Projections.fs).

Thanks to the final projection this is obviously a functor. And even it's not really an applicative-functor, as the types from the internal-fold-state mess up most of the laws, it's an applicative functor by behaviour and I added the common operations.

## Remarks

There is an EF repository included that should work on .net and Sqlite repository that should work on Mono/Linux (Monodevelop).

I did not find a way to make either work on both plattforms (yet) - maybe someone can give me a clue on how to achive this (Pull-Request very welcome).

Anyway you should consider implementing your own repository in a serious situation.
