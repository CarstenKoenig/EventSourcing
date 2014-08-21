namespace EventSourcing.ConsoleSample

open System
open EventSourcing

module Example =

    // it's all about cargo containers, that gets created, moved and loaded/unloaded
 
    [<Measure>] type kg
    [<Measure>] type t
    let toKg (t : float<t>)   : float<kg> = t  * 1000.0<kg/t>
    let toT  (kg : float<kg>) : float<t>  = kg / 1000.0<kg/t>
 
    type Id       = Guid
    type Location = String
    type Goods    = string
    type Weight   = float<t>
 
    type Container = 
        | Created    of Id
        | MovedTo    of Location
        | Loaded     of Goods * Weight
        | Unloaded   of Goods * Weight
 
    // let's begin with the fun part
    // insted of focusing on complete aggregates
    // we define some basic views:
 
    /// the id of a container 
    let id = 
        Projection.create Guid.Empty (fun s ev ->
            match ev with
            | Created i -> if s = Guid.Empty 
                            then i
                           else failwith "should not create a container twice"
            | _ -> s)
 
    /// the current location of a container
    let location = 
        Projection.latest (
            function
            | MovedTo l -> Some l
            | _ -> None )
 
    /// the netto-weight, assuming a container itself is 2.33t
    let nettoWeight = 
        ((+) 2.33<t>) <?> Projection.sumBy (
            function
            | Loaded (_,w)   -> Some w
            | Unloaded (_,w) -> Some (-w)
            | _              -> None )
 
    /// weight of a given good (0 if not loaded)
    let goodWeight (g : Goods) = 
        Projection.sumBy (
            function
            | Loaded (g',w)  when g' = g  -> Some w
            | Unloaded (g',w) when g' = g -> Some (-w)
            | _                           -> None )

    /// the loaded goods (with their weight)
    let goods =
        Projection.createWithProjection Map.toList Map.empty (fun m ev ->
            match ev with
            | Loaded (g,w) ->   match m.TryFind g with
                                | Some w' -> m |> Map.remove g |> Map.add g (w+w')
                                | None    -> m |> Map.add g w
            | Unloaded (g,w) -> match m.TryFind g with
                                | Some cur -> if cur < w 
                                                then failwith (sprintf "tried to unload %.2ft %s but there are only %.2ft" (cur / 1.0<t>) g (w / 1.0<t>)) 
                                              elif cur = w
                                                then m |> Map.remove g
                                              else m |> Map.remove g |> Map.add g (cur-w)
                                | None    -> failwith (sprintf "tried to unload %.2ft non-loaded goods %s" (w / 1.0<t>) g)
            | _            -> m
            )
 
    // of course we can compose these:
 
    /// is the container heavier than it should be? (assuming the max. weight is 28t)
    let isOverloaded = (fun netto -> netto > 28.0<t>) <?> nettoWeight
 
    /// collects information about the current state of a certain container
    type ContainerInfo = { id : Id; location : Location; netto : Weight; overloaded : bool; goods : (Goods * Weight) list }
    let createInfo i l n o g = { id = i; location = l; netto = n; overloaded = o; goods = g }
 
    /// current container-info
    let containerInfo =
        createInfo <?> id <*> location <*> nettoWeight <*> isOverloaded <*> goods

    // commands

    let createContainer (store : IEventStore) : Id =
        let id = Id.NewGuid()
        let ev = Created id
        store |> EventStore.add id ev
        id

    let moveTo (id : Id) (l : Location) (store : IEventStore) =
        if store |> EventStore.exists id |> not then failwith "container not found"
        let ev = MovedTo l
        store |> EventStore.add id ev

    let loadGood (id : Id) (g : Goods, w : Weight) (store : IEventStore) =
        if store |> EventStore.exists id |> not then failwith "container not found"
        let ev = Loaded (g,w)
        store |> EventStore.add id ev

    let unloadGood (id : Id) (g : Goods, w : Weight) (store : IEventStore) =
        let loaded = store |> EventStore.playback (goodWeight g) id
        if w > loaded then failwith "cannot unload more than is loaded"
        let ev = Unloaded (g,w)
        store |> EventStore.add id ev

    /// run a basic example
    let run dbName =

        let store = EntityFramework.EventStore.create dbName

        // insert some sample history
        let id = store |> createContainer
        store |> moveTo     id "Bremen"
        store |> loadGood   id ("Tomaten", 3500.0<kg> |> toT)
        store |> moveTo     id "Hamburg"
        store |> unloadGood id ("Tomaten", 2.5<t>)
        store |> loadGood   id ("Fisch", 20.0<t>)

        // aggregate the history into a container-info and print it
        store |> EventStore.playback containerInfo id
        |> (fun ci -> printfn "Container %A currently in %s, loaded with: %A for a total of %.2ft is overloaded: %A" 
                        ci.id ci.location (List.map fst ci.goods) (ci.netto / 1.0<t>) ci.overloaded)

module Main =

    [<EntryPoint>]
    let main argv = 
        let connection = "TestDb"

        // reset the Database
        using (new EntityFramework.EventStore.StoreContext(connection)) (fun c -> c.ClearTables())

        // let created = EntityFramework.EventStore.createDatabase connection
        Example.run connection
        printfn "Return to close"
        Console.ReadLine() |> ignore
        0 // return an integer exit code
