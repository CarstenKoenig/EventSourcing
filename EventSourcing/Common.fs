namespace EventSourcing

type EntityId = System.Guid
type Version  = int

[<AutoOpen>]
module internal Common =

    open System.Collections.Generic

    let (|Contains|_|) (k : 'k) (d : Dictionary<'k,'v>) =
        match d.TryGetValue k with
        | (true, v)  -> Some v
        | (false, _) -> None

