namespace EventSourcing

open System 

type EntityId = Guid

/// a eventstore should provide
/// methods to add events
/// and playback events using a projection
type IEventStore =
    abstract entityIds : unit -> Collections.Generic.HashSet<EntityId>
    abstract add       : Guid -> 'e -> unit
    abstract playback  : Projection.T<'e,_,'a> -> Guid -> 'a

module EventStore =

    let add (id : EntityId) (e : 'e) (es : IEventStore) =
        es.add id e

    let playback (p : Projection.T<_,_,'a>) (id : EntityId) (es : IEventStore) : 'a =
        es.playback p id

    let exists (id : EntityId) (es : IEventStore) =
        es.entityIds().Contains id

module Context =
    
    type T = private { store : IEventStore }

    let create store = { store = store }

    type Computation<'a> = T -> 'a

    let evalIn (ctx : T) (m : Computation<'a>) : 'a =
        m ctx

    let evalUsing (store : IEventStore) (m : Computation<'a>) : 'a =
        m |> evalIn (create store)

    let returnC a : Computation<'a> = fun _ -> a

    let bind (m : Computation<'a>) (f : 'a -> Computation<'b>) : Computation<'b> =
        fun ctx -> f (m ctx) ctx

    let combine (a : Computation<'a>) (b : Computation<'b>) : Computation<'b> =
        fun ctx -> a ctx |> ignore; b ctx;

    let (>>=) = bind

    type ContextBuilder internal () =
        member x.Bind(m, f) = m >>= f
        member x.Return(v) = returnC v
        member x.ReturnFrom(v) = v
        member x.Delay(f) = f ()     
        member x.Zero() = returnC ()   
        member x.Combine (a,b) = combine a b

    let context = ContextBuilder()

    let lift1 (f : 'a -> IEventStore -> 'b) (a : 'a) : Computation<'b> =
        fun ctx -> ctx.store |> f a

    let lift2 (f : 'a -> 'b -> IEventStore -> 'c) (a : 'a) (b : 'b) : Computation<'c> =
        fun ctx -> ctx.store |> f a b

    let add (id : EntityId) (e : 'e) : Computation<unit> =
        fun ctx -> ctx.store |> EventStore.add id e

    let playback (p : Projection.T<'e,_,'a>) (id : EntityId) : Computation<'a> =
        fun ctx -> ctx.store |> EventStore.playback p id

    let exists (id : EntityId) : Computation<bool> =
        fun ctx -> ctx.store |> EventStore.exists id

[<AutoOpen>]
module ContextOperations =
    open Context

    let context = context