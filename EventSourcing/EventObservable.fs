namespace EventSourcing

/// internal support module to wrap observable capabilities
/// around a repository - used to create EventStores from repositories
module internal EventObservable =

    open System
    open System.Collections.Generic

    type 'e EventHandler = (EntityId * 'e) -> unit

    type IEventObservable =
        inherit IDisposable
        abstract addHandler : 'e EventHandler -> IDisposable
        abstract publish    : EntityId * 'e -> unit

    type private ObservableTransactionScope (rep : IEventRepository, obs : IEventObservable) =
        let newEvents = List<_>()
        let invoke f = try f () with _ as ex -> raise (HandlerException ex)
        let transScope = rep.beginTransaction ()

        member __.addEvent (id : EntityId, event : 'e) (v : EventSourcing.Version option) =
            let ver = rep.add (transScope, id, v, event)
            newEvents.Add (fun () -> obs.publish (id, event))
            ver

        member __.restore (p : Projection.T<_,_,_>) (id : EntityId) =
            rep.restore (transScope,id,p)

        member __.exists (id : EntityId) =
            rep.exists (transScope,id)

        member __.commit () =
            rep.commit transScope
            // publish new Events
            newEvents |> Seq.iter invoke
            newEvents.Clear()

        member __.rollback() =
            rep.rollback transScope
            newEvents.Clear()

        interface ITransactionScope with
            member __.Dispose() = 
                newEvents.Clear ()
                transScope.Dispose()

    let wrap (rep : IEventRepository) (src : IEventObservable) : IEventRepository =
        let beginTrans () = new ObservableTransactionScope (rep, src) :> ITransactionScope
        let call f (t : ITransactionScope) = f (t :?> ObservableTransactionScope)

        { new IEventRepository with 
            member __.Dispose()           = src.Dispose(); rep.Dispose()
            member __.beginTransaction () = beginTrans ()
            member __.commit t            = t |> call (fun t -> t.commit ())
            member __.rollback t          = t |> call (fun t -> t.rollback ())
            member __.exists (t,id)       = t |> call (fun t -> t.exists id)
            member __.restore (t,id,p)    = t |> call (fun t -> t.restore p id)
            member __.add (t,i,v,e)       = t |> call (fun t -> t.addEvent (i,e) v)
        }

    let create () : IEventObservable =
        let handlers = Dictionary<Type, List<(EntityId * obj) -> unit>>()
        let add (h : 'e EventHandler) : IDisposable =
            lock handlers (fun () ->
                let t = typeof<'e>
                let list =
                    match handlers with
                    | Contains t l -> l
                    | _ ->
                        let l = new List<(EntityId * obj) -> unit>()
                        handlers.Add (t, l)
                        l
                let h' (id : EntityId, o : obj) = h (id, unbox o)
                list.Add h'
                { new IDisposable with
                    member __.Dispose() = list.Remove h' |> ignore })
        let notify (id : EntityId, event : 'e) = 
            lock handlers (fun () ->
                let t = typeof<'e>
                match handlers with
                | Contains t l -> l :> ((EntityId * obj) -> unit) seq
                | _            -> Seq.empty
                |> Seq.iter (fun h -> h (id, box event)))

        { new IEventObservable with
            member __.Dispose()      = handlers.Clear()
            member __.addHandler h   = add h
            member __.publish (id,e) = notify (id,e) }
