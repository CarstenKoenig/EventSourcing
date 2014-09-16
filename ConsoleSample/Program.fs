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
        Projection.single (
            function
            | Created i -> Some i
            | _         -> None)
 
    /// the current location of a container
    let location = 
        Projection.latest (
            function
            | MovedTo l -> Some l
            | _ -> None )
 
    /// the netto-weight, assuming a container itself is 2.33t
    let nettoWeight = 
        ((+) 2.33<t>) $ Projection.sumBy (
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
            | Loaded (g,w) ->   
                match m.TryFind g with
                | Some w' -> m |> Map.remove g |> Map.add g (w+w')
                | None    -> m |> Map.add g w
            | Unloaded (g,w) -> 
                match m.TryFind g with
                | Some cur -> 
                    if cur < w then
                        failwith (sprintf "tried to unload %.2ft %s but there are only %.2ft" (cur / 1.0<t>) g (w / 1.0<t>)) 
                    elif cur = w then
                        m |> Map.remove g
                    else
                        m |> Map.remove g |> Map.add g (cur-w)
                | None -> 
                    failwith (sprintf "tried to unload %.2ft non-loaded goods %s" (w / 1.0<t>) g)
            | _ -> m)
 
    // of course we can compose these:
 
    /// is the container heavier than it should be? (assuming the max. weight is 28t)
    let isOverloaded = (fun netto -> netto > 28.0<t>) $ nettoWeight
 
    /// collects information about the current state of a certain container
    type ContainerInfo = { id : Id; location : Location; netto : Weight; overloaded : bool; goods : (Goods * Weight) list }
    let createInfo i l n o g = { id = i; location = l; netto = n; overloaded = o; goods = g }
 
    /// current container-info
    let containerInfo =
        createInfo $ id <*> location <*> nettoWeight <*> isOverloaded <*> goods

    // *************************
    // commands:

    let assertExists (id : Id) : StoreComputation.T<unit> =
        store {
            let! containerExists = StoreComputation.exists id
            if not containerExists then failwith "container not found" }

    let createContainer : StoreComputation.T<Id> =
        store {
            let id = Id.NewGuid()
            let ev = Created id
            do! StoreComputation.add id ev
            return id }

    let shipTo (l : Location) (id : Id) : StoreComputation.T<unit> =
        store {
            do! assertExists id
            let ev = MovedTo l
            do! StoreComputation.add id ev }

    let load (g : Goods) (w : Weight) (id : Id) : StoreComputation.T<unit> =
        store {
            do! assertExists id
            let ev = Loaded (g,w)
            do! StoreComputation.add id ev }

    let unload (g : Goods) (w : Weight) (id : Id) : StoreComputation.T<unit> =
        store {
            let! loaded = StoreComputation.restore (goodWeight g) id
            if w > loaded then failwith "cannot unload more than is loaded"
            let ev = Unloaded (g,w)
            do! StoreComputation.add id ev }

    // ******************
    // example

    /// run a basic example
    let run (rep : IEventRepository) =

        let eventStore = EventStore.fromRepository rep 

        let writeWithColor (c : ConsoleColor) (s : string) =
            Console.ForegroundColor <- c
            Console.WriteLine s
            Console.ResetColor()

        let writeEvent = writeWithColor ConsoleColor.Yellow
        let showCaption = (fun s -> s + "\n------------------") >> writeWithColor ConsoleColor.Green

        // subscribe an event-handler for logging...
        use unsubscribe = 
            eventStore.subscribe (
                function
                | (id, Created _)      -> sprintf "container %A created" id
                | (id, MovedTo l)      -> sprintf "container %A moved to %s"  id l
                | (id, Loaded (g,w))   -> sprintf "container %A loaded %.2ft of %s" id (w / 1.0<t>) g
                | (id, Unloaded (g,w)) -> sprintf "container %A UNloaded %.2ft of %s" id (w / 1.0<t>) g
                >> writeEvent)

        // insert some sample history
        let container = 
            store {
                let! container = createContainer
                do!  container |> shipTo "Barcelona"
                do!  container |> load   "Tomatoes" (toT 3500.0<kg>)
                do!  container |> shipTo "Hamburg"
                do!  container |> unload "Tomatoes"         2.5<t>
                do!  container |> load   "Fish"            20.0<t>
                do!  container |> shipTo "Hongkong"
                return container } 
            |> EventStore.execute eventStore


        // just show all events
        showCaption ("\n\nEvents")
        container
        |> StoreComputation.restore (Projection.events())
        |> EventStore.execute eventStore
        |> List.iteri (fun i (ev : Container) -> printfn "Event %d: %A" (i+1) ev)
        Console.WriteLine("=============================")

        showCaption ("\n\nResult")
        let showGoods (goods : (Goods*Weight) list) =
            let itms = goods |> List.map (fun (g,w) -> sprintf "  %6.2ft %s" (w / 1.0<t>) g)
            String.Join("\n", itms)

        // aggregate the history into a container-info and print it
        container 
        |> StoreComputation.restore containerInfo 
        |> EventStore.execute eventStore
        |> (fun ci -> printfn "Container %A\ncurrently in %s\nloaded with:\n%s\nfor a total of %.2ft\nis overloaded: %A" 
                        ci.id ci.location (showGoods ci.goods) (ci.netto / 1.0<t>) ci.overloaded)

module Main =

    [<EntryPoint>]
    let main argv = 
#if MONO 
        use rep = Repositories.Sqlite.openAndCreate ("URI=file::memory:", true)
#else
        use rep = Repositories.EntityFramework.create ("TestDb", true)
#endif

        Example.run rep
        printfn "Return to close"
        Console.ReadLine() |> ignore
        0 // return an integer exit code
