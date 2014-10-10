namespace EventSourcing.Repositories

open EventSourcing

module Syncronised =

    type private Agent<'cmd> = MailboxProcessor<'cmd>
    type private Result<'rep> = Success of 'rep | Failure of exn
    type private Reply<'rep> = AsyncReplyChannel<Result<'rep>>
    type private Command = Unsafe of (IEventRepository -> obj) * Reply<obj>

    /// crates a syncronised Repository (using a command-queue inside an agent) around an exisitng repository
    let from (rep : IEventRepository) =
        let cts = new System.Threading.CancellationTokenSource()
        let agent = 
            Agent.Start ( (fun inbox ->
                let rec loop() = 
                    async {
                        let! cmd = inbox.Receive()
                        match cmd with 
                        | Unsafe (f,reply) -> 
                            try 
                                f rep 
                                |> Success
                                |> reply.Reply
                            with
                            | _ as e ->
                                Failure e
                                |> reply.Reply
                        return! loop()
                    }
                loop()), cts.Token)
        let inline sync (f : IEventRepository -> 'a) : 'r = 
            match agent.PostAndReply (fun r -> Unsafe (f >> box, r)) with
            | Success r -> unbox r
            | Failure e -> raise e
        { new IEventRepository with
            member __.Dispose() =
                cts.Cancel()
                cts.Dispose()
            member __.add (t,id,ver,event) = sync (fun rep -> rep.add (t,id,ver,event))
            member __.exists (t,id)        = sync (fun rep -> rep.exists (t,id))
            member __.restore (t, id, p)   = sync (fun rep -> rep.restore (t,id,p))
            member __.beginTransaction ()  = sync (fun rep -> rep.beginTransaction())
            member __.rollback t           = sync (fun rep -> rep.rollback t)
            member __.commit   t           = sync (fun rep -> rep.commit t) }


module InMemory =

    open System.Collections.Generic

    /// creates an in-memory event-repository
    let create (errorOnRollbackEnabled : bool) : IEventRepository =
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
            member __.Dispose()            = cache.Clear()
            member __.add (_,id,ver,event) = add (id,ver) event
            member __.exists (_,id)        = exists id
            member __.restore (_, id, p)   = restore p id
            member __.beginTransaction ()  = emptyScope
            member __.rollback _           = if errorOnRollbackEnabled then failwith "this repository does not support rollbacks - sorry"
            member __.commit   _           = () }
