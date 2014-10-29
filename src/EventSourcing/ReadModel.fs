namespace EventSourcing

type IKeyValueStore<'key, 'value> =
    abstract Read : 'key -> 'value option
    abstract Save : 'key -> 'value -> unit

module ReadModel =
    
    type T<'key,'id,'event,'state,'result> = 
        internal {
          projection : Projection.T<'event,'state,'result>
          getKey     : 'id -> 'event -> 'key
          store      : IKeyValueStore<'key,'state>
        }

    let create (kvs : IKeyValueStore<'key,'state>) (p : Projection.T<'event,'state,'result>) key =
        { projection = p
          getKey     = key
          store      = kvs
        }

    let internal readCurrentState (rm : T<'key,_, _,'state, _>) (key : 'key) : 'state =
        match rm.store.Read key with
        | Some state -> state
        | None       -> Projection.initValue rm.projection

    let internal eventHandler 
        (rm : T<'key,'id,'event,'state,'result>) 
        (id : 'id, event : 'event) : unit =
        let key = rm.getKey id event
        let nextState = Projection.step rm.projection (readCurrentState rm key) event
        rm.store.Save key nextState

    let load (rm : T<'key,'id,'event,'state,'result>) (key : 'key) : 'result =
        readCurrentState rm key |> Projection.project rm.projection