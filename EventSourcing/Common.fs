namespace EventSourcing

type EntityId = System.Guid
type Version  = int

type ITransactionScope =
    inherit System.IDisposable

type IEventRepository =
    inherit System.IDisposable
    abstract beginTransaction : unit -> ITransactionScope
    abstract commit           : ITransactionScope -> unit
    abstract rollback         : ITransactionScope -> unit
    abstract exists           : ITransactionScope * EntityId -> bool
    abstract restore          : ITransactionScope * EntityId * Projection.T<'e,_,'a> -> ('a * Version)
    /// if an optional version is given teh repository will check that it's the same as the latest entity-event version in the repository
    abstract add              : ITransactionScope * EntityId * Version option * 'a -> Version

exception EntityConcurrencyException of EntityId * string

[<AutoOpen>]
module internal Common =

    open System.Collections.Generic

    let (|Contains|_|) (k : 'k) (d : Dictionary<'k,'v>) =
        match d.TryGetValue k with
        | (true, v)  -> Some v
        | (false, _) -> None

