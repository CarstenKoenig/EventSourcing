namespace PlayUno

open Cards
open Game

open System

module EventHandlers =

    let setColor card =
        let color = 
            Cards.color card
            |> function
                | Red -> ConsoleColor.Red
                | Green -> ConsoleColor.Green
                | Blue -> ConsoleColor.Blue
                | Yellow -> ConsoleColor.Yellow
        Console.ForegroundColor <- color

    let resetColor () =
        Console.ResetColor()
    
    let printCard (c : Card) =
      string c

    let printer f (w : IO.TextWriter) v = 
        w.Write(f v : string)

    let logEvent (store : EventSourcing.IEventStore<Game.Id, Game.Event>) 
                 (id : Game.Id, event : Game.Event) = 
        let state = store |> EventSourcing.EventStore.restore Game.currentState id
        match event with
        | GameStarted (id, ps) ->
            printfn "Game %O started with %d players" id ps
            resetColor ()
        | CardOnTop card ->
            setColor card
            printfn "new card on top: %s" (string card)
            resetColor()
        | NextTurn ->
            printfn "[%d] Players %d turn" state.TurnNr state.Player
        | DirectionChanged ->
            printfn "change Direction!"
        | SkipPlayer ->
            printfn "skip Player!"
        | TriedToCheat (player, cheat) ->
            match cheat with
            | WrongTurn ->
                printfn "\t!!Player %d tried to cheat by playing on Player %d turn!!" player state.Player
            | InvalidCard c ->
                printfn "\t!!Player %d played a invalid card: %A!!" player c