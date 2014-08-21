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

    module InMemory =

        open System.Collections.Generic

        let create () : IEventStore =
            let cache = new Dictionary<EntityId, List<obj>>()
            let ids () = lock cache (fun () -> cache.Keys |> fun ks -> new HashSet<_>(ks))
            let add id e = lock cache (fun () -> 
                match cache.TryGetValue id with
                | (true, l)  -> l
                | (false, _) -> let l = List<_>()
                                cache.Add (id, l)
                                l
                |> fun l -> l.Add (box e))
            let play p id = lock cache (fun () ->
                match cache.TryGetValue id with
                | (true, l)  -> l :> obj seq
                | (false, _) -> Seq.empty
                |> Seq.map unbox
                |> Projection.fold p)
            { new IEventStore with
                member __.add id e = add id e
                member __.playback p id = play p id
                member __.entityIds () = ids () }
