namespace EventSourcing

module Computation =

    /// keeps track of used entities and their latest version inside a store-computation
    type UsedEntities<'id when 'id : comparison> = Map<'id, Version>

    let private updateUsed (id : 'id, ver : Version) (u : UsedEntities<'id>) : UsedEntities<'id> =
        match u.TryFind id with
        | None   -> u.Add (id, ver)
        | Some _ -> u.Remove id |> Map.add id ver

    let private removeUsed (id : 'id) (u : UsedEntities<'id>) : UsedEntities<'id> =
        u.Remove id

    let private latestUsedVersion (id : 'id) (u : UsedEntities<'id>) : Version option =
        u.TryFind id

    type T<'id,'events,'a when 'id : comparison> = 
        private { 
            run : IEventRepository<'id,'events> -> ITransactionScope -> UsedEntities<'id> -> ('a * UsedEntities<'id>) 
        }    
    
    let private create f = { run = f }

    // **********************
    // Monad implementation

    let returnS (a : 'a) : T<_, _, 'a> =
        { run = fun _ _ u -> (a, u) }

    let bind (m : T<_, _, 'a>) (f : 'a -> T<_, _, 'b>) : T<_, _, 'b> =
        { run = fun r t u -> let (a, u2)  = m.run r t u
                             let (b, u') = (f a).run r t u2
                             (b, u') }
        
    let combine (a : T<_, _, 'a>) (b : T<_, _, 'b>) : T<_, _, 'b> =
        create (fun r t u -> let (_, u') = a.run r t u
                             b.run r t u')

    let (>>=) = bind

    type ComputationBuilder internal () =
        member __.Bind(m, f) = m >>= f
        member __.Return(v) = returnS v
        member __.ReturnFrom(v) = v
        member __.Delay(f) = f ()     
        member __.Zero() = returnS ()   
        member __.Combine (a,b) = combine a b

    let Do = ComputationBuilder()

    let internal run (rep : IEventRepository<'id, 'event>) (ts : ITransactionScope) (comp : T<'id,'event,'a>) : 'a =
        let (res, _) = comp.run rep ts Map.empty
        res

    // *********************
    // public operations

    /// does the entity exists inside the used repository?
    let exists (id : 'id) : T<'id, _, bool> =
        create (fun r t u ->
            (r.exists (t,id), u))

    let allIds() : T<'id, _ , 'id seq> =
        create (fun r t u ->
            (r.allIds t, u))

    /// restore a value from the repository using a projection
    /// tracks the latest version of this entity
    let restore (p : Projection.T<'event, _, 'a>) (id : 'id) : T<'id, 'event, 'a> =
        create (fun r t u ->
            let (a, _) = r.restore (t, id, p)
            (a, u))

    /// adds another event for a entity into the repository
    /// using the internal used entity-version (concurrency-check)
    let add (id : 'id) (event : 'event) : T<'id, 'event, unit> =
        create (fun r t u ->
            let ver  = u |> latestUsedVersion id
            let ver' = r.add (t, id, ver, event)
            let u'   = u |> updateUsed (id, ver')
            ((), u'))

    /// dissables the next concurrency check for an entity by removing
    /// the cached version
    let ignoreNextConccurrencyCheckFor (id : 'id) : T<'id, _, unit> =
        create (fun _ _ u ->
            let u'   = u |> removeUsed id
            ((), u'))