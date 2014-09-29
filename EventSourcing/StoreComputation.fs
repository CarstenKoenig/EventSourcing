namespace EventSourcing

module StoreComputation =

    /// keeps track of used entities and their latest version inside a store-computation
    type UsedEntities = Map<EntityId, Version>

    let private updateUsed (id : EntityId, ver : Version) (u : UsedEntities) : UsedEntities =
        match u.TryFind id with
        | None   -> u.Add (id, ver)
        | Some _ -> u.Remove id |> Map.add id ver

    let private removeUsed (id : EntityId) (u : UsedEntities) : UsedEntities =
        u.Remove id

    let private latestUsedVersion (id : EntityId) (u : UsedEntities) : Version option =
        u.TryFind id

    type T<'a> = private { run : IEventRepository -> ITransactionScope -> UsedEntities -> ('a * UsedEntities) }    
    
    let private create f = { run = f }

    // **********************
    // Monad implementation

    let returnS (a : 'a) : T<'a> =
        { run = fun _ _ u -> (a, u) }

    let bind (m : T<'a>) (f : 'a -> T<'b>) : T<'b> =
        { run = fun r t u -> let (a, u2)  = m.run r t u
                             let (b, u') = (f a).run r t u2
                             (b, u') }
        
    let combine (a : T<'a>) (b : T<'b>) : T<'b> =
        create (fun r t u -> let (_, u') = a.run r t u
                             b.run r t u')

    let (>>=) = bind

    type StoreComputationBuilder internal () =
        member __.Bind(m, f) = m >>= f
        member __.Return(v) = returnS v
        member __.ReturnFrom(v) = v
        member __.Delay(f) = f ()     
        member __.Zero() = returnS ()   
        member __.Combine (a,b) = combine a b

    let store = StoreComputationBuilder()

    let internal run (rep : IEventRepository) (ts : ITransactionScope) (comp : 'a T) =
        let (res, _) = comp.run rep ts Map.empty
        res

    // *********************
    // public operations

    /// does the entity exists inside the used repository?
    let exists (id : EntityId) : T<bool> =
        create (fun r t u ->
            (r.exists (t,id), u))

    let read (rm : ReadModel<'key, 'value>) (key : 'key) : T<'value> =
        create (fun _ _ u ->
            let value = rm.Read key
            (value, u))

    /// restore a value from the repository using a projection
    /// tracks the latest version of this entity
    let restore (p : Projection.T<'e,_,'a>) (id : EntityId) : T<'a> =
        create (fun r t u ->
            let (a, _) = r.restore (t, id, p)
            (a, u))

    /// adds another event for a entity into the repository
    /// using the internal used entity-version (concurrency-check)
    let add (id : EntityId) (event : 'e) : T<unit> =
        create (fun r t u ->
            let ver  = u |> latestUsedVersion id
            let ver' = r.add (t, id, ver, event)
            let u'   = u |> updateUsed (id, ver')
            ((), u'))

    /// dissables the next concurrency check for an entity by removing
    /// the cached version
    let ignoreNextConccurrencyCheckFor (id : EntityId) : T<unit> =
        create (fun _ _ u ->
            let u'   = u |> removeUsed id
            ((), u'))
            
[<AutoOpen>]
module StoreComputationOperations =
    open StoreComputation

    let store = store