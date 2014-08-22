namespace EventSourcing

open System 

/// a eventstore should provide
/// methods to run computations
/// and check if a entity exists
type IEventStore =
    abstract exists    : EntityId -> bool
    abstract run       : StoreComputation.T<'a> -> 'a
    abstract subscribe : ((EntityId * 'e) -> unit) -> IDisposable

module EventStore =

    let add (id : EntityId) (e : 'e) (es : IEventStore) =
        StoreComputation.add id e
        |> es.run

    let restore (p : Projection.T<_,_,'a>) (id : EntityId) (es : IEventStore) : 'a =
        StoreComputation.restore p id
        |> es.run

    let exists (id : EntityId) (es : IEventStore) =
        es.exists(id)

    let fromRepository (rep : IEventRepository) : IEventStore =
        let eventObs = EventObservable.create ()
        let rep' = EventObservable.wrap rep eventObs
        { new IEventStore with
            member __.exists id   = rep.exists id
            member __.run p       = p |> StoreComputation.executeIn rep'
            member __.subscribe h = eventObs.addHandler h
        }

    let execute (es : IEventStore) (comp : StoreComputation.T<'a>) =
        es.run comp