namespace EventSourcing

open System 

module internal EventObservable =

    open System.Collections.Generic

    type IEventObservable =
        abstract addHandler : ((EntityId * 'e) -> unit) -> IDisposable
        abstract publish    : (EntityId * 'e) -> unit

    type private TransactionScope (rep : IEventRepository, obs : IEventObservable) =
        let newEvents = List<_>()
        let t = rep.beginTransaction ()

        member this.addEvent (id : EntityId, event : 'e) =
            newEvents.Add (fun () -> obs.publish (id, event))

        member this.commit () =
            rep.commit t
            // publish new Events
            newEvents |> Seq.iter (fun f -> f ())
            newEvents.Clear()

        member this.rollback() =
            rep.rollback t
            newEvents.Clear()

        interface ITransactionScope with
            member __.Dispose() = 
                newEvents.Clear ()
                t.Dispose()

    let wrap (rep : IEventRepository) (src : IEventObservable) : IEventRepository =
        let beginTrans () = new TransactionScope (rep, src) :> ITransactionScope
        let commit (t : ITransactionScope) = (t :?> TransactionScope).commit ()
        let rollback (t : ITransactionScope) = (t :?> TransactionScope).rollback ()
        let add (t : ITransactionScope) i v e =
            (t :?> TransactionScope).addEvent (i,e)
            rep.add t i v e

        { new IEventRepository with 
            member __.beginTransaction () = beginTrans ()
            member __.commit t            = commit t
            member __.rollback t          = rollback t
            member __.exists id           = rep.exists id
            member __.restore t id p      = rep.restore t id p
            member __.add t i v e         = add t i v e
        }

    let create () =
        let handlers = Dictionary<Type, List<obj -> unit>>()
        let add (h : 'e -> unit) : IDisposable =
            lock handlers (fun () ->
                let t = typeof<'e>
                let list =
                    match handlers.TryGetValue t with
                    | (true, l) -> l
                    | (false,_) -> let l = new List<_>()
                                   handlers.Add (t, l)
                                   l
                let h' (o : obj) = h (unbox o)
                list.Add h'
                { new IDisposable with
                    member __.Dispose() = list.Remove h' |> ignore })
        let notify (event : 'e) = 
            lock handlers (fun () ->
                let t = typeof<'e>
                match handlers.TryGetValue t with
                | (true, l) -> l :> (obj -> unit) seq
                | (false,_) -> Seq.empty
                |> Seq.iter (fun h -> h (box event)))

        { new IEventObservable with
            member __.addHandler h = add h
            member __.publish    e = notify e }
