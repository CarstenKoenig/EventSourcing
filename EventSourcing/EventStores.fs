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