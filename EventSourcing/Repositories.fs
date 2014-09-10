namespace EventSourcing.Repositories

open EventSourcing

module InMemory =

    let mutable private RollbackErrorEnabled = true

    let disableRollbackError() =
        RollbackErrorEnabled <- false

    open System.Collections.Generic

    /// creates an in-memory event-repository
    let create () : IEventRepository =
        let cache = new Dictionary<EntityId, (List<obj>*Version)>()

        let exists id = lock cache (fun () -> cache.ContainsKey id)

        let add (id, ver) e = lock cache (fun () -> 
            match cache with
            | Contains id (l,v) -> (l,v)
            | _ ->
                let tpl = (List<_>(), 0)
                cache.Add (id, tpl)
                tpl
            |> fun (l, v) -> 
                if Option.isSome ver && v <> ver.Value 
                then 
                    raise (EntityConcurrencyException (id, "concurrency exception"))
                else
                    l.Add (box e)
                    let v' = v+1
                    cache.[id] <- (l, v')
                    v')

        let restore p id = lock cache (fun () ->
            match cache with
            | Contains id (l,v) -> (l :> obj seq, v)
            | _                 -> (Seq.empty, 0)
            |> (fun (l,v) -> 
                (Seq.map unbox l |> Projection.fold p, v)))

        let emptyScope = { new ITransactionScope with 
                            member __.Dispose() = () }

        { new IEventRepository with
            member __.add (_,id,ver,event) = add (id,ver) event
            member __.exists id            = exists id
            member __.restore (_, id, p)   = restore p id
            member __.beginTransaction ()  = emptyScope
            member __.rollback _           = if RollbackErrorEnabled then failwith "this repository does not support rollbacks - sorry"
            member __.commit   _           = () }
