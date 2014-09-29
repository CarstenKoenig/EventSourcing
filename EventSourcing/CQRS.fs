namespace EventSourcing

module CQRS =

    open System
    open System.Collections.Generic

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

    let restore (pr : Projection.T<_,_,'a>) (eId : EntityId) (model : T<_>) : 'a =
        model.store |> EventStore.restore pr eId
    
    let registerReadModelSink 
        (update : IEventStore -> (EntityId * 'ev) -> unit) 
        (model : T<_>) : IDisposable =
        let unsubscribe =
            model
            |> subscribe (fun (entityId, event) ->
                update model.store (entityId, event))
        model.registeredSinks.Add unsubscribe
        { new IDisposable with 
            member __.Dispose() = 
                model.registeredSinks.Remove(unsubscribe) |> ignore
                unsubscribe.Dispose()
        }


