namespace EventSourcing.Tests

open System
open Xunit
open FsCheck.Xunit
open FsUnit.Xunit 
open Moq

   
module ``when adding events to an event-store`` =
    open EventSourcing

    [<AutoOpen>]
    module SystemUnderTest =

        type T = 
            private {
              repoMock     : Mock<IEventRepository> 
              entityId     : Guid
              addTestevent : StoreComputation.T<unit>
              eventStore   : IEventStore 
            }

        let create (ev : string) : T =
            let repoMock = new Mock<EventSourcing.IEventRepository>()
            let createTrans() =
                let transMock = new Mock<ITransactionScope>()
                transMock.Object
            repoMock.Setup(fun r -> r.beginTransaction()).Returns(fun () -> createTrans()) |> ignore

            let entityId = Guid.NewGuid()
            let addTestevent = StoreComputation.add entityId ev
            let eventStore = EventStore.fromRepository repoMock.Object
            { repoMock = repoMock
            ; entityId = entityId
            ; addTestevent = addTestevent
            ; eventStore = eventStore }

        let runComputation (comp : StoreComputation.T<'a>) (sut : T) : 'a =
            sut.eventStore.run comp

        let run (sut : T) =
            sut.eventStore.run sut.addTestevent

        let subscribe h (sut : T) =
            sut.eventStore.subscribe h

        let wasAdded (ev : string) (sut : T) =
            sut.repoMock.Verify(fun r -> r.add (It.IsAny<_>(), sut.entityId, It.IsAny<_>(), ev))

        let wasRolledBack (sut : T) =
            sut.repoMock.Verify ((fun r -> r.rollback (It.IsAny<_>())), Times.Never)

        let wasCommited (sut : T) =
            sut.repoMock.Verify((fun r -> r.commit (It.IsAny<_>())), Times.Once)

        let entityId (sut : T) = sut.entityId


    [<Fact>]
    let ``it should be added to the underlying repository`` () = 
        let sut = create "Testevent"
        run sut   
        sut |> wasAdded "Testevent"
    
    module ``given: the event-store has observable support`` =
        
        [<Fact>]
        let ``registered handlers should be called with the entityId and the event`` () = 
            let sut = create "Testevent"
            let event = ref ""
            let recId = ref Guid.Empty
            use unsub = sut |> subscribe (fun (id,e) -> recId := id; event := e)

            run sut
            !recId |> should equal (entityId sut)
            !event |> should equal "Testevent"


    module ``the event-store has observable support and one handler will throw an exception`` =
        

        let badHandler (_ : EntityId, _ : string) =
            failwith "error in handler"

        [<Fact>]
        let ``the transaction should not be rolled back`` () = 
            let sut = create "Testevent"
            use unsub = sut |> subscribe badHandler

            (fun () -> run sut) |> ignoreExceptions ()
            sut |> wasRolledBack

        [<Fact>]
        let ``the transaction should be commited anyway`` () = 
            let sut = create "Testevent"
            use unsub = sut |> subscribe badHandler

            (fun () -> run sut) |> ignoreExceptions ()
            sut |> wasCommited

        [<Fact>]
        let ``the exception should be raised to the caller`` () =
            let sut = create "Testevent"
            use unsub = sut |> subscribe badHandler

            (fun () -> run sut)
            |> should throw typeof<HandlerException>
