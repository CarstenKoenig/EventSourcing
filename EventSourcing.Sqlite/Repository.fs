namespace EventSourcing.Repositories

open System
open EventSourcing

open Newtonsoft.Json
open Mono.Data.Sqlite 

// implements an event-store using Microsofts EntityFramework
// the events are serialized using JSON.net
module Sqlite =

    type Number   = int
    type Version  = int
    type JsonData = string

    type TableName internal (builder) =
        inherit System.Data.DataRow(builder)
        member this.Name with get() : string = unbox <| this.Item "name" 

    type Tables() =
        inherit System.Data.DataTable ("sqlite_master")
        do
            base.Columns.Add("name", typeof<string>) |> ignore
        override this.GetRowType() =
            typeof<TableName>
        override this.NewRowFromBuilder (builder) =
            TableName(builder) :> _
        member this.TableNames with get() : TableName seq = this.Rows |> Seq.cast<TableName>

    let checkForTable (con : SqliteConnection) =
        use adp = new SqliteDataAdapter("SELECT name FROM sqlite_master WHERE type='table'", con)
        let tbl = new Tables()
        let count = adp.Fill(tbl)
        tbl.TableNames |> Seq.exists (fun row -> row.Name = "EntityEvents")

    let createEntityEventsTable(con : SqliteConnection) =
        if not <| checkForTable con then
            use cmd = new SqliteCommand("CREATE TABLE EntityEvents(eventId INTEGER PRIMARY KEY ASC, entityId TEXT, version INTEGER, json TEXT); CREATE UNIQUE INDEX entityEvent ON EntityEvents(entityId, version)", con)
            cmd.ExecuteNonQuery() |> ignore

    type EntityEvent internal (builder) =
        inherit System.Data.DataRow(builder)

        member this.EventId 
            with get()  : int = unbox <| this.Item "eventId" 

        member this.EntityId 
            with get ()  : System.Guid  = System.Guid (this.Item "entityId" |> string)
            and  set (id : System.Guid) = this.["entityId"] <- string id

        member this.Version 
            with get()  : int           = unbox <| this.Item "version" 
            and  set (v : int)          = this.["version"] <- v

        member this.Json    
            with get()  : string        = unbox <| this.Item "json" 
            and  set (s : string)       = this.["json"] <- s

    type EntityEvents() =
        inherit System.Data.DataTable ("EntityEvents")
        do
            base.Columns.Add("eventId", typeof<int>) |> ignore
            base.Columns.Add("entityId", typeof<string>) |> ignore
            base.Columns.Add("version", typeof<int>) |> ignore
            base.Columns.Add("json", typeof<string>) |> ignore
        override this.GetRowType() =
            typeof<EntityEvent>
        override this.NewRowFromBuilder (builder) =
            EntityEvent(builder) :> _
        member this.Events with get() : EntityEvent seq = this.Rows |> Seq.cast<EntityEvent>

    let addParam (name : string, dbType : Data.DbType, v : obj) (cmd : SqliteCommand) =
        let param = SqliteParameter(dbType, v)
        param.ParameterName <- name
        cmd.Parameters.Add param |> ignore


    let getEvents (id : System.Guid) (con : SqliteConnection) =
        use adp = new SqliteDataAdapter("SELECT eventId, entityId, version, json FROM EntityEvents WHERE entityId = @eId ORDER BY version ASC", con)
        adp.SelectCommand |> addParam ("@eId", Data.DbType.String, string id)
        let tbl = new EntityEvents()
        let count = adp.Fill(tbl)
        printfn "got %d rows ..." count
        tbl.Events

    let addEvent (entityId : System.Guid, version : int, json : string) (con : SqliteConnection) =
        use cmd = new SqliteCommand("insert into EntityEvents (entityId, version, json) values (@eId,@eVer,@json)", con)
        cmd |> addParam ("@eId", Data.DbType.String, string entityId)
        cmd |> addParam ("@eVer", Data.DbType.Int32, version)
        cmd |> addParam ("@json", Data.DbType.String, json)
        cmd.ExecuteNonQuery()

    let testRun() =
        use con = new SqliteConnection("URI=file::memory:")
        con.Open()
        createEntityEventsTable(con)
        printfn "DB created? %A" (checkForTable con)
        let id = System.Guid.NewGuid()
        printfn "add entity row for %A" id
        let cnt = con |> addEvent (id, 1, "testdata")
        printfn "%d rows written.." cnt
        let evs = con |> getEvents id
        evs |> Seq.iter (fun row -> printfn "Ver: %d - data: %s" row.Version row.Json)