namespace EventSourcing

type ITransactionScope =
    inherit System.IDisposable

type IEventRepository =
    abstract beginTransaction : unit -> ITransactionScope
    abstract commit           : ITransactionScope -> unit
    abstract rollback         : ITransactionScope -> unit
    abstract exists           : EntityId -> bool
    abstract restore          : ITransactionScope -> EntityId -> Projection.T<'e,_,'a> -> ('a * Version)
    abstract add              : ITransactionScope -> EntityId -> Version option -> 'a -> Version

module StoreComputation =

    type UsedEntities = Map<EntityId, Version>

    let private updateUsed (id : EntityId, ver : Version) (u : UsedEntities) : UsedEntities =
        match u.TryFind id with
        | None   -> u.Add (id, ver)
        | Some _ -> u.Remove id |> Map.add id ver

    let private getUsed (id : EntityId) (u : UsedEntities) : Version option =
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


    // *********************
    // public operations

    let exists (id : EntityId) : T<bool> =
        create (fun r _ u ->
            (r.exists id, u))

    let restore (p : Projection.T<'e,_,'a>) (id : EntityId) : T<'a> =
        create (fun r t u ->
            let (a, ver) = r.restore t id p
            let u'       = u |> updateUsed (id, ver)
            (a, u'))

    let add (id : EntityId) (event : 'e) : T<unit> =
        create (fun r t u ->
            let ver  = u |> getUsed id
            let ver' = r.add t id ver event
            let u'   = u |> updateUsed (id, ver')
            ((), u'))

    let executeIn (rep : IEventRepository) (comp : T<'a>) : 'a =
        use trans = rep.beginTransaction ()
        try
            let (res, _) = comp.run rep trans Map.empty
            rep.commit trans
            res
        with
        | _ ->
            rep.rollback trans
            reraise()
            
[<AutoOpen>]
module StoreComputationOperations =
    open StoreComputation

    let store = store