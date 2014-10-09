namespace PlayUno

module Game =

    open Cards
    open EventSourcing

    type Id          = EntityId
    type PlayerNr    = int 
    type PlayerCount = int
    type TurnNr      = int

    type Direction =
        | ClockWise
        | CounterClockWise

    // Events

    type Event =
        | GameStarted      of Id * PlayerCount
        | CardOnTop        of Card
        | NextTurn
        | DirectionChanged
        | SkipPlayer
        | TriedToCheat     of PlayerNr * Cheat
    and Cheat =
        | InvalidCard of Card
        | WrongTurn

    // Projections to parts of the current-game state

    let private nextPlayer (player, players, dir) =
        let wrap p = 
            if p < 0 then 
                players + p
            elif p >= players then
                p % players
            else
                p
        match dir with
        | CounterClockWise -> player-1 |> wrap
        | ClockWise        -> player+1 |> wrap

    let private changedDirection dir = 
        match dir with
        | ClockWise        -> CounterClockWise
        | CounterClockWise -> ClockWise

    let gameId =
        Projection.single (function GameStarted (id,_) -> Some id | _ -> None)

    let turnNumber =
        Projection.sumBy (function NextTurn -> Some 1 | _ -> None)

    let currentDirection =
        Projection.create 
            ClockWise
            (fun dir event ->
                match event with
                | GameStarted (_,ps) -> ClockWise
                | DirectionChanged   -> changedDirection dir
                | _                  -> dir)

    let currentPlayer =
        Projection.createWithProjection 
            (fun (p,_,_) -> p) 
            (-1, 0, ClockWise) 
            (fun (player, players, dir) event ->
                match event with
                | GameStarted (_,ps) -> (-1, ps, ClockWise)
                | SkipPlayer         -> (nextPlayer (player, players, dir), players, dir)
                | NextTurn           -> (nextPlayer (player, players, dir), players, dir)
                | DirectionChanged   -> (player, players, changedDirection dir)
                | _                  -> (player, players, dir))

    let topCard =
        function 
        | CardOnTop c -> Some c 
        | _           -> None
        |> Projection.latest

    type State = 
        { GameId    : Id
          TopCard   : Card
          Player    : PlayerNr
          TurnNr    : int 
          Direction : Direction }

    let private state id c p t d = { GameId = id; TopCard = c; Player = p; TurnNr = t; Direction = d }

    let currentState =
        state $ gameId <*> topCard <*> currentPlayer <*> turnNumber <*> currentDirection

    // Commands

    let private sideEffect (gameId : Id) (card : Card) =
        match card with
        | KickBack _ ->
            StoreComputation.add gameId DirectionChanged
        | Skip _ -> 
            StoreComputation.add gameId SkipPlayer
        | _ -> StoreComputation.returnS ()
        

    let startGame (players : int, firstCard : Card) (store : IEventStore) =
        if players <= 2 then invalidArg "players" "There should be at least 3 players"
        StoreComputation.store {
            let id = Id.NewGuid()
            do! StoreComputation.add id (GameStarted (id, players))
            do! StoreComputation.add id (CardOnTop firstCard)
            do! sideEffect id firstCard
            do! StoreComputation.add id NextTurn
            return id
        } |> store.run 

    let playCard (gameId : Id) (player : PlayerNr, card : Card) (store : IEventStore) =
        StoreComputation.store {
            let! state = gameId |> StoreComputation.restore currentState
            if state.Player <> player then
                do! StoreComputation.add gameId (TriedToCheat (player, WrongTurn))
            elif Cards.isInvalidNextCard state.TopCard card then
                do! StoreComputation.add gameId (TriedToCheat (player, InvalidCard card))
            else
                do! StoreComputation.add gameId (CardOnTop card)
                do! sideEffect gameId card
                do! StoreComputation.add gameId NextTurn
        } |> store.run