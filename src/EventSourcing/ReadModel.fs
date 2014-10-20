namespace EventSourcing

open System

type IKeyValueStore<'key, 'value> =
    abstract Read : 'key -> 'value option
    abstract Save : 'key -> 'value -> unit

module ReadModel =
    
    type T<'key,'ev,'state,'result> = 
        internal {
          projection : Projection.T<'ev,'state,'result>
          getKey     : EntityId -> 'ev -> 'key
          store      : IKeyValueStore<'key,'state>
        }

    let create (kvs : IKeyValueStore<'key,'state>) (p : Projection.T<'ev,'state,'result>) key =
        { projection = p
          getKey = key
          store  = kvs
        }

    let internal readCurrentState (rm : T<'key,'ev,'state,'result>) (key : 'key) : 'state =
        match rm.store.Read key with
        | Some state -> state
        | None       -> 
            Projection.initValue rm.projection

    let internal eventHandler 
        (rm : T<'key,'ev,'state,'result>) 
        (id : EntityId, event : 'ev) : unit =
        let key = rm.getKey id event
        let nextState = Projection.step rm.projection (readCurrentState rm key) event
        rm.store.Save key nextState

    let load (rm : T<'key,'ev,'state,'result>) (key : 'key) : 'result =
        readCurrentState rm key |> Projection.project rm.projection