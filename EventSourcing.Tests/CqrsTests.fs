namespace EventSourcing.Tests

open System
open Xunit
open FsCheck.Xunit
open FsUnit.Xunit 
open Moq

   
module ``integration: testing CQRS interfaces`` =
    open EventSourcing

    [<AutoOpen>]
    module SystemUnderTest =

        type NumberValue =
            | Created    of int
            | Added      of int
            | Subtracted of int

        type Commands =
            | Create   of EntityId * int
            | Add      of EntityId * int
            | Subtract of EntityId * int
            | Transfer of EntityId * EntityId * int

        let private currentValueP : Projection.T<NumberValue,_,int> =
            Projection.create 0 (fun nv -> 
                function
                | Created n    -> n
                | Added   n    -> nv + n
                | Subtracted n -> nv - n)

        type T = 
            private {
              model     : CQRS.T<Commands>
              values    : System.Collections.Generic.Dictionary<EntityId, int>
              readModel : ReadModel<EntityId, int>
            }
            with
            interface IDisposable with
                member i.Dispose() = (i.model :> IDisposable).Dispose()

        let create () : T =
            let rep =
                Repositories.InMemory.create false
                |> Repositories.Syncronised.from
            let model = 
                CQRS.create rep (function
                    | Create (eId, n) -> 
                        StoreComputation.add eId (Created n)
                    | Add (eId, n) ->
                        StoreComputation.add eId (Added n)
                    | Subtract (eId, n) ->
                        StoreComputation.add eId (Subtracted n)
                    | Transfer (fromId, toId, amount) ->
                        store {
                            let! vF = StoreComputation.restore currentValueP fromId
                            if vF < amount then failwith "from-amount to small"
                            do! StoreComputation.add fromId (Subtracted amount)
                            do! StoreComputation.add toId (Added amount)
                        })
            let dict = System.Collections.Generic.Dictionary<EntityId, int>()
            model |> CQRS.registerReadModelSink
                (fun (eId, (_ : NumberValue)) -> Some (eId, StoreComputation.restore currentValueP eId))
                (fun (eId, v)                 -> dict.[eId] <- v)
            |> ignore
            let readModel =
                { new ReadModel<EntityId, int> with
                    member __.Read eId = dict.[eId] }
            { model     = model
            ; values    = dict 
            ; readModel = readModel}

        let currentValue (id : EntityId) (sut : T) =
            sut.values.[id]

        let addNumber (id : EntityId) (n : int) (sut : T) =
            sut.model |> CQRS.execute (Add (id, n))

        let subtractNumber (id : EntityId) (n : int) (sut : T) =
            sut.model |> CQRS.execute (Subtract (id, n))

        let createNewNumber (init : int) (sut : T) : EntityId =
            let eId = EntityId.NewGuid ()
            sut.model |> CQRS.execute (Create (eId,init))
            eId

        let executeTransaction (srcId: EntityId, destId : EntityId) (v : int) (sut : T) =
            sut.model |> CQRS.execute (Transfer (srcId, destId, v))

    [<Fact>]
    let ``events for 5 + 6 - 3 should result in a currentValue of 8`` () = 
        use sut = create ()
        let id = createNewNumber 5 sut
        sut |> addNumber id 6
        sut |> subtractNumber id 3

        sut |> currentValue id |> should equal 8

    [<Fact>]
    let ``can add and read from multiple entities`` () = 
        use sut = create ()
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
        use sut = create ()
        let sourceId = createNewNumber 10 sut
        let destId   = createNewNumber 5  sut
        sut |> executeTransaction (sourceId, destId) 8
        sut |> currentValue sourceId |> should equal 2
        sut |> currentValue destId |> should equal 13

    [<Fact>]
    let ``if the complex transaction will fail the current values will not change``() =
        use sut = create ()
        let sourceId = createNewNumber 10 sut
        let destId   = createNewNumber 5  sut
        (fun () -> sut |> executeTransaction (sourceId, destId) 11) |> should throw typeof<exn>
        sut |> currentValue sourceId |> should equal 10
        sut |> currentValue destId |> should equal 5