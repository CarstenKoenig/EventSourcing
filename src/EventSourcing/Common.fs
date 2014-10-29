namespace EventSourcing

type Version  = int

type ITransactionScope =
    inherit System.IDisposable

type IEventRepository<'id,'event> =
    inherit System.IDisposable
    abstract beginTransaction : unit -> ITransactionScope
    abstract commit           : ITransactionScope -> unit
    abstract rollback         : ITransactionScope -> unit
    abstract exists           : ITransactionScope * 'id -> bool
    abstract allIds           : ITransactionScope -> 'id seq
    abstract restore          : ITransactionScope * 'id * Projection.T<'event,_,'res> -> ('res * Version)
    /// if an optional version is given teh repository will check that it's the same as the latest entity-event version in the repository
    abstract add              : ITransactionScope * 'id * Version option * 'event -> Version

exception EntityConcurrencyException of string

[<AutoOpen>]
module internal Common =

    open System.Collections.Generic

    let (|Contains|_|) (key : 'key) (d : Dictionary<'key,'value>) =
        match d.TryGetValue key with
        | (true, value)  -> Some value
        | (false, _)     -> None

