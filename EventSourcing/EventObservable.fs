namespace EventSourcing

module internal EventObservable =

    open System
    open System.Collections.Generic

    type 'e EventHandler = (EntityId * 'e) -> unit

    type IEventObservable =
        abstract addHandler : 'e EventHandler -> IDisposable
        abstract publish    : EntityId * 'e -> unit

    type private TransactionScope (rep : IEventRepository, obs : IEventObservable) =
        let newEvents = List<_>()
        let invoke f = try f () with _ as ex -> raise (HandlerException ex)
        let transScope = rep.beginTransaction ()

        member this.addEvent (id : EntityId, event : 'e) (v : EventSourcing.Version option) =
            let ver = rep.add (this, id, v, event)
            newEvents.Add (fun () -> obs.publish (id, event))
            ver

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
        let beginTrans () = new TransactionScope (rep, src) :> ITransactionScope
        let commit (t : ITransactionScope) = (t :?> TransactionScope).commit ()
        let rollback (t : ITransactionScope) = (t :?> TransactionScope).rollback ()
        let add (t : ITransactionScope) i v e =
            (t :?> TransactionScope).addEvent (i,e) v

        { new IEventRepository with 
            member __.beginTransaction () = beginTrans ()
            member __.commit t            = commit t
            member __.rollback t          = rollback t
            member __.exists id           = rep.exists id
            member __.restore (t,id,p)    = rep.restore (t,id,p)
            member __.add (t,i,v,e)       = add t i v e
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
            member __.addHandler h   = add h
            member __.publish (id,e) = notify (id,e) }
