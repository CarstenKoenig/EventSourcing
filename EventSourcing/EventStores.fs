namespace EventSourcing

open EventObservable

/// a eventstore should provide
/// methods to run computations
/// and check if a entity exists
type IEventStore =
    inherit System.IDisposable
    abstract run       : StoreComputation.T<'a> -> 'a
    abstract subscribe : 'e EventHandler -> System.IDisposable

module EventStore =

    /// subscribes an event-handler (for a certain event-type) to the event-store
    let subscribe (h : 'e EventHandler) (es : IEventStore) : System.IDisposable =
        es.subscribe h

    /// adds an event for a entity into the store
    let add (id : EntityId) (e : 'e) (es : IEventStore) =
        StoreComputation.add id e
        |> es.run

    /// restores data from a eventstore for a given entity-id using a projection
    let restore (p : Projection.T<_,_,'a>) (id : EntityId) (es : IEventStore) : 'a =
        StoreComputation.restore p id
        |> es.run

    /// executes a store-computation using the given repository
    /// and it's transaction support
    /// Note: it will reraise any internal error but will not rollback
    /// if the errors where caused by EventHandlers (so the events will
    /// still be saved if there where errors in any EventHandler)
    let private executeComp (rep : IEventRepository) (comp : StoreComputation.T<'a>) : 'a =
        use trans = rep.beginTransaction ()
        try
            let res = comp |> StoreComputation.run rep trans
            rep.commit trans
            res
        with
        | :? HandlerException ->
            // don't rollback on Handler-Exceptions
            reraise()
        | _ ->
            rep.rollback trans
            reraise()

    /// creates an event-store from a repository
    let fromRepository (rep : IEventRepository) : IEventStore =
        let eventObs = EventObservable.create ()
        let rep' = EventObservable.wrap rep eventObs
        { new IEventStore with
            member __.Dispose()   = rep.Dispose()
            member __.run p       = executeComp rep' p
            member __.subscribe h = eventObs.addHandler h
        }

    /// executes an store-computation using an event-store
    let execute (es : IEventStore) (comp : StoreComputation.T<'a>) =
        es.run comp