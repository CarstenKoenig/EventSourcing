namespace PlayUno

module Main =

    open EventSourcing

    [<EntryPoint>]
    let main _ = 

        use store =
            Repositories.InMemory.create false
            |> EventStore.fromRepository 

        use unsub =
            store.subscribe (EventHandlers.logEvent store)

        let gameId = store |> Game.startGame (4, Cards.card (3, Cards.Red))

        store |> Game.playCard gameId (0, Cards.card (3, Cards.Blue))
        store |> Game.playCard gameId (1, Cards.card (8, Cards.Blue))
        store |> Game.playCard gameId (2, Cards.card (8, Cards.Yellow))
        store |> Game.playCard gameId (3, Cards.card (4, Cards.Blue))
        store |> Game.playCard gameId (3, Cards.card (4, Cards.Yellow))
        store |> Game.playCard gameId (1, Cards.card (4, Cards.Red))
        store |> Game.playCard gameId (0, Cards.card (4, Cards.Green))
        store |> Game.playCard gameId (1, Cards.kickBack Cards.Green)
        store |> Game.playCard gameId (0, Cards.skip Cards.Green)

        System.Console.ReadLine() |> ignore

        0 // return an integer exit code