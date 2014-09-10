namespace EventSourcing.Tests

open System
open Xunit
open FsCheck.Xunit
open FsUnit.Xunit 
open Moq

   
module ``integration: using a simple domain with an inmemory-store`` =
    open EventSourcing

    Repositories.InMemory.disableRollbackError()

    [<AutoOpen>]
    module SystemUnderTest =

        type NumberValue =
            | Created    of int
            | Added      of int
            | Subtracted of int

        let currentValueP : Projection.T<NumberValue,_,int> =
            Projection.create 0 (fun nv -> 
                function
                | Created n    -> n
                | Added   n    -> nv + n
                | Subtracted n -> nv - n)

        type T = 
            private {
              eventStore   : IEventStore 
            }

        let create () : T =
            let eventStore = 
                Repositories.InMemory.create ()
                |> EventStore.fromRepository
            { eventStore = eventStore }

        let currentValue (id : EntityId) (sut : T) =
            sut.eventStore 
            |> EventStore.restore currentValueP id

        let addNumber (id : EntityId) (n : int) (sut : T) =
            sut.eventStore
            |> EventStore.add id (Added n)

        let subtractNumber (id : EntityId) (n : int) (sut : T) =
            sut.eventStore
            |> EventStore.add id (Subtracted n)

        let run (comp : StoreComputation.T<'a>) (sut : T) : 'a =
            EventStore.execute sut.eventStore comp

        let createNewNumber (init : int) (sut : T) : EntityId =
            store {
                let newId  = EntityId.NewGuid()
                do! StoreComputation.add newId (Created init)
                return newId 
            } |> EventStore.execute sut.eventStore

        let executeTransaction (srcId: EntityId, destId : EntityId) (v : int) (sut : T) =
            store {
                let! vF = StoreComputation.restore currentValueP srcId
                if vF < v then failwith "from-value to small"
                do! StoreComputation.add srcId (Subtracted v)
                do! StoreComputation.add destId (Added v)
            } |> EventStore.execute sut.eventStore


    [<Fact>]
    let ``events for 5 + 6 - 3 should result in a currentValue of 8`` () = 
        let sut = create ()
        let id = createNewNumber 5 sut
        sut |> addNumber id 6
        sut |> subtractNumber id 3

        sut |> currentValue id |> should equal 8

    [<Fact>]
    let ``can add and read from multiple entities`` () = 
        let sut = create ()
        let id = createNewNumber 5 sut
        let id' = createNewNumber 5 sut
        sut |> addNumber id 6
        sut |> addNumber id' 3
        sut |> subtractNumber id 3
        sut |> subtractNumber id' 6

        sut |> currentValue id |> should equal 8
        sut |> currentValue id' |> should equal 2

    [<Fact>]
    let ``the complex transaction will transfer subtract the value from the source and add it to the destination``() =
        let sut = create ()
        let sourceId = createNewNumber 10 sut
        let destId   = createNewNumber 5  sut
        sut |> executeTransaction (sourceId, destId) 8
        sut |> currentValue sourceId |> should equal 2
        sut |> currentValue destId |> should equal 13

    [<Fact>]
    let ``if the complex transaction will fail the current values will not change``() =
        let sut = create ()
        let sourceId = createNewNumber 10 sut
        let destId   = createNewNumber 5  sut
        (fun () -> sut |> executeTransaction (sourceId, destId) 11) |> should throw typeof<exn>
        sut |> currentValue sourceId |> should equal 10
        sut |> currentValue destId |> should equal 5

    [<Fact>]
    let ``a store-computation should throw an error if another event got inserted while running``() =
        let sut = create ()
        let id = sut |> createNewNumber 0
        let insertAnotherOne() = sut |> addNumber id 5
        let workflow =  
            store {
                do! StoreComputation.add id (Added 1)
                insertAnotherOne()
                do! StoreComputation.add id (Added 2)
                return! StoreComputation.restore currentValueP id
            }
        (fun () -> sut |> run workflow |> ignore) |> should throw typeof<EntityConcurrencyException>


