namespace EventSourcing.ConsoleSample

open System
open EventSourcing

[<AutoOpen>]
module private Helper =
    let writeWithColor (c : ConsoleColor) (s : string) =
        Console.ForegroundColor <- c
        Console.WriteLine s
        Console.ResetColor()

    let writeEvent = writeWithColor ConsoleColor.Yellow
    let showCaption = (fun s -> s + "\n------------------") >> writeWithColor ConsoleColor.Green


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
    // Readmodel
    let locations = System.Collections.Generic.Dictionary<Id, Location>()
    let locationRM = { new ReadModel<Id,Location> with member __.Read(contId) = locations.[contId] }

    // *************************
    // CQRS
    type Commands = 
        | CreateContainer of Id
        | ShipTo of (Id * Location)
        | Load of (Id * Goods * Weight)
        | Unload of (Id * Goods * Weight)

    let model rep = 
        let assertExists (id : Id) : StoreComputation.T<unit> =
            store {
                let! containerExists = StoreComputation.exists id
                if not containerExists then failwith "container not found" }
        // create the CQRS model
        let model =
            CQRS.create rep (function
                | CreateContainer id ->
                        StoreComputation.add id (Created id)
                | ShipTo (id, l) ->
                    store {
                        do! assertExists id
                        do! StoreComputation.add id (MovedTo l) }
                | Load (id, g, w) ->
                    store {
                        do! assertExists id
                        do! StoreComputation.add id (Loaded (g,w)) }
                | Unload (id, g, w) ->
                    store {
                        do! assertExists id
                        do! StoreComputation.add id (Unloaded (g,w)) }
                )
        // register a sink for the location-dictionary:
        model 
        |> CQRS.registerReadModelSink 
            (fun _ (eId, ev) ->
                match ev with
                | MovedTo l -> locations.[eId] <- l
                | _ -> ())
        |> ignore
        // return the model
        model

    // ******************
    // example

    /// run a basic example
    let run (rep : IEventRepository) =

        let model = model rep

        // subscribe an event-handler for logging...
        use unsubscribe = 
            model |> CQRS.subscribe (
                function
                | (id, Created _)      -> sprintf "container %A created" id
                | (id, MovedTo l)      -> sprintf "container %A moved to %s"  id l
                | (id, Loaded (g,w))   -> sprintf "container %A loaded %.2ft of %s" id (w / 1.0<t>) g
                | (id, Unloaded (g,w)) -> sprintf "container %A UNloaded %.2ft of %s" id (w / 1.0<t>) g
                >> writeEvent)

        // insert some sample history
        showCaption "Log:"
        let container = 
            let container = Id.NewGuid()
            model |> CQRS.execute (CreateContainer container)
            model |> CQRS.execute (ShipTo (container, "Barcelona"))
            model |> CQRS.execute (Load   (container, "Tomatoes", toT 3500.0<kg>))
            model |> CQRS.execute (ShipTo (container, "Hamburg"))
            model |> CQRS.execute (Unload (container, "Tomatoes", 2.5<t>))
            model |> CQRS.execute (Load   (container, "Fish", 20.0<t>))
            model |> CQRS.execute (ShipTo (container, "Hongkong"))
            container


        // just show all events
        showCaption ("\n\ncontained Events")
        model
        |> CQRS.restore (Projection.events()) container
        |> List.iteri (fun i (ev : Container) -> printfn "Event %d: %A" (i+1) ev)
        Console.WriteLine("=============================")

        showCaption ("\n\nResult")
        let showGoods (goods : (Goods*Weight) list) =
            let itms = goods |> List.map (fun (g,w) -> sprintf "  %6.2ft %s" (w / 1.0<t>) g)
            String.Join("\n", itms)

        // aggregate the history into a container-info and print it
        model
        |> CQRS.restore containerInfo container
        |> (fun ci -> printfn "Container %A\ncurrently in %s\nloaded with:\n%s\nfor a total of %.2ft\nis overloaded: %A" 
                        ci.id ci.location (showGoods ci.goods) (ci.netto / 1.0<t>) ci.overloaded)

        // Show the result from the read-model
        showCaption ("\n\nReadmodel:")
        locationRM.Read (container)
        |> printfn "Container %A is currently located in %s" container

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
