namespace EventSourcing

module CQRS =

    open System
    open System.Collections.Generic

    type ReadModelSink<'key, 'ev, 'value> =
        abstract React  : ('key * 'ev) -> StoreComputation.T<'value>
        abstract Update : ('key * 'value) -> unit

    type T<'cmd> = 
        internal {
            store           : IEventStore
            commandHandler  : 'cmd -> StoreComputation.T<unit>
            registeredSinks : List<IDisposable>
        } 
        interface IDisposable with
            member this.Dispose() =
                this.registeredSinks |> Seq.iter (fun d -> d.Dispose())
                this.registeredSinks.Clear()
                

    let create (rep : IEventRepository) (cmdHandler : 'cmd -> StoreComputation.T<unit>) =
        let store = EventStore.fromRepository rep
        { store           = store
          commandHandler  = cmdHandler
          registeredSinks = List<IDisposable>()
        }

    let subscribe (handler : EventObservable.EventHandler<'ev>) (model : T<'cmd>) : IDisposable = 
        model.store.subscribe handler

    let execute (cmd : 'cmd) (model : T<'cmd>) =
        let comp = model.commandHandler cmd
        model.store.run comp
    
    let registerReadModelSink 
        (react : (EntityId * 'ev) -> ('key * StoreComputation.T<'value>) option) 
        (update : ('key * 'value) -> unit) 
        (model : T<_>) : IDisposable =
        let unsubscribe =
            model
            |> subscribe (fun (entityId, event) ->
                match react (entityId, event) with
                | Some (key, comp) ->
                    let value = comp |> EventStore.execute model.store
                    update (key, value)
                | None -> ())
        model.registeredSinks.Add unsubscribe
        { new IDisposable with 
            member __.Dispose() = 
                model.registeredSinks.Remove(unsubscribe) |> ignore
                unsubscribe.Dispose()
        }


