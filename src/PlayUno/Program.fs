namespace PlayUno

module Main =

    open EventSourcing
    open Game
    open Cards

    [<EntryPoint>]
    let main _ = 

        // prepare the infrastructure

        use store =
            Repositories.InMemory.create false
            |> EventStore.fromRepository 

        let execute = store.run

        use unsub =
            store.subscribe (EventHandlers.logEvent store)

        // make a nice little dsl

        let gameId = 
            execute <| startGame (4, card (3, Red))

        let plays card pnr =
            execute <| playCard gameId (pnr, card)
            
        let player pnr = pnr

        // **** let's play a game

        player 0 |> plays (card (3, Blue))
        player 1 |> plays (card (8, Blue))
        player 2 |> plays (card (8, Yellow))
        player 3 |> plays (card (4, Blue))
        player 3 |> plays (card (4, Yellow))
        player 1 |> plays (card (4, Red))
        player 0 |> plays (card (4, Green))
        player 1 |> plays (kickBack Green)
        player 0 |> plays (skip Green)

        System.Console.ReadLine() |> ignore
        0