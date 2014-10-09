namespace PlayUno

module Main =

    open EventSourcing
    open Game
    open Cards

    [<EntryPoint>]
    let main _ = 

        use store =
            Repositories.InMemory.create false
            |> EventStore.fromRepository 

        let execute = store.run

        use unsub =
            store.subscribe (EventHandlers.logEvent store)

        let gameId = 
            execute <| startGame (4, card (3, Red))

        execute <| playCard gameId (0, card (3, Blue))
        execute <| playCard gameId (1, card (8, Blue))
        execute <| playCard gameId (2, card (8, Yellow))
        execute <| playCard gameId (3, card (4, Blue))
        execute <| playCard gameId (3, card (4, Yellow))
        execute <| playCard gameId (1, card (4, Red))
        execute <| playCard gameId (0, card (4, Green))
        execute <| playCard gameId (1, kickBack Green)
        execute <| playCard gameId (0, skip Green)

        System.Console.ReadLine() |> ignore

        0 // return an integer exit code