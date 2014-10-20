namespace EventSourcing.Repositories

open EventSourcing

module KeyValueStores =
    
    let inMemory() : IKeyValueStore<'key,'value> =
        let cache = System.Collections.Generic.Dictionary<'key,'value>()
        let get key : 'value option =
            lock cache 
                (fun () ->
                    match cache.TryGetValue key with
                    | (true, value) -> Some value
                    | _             -> None)
        let set (key, value) =
            lock cache
                (fun () -> cache.[key] <- value)
        { new IKeyValueStore<'key,'value> with
            member __.Read key = get key
            member __.Save key value = set (key,value) }
