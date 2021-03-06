﻿namespace EventSourcing.Repositories

open System
open EventSourcing

open Newtonsoft.Json
open Mono.Data.Sqlite 

// implements an event-store using Microsofts EntityFramework
// the events are serialized using JSON.net
module Sqlite =

    [<AutoOpen>]
    module internal Database =

        type EntityId = System.Guid
        type Number   = int
        type Version  = int
        type JsonData = string

        let serialize (a : 'a) : JsonData = JsonConvert.SerializeObject a
        let deserialize (json : JsonData) : 'a = JsonConvert.DeserializeObject<'a>(json)

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
            if con.State <> Data.ConnectionState.Open then con.Open()
            if not <| checkForTable con then
                use cmd = new SqliteCommand("CREATE TABLE EntityEvents(eventId INTEGER PRIMARY KEY ASC, entityId TEXT, version INTEGER, json TEXT); CREATE UNIQUE INDEX entityEvent ON EntityEvents(entityId, version)", con)
                cmd.ExecuteNonQuery() |> ignore

        type EntityEvent internal (builder) =
            inherit System.Data.DataRow(builder)

            member this.EventId 
                with get()  : int = unbox <| this.Item "eventId" 

            member this.EntityId 
                with get ()  : EntityId  = EntityId (this.Item "entityId" |> string)
                and  set (id : EntityId) = this.["entityId"] <- string id

            member this.Version 
                with get()  : int           = unbox <| this.Item "version" 
                and  set (v : int)          = this.["version"] <- v

            member this.Json    
                with get()  : string        = unbox <| this.Item "json" 
                and  set (s : string)       = this.["json"] <- s

        type EntityIdRow internal (builder) =
            inherit System.Data.DataRow(builder)

            member this.EventId 
                with get()  : int = unbox <| this.Item "eventId" 

            member this.EntityId 
                with get ()  : EntityId  = EntityId (this.Item "entityId" |> string)
                and  set (id : EntityId) = this.["entityId"] <- string id

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
                new EntityEvent(builder) :> _
            member this.Events 
                with get() : EntityEvent seq = 
                    this.Rows |> Seq.cast<EntityEvent>

        type EntityIdTable() =
            inherit System.Data.DataTable ("EntityEvents")
            do
                base.Columns.Add("entityId", typeof<string>) |> ignore
            override this.GetRowType() =
                typeof<EntityIdRow>
            override this.NewRowFromBuilder (builder) =
                EntityIdRow(builder) :> _
            member this.Ids 
                with get() : EntityIdRow seq = 
                    this.Rows |> Seq.cast<EntityIdRow>

        let addParam (name : string, dbType : Data.DbType, v : obj) (cmd : SqliteCommand) =
            let param = SqliteParameter(dbType, v)
            param.ParameterName <- name
            cmd.Parameters.Add param |> ignore


        let getEvents (id : EntityId) (con : SqliteConnection) =
            use adp = new SqliteDataAdapter("SELECT eventId, entityId, version, json FROM EntityEvents WHERE entityId = @eId ORDER BY version ASC", con)
            adp.SelectCommand |> addParam ("@eId", Data.DbType.String, string id)
            let tbl = new EntityEvents()
            let count = adp.Fill(tbl)
            tbl

        let getLatestVersion (id : EntityId) (con : SqliteConnection) =
            use cmd = new SqliteCommand("SELECT version FROM EntityEvents WHERE entityId = @eId ORDER BY version DESC LIMIT 1", con)
            cmd |> addParam ("@eId", Data.DbType.String, string id)
            let res = cmd.ExecuteScalar()
            if res = null then 0L else res :?> Int64

        let exists (id : EntityId) (con : SqliteConnection) =
            con |> getLatestVersion id <> 0L

        let allIds (con : SqliteConnection) =
            use adp = new SqliteDataAdapter("SELECT DISTINCT entityId FROM EntityEvents", con)
            let tbl = new EntityIdTable()
            let count = adp.Fill(tbl)
            tbl.Ids |> Seq.map (fun row -> row.EntityId)

        let addEvent (entityId : EntityId, afterVersion : int option, event : 'e) (con : SqliteConnection) =
            let json = serialize event
            let latestVer = con |> getLatestVersion entityId
            let newVer = latestVer + 1L
            if Option.isSome afterVersion && int64 afterVersion.Value <> latestVer then 
                raise (EntityConcurrencyException (sprintf "concurrency error while adding an event")) 
            else
            use cmd = new SqliteCommand("insert into EntityEvents (entityId, version, json) values (@eId,@eVer,@json)", con)
            cmd |> addParam ("@eId", Data.DbType.String, string entityId)
            cmd |> addParam ("@eVer", Data.DbType.Int32, newVer)
            cmd |> addParam ("@json", Data.DbType.String, json)
            if cmd.ExecuteNonQuery() <> 1 then failwith "could not write the row into the table"
            int newVer

        let loadProjection(p : Projection.T<_,_,'a>, id : EntityId) (con : SqliteConnection) : ('a * Version) =
            let pAndVer = p <|> Projection.sumBy (fun _ -> Some 1)
            use evts = con |> getEvents id
            evts.Events
            |> Seq.map (fun ev -> deserialize ev.Json)
            |> Projection.fold pAndVer

    type internal SqliteTransaction (con : SqliteConnection, useTransactions) =
        let trans = if useTransactions then Some <| con.BeginTransaction() else None

        member __.execute (f : SqliteConnection -> 'a) = f con
        member __.Commit() = trans |> Option.iter (fun t -> t.Commit())
        member __.Rollback() = trans |> Option.iter (fun t -> t.Rollback())

        interface ITransactionScope with
            member __.Dispose() = 
                trans |> Option.iter (fun t -> t.Dispose())

    /// creates an event-repository using the given sqlite-connection
    /// this will check if a EntityEvents table exists and if not create one in the database
    /// if the repository will get disposed it will dispose the connection with it
    let create (connection : SqliteConnection, useTransactions : bool) : IEventRepository<EntityId, 'event> =
        
        let call f (t : ITransactionScope) = f (t :?> SqliteTransaction)
        let execute f t = call (fun t -> t.execute f) t

        { new IEventRepository<EntityId, 'event> with
            member __.Dispose()            = connection.Dispose()
            member __.add (t,id,ver,event) = t |> execute (addEvent (id,ver,event))
            member __.exists (t,id)        = t |> execute (exists id)
            member __.restore (t,id,p)     = t |> execute (loadProjection (p,id))
            member __.beginTransaction ()  = new SqliteTransaction (connection, useTransactions) :> ITransactionScope
            member __.rollback t           = t |> call (fun t -> t.Rollback())
            member __.commit   t           = t |> call (fun t -> t.Commit())
            member __.allIds t             = t |> execute allIds
        }

    /// creates an event-repository using the given sqlite-connection-string
    /// this will check if a EntityEvents table exists and if not create one in the database
    let openAndCreate (conStr : string, useTransactions : bool) : IEventRepository<EntityId, 'event> =
        let con = new SqliteConnection (conStr)
        con |> createEntityEventsTable
        create (con, useTransactions)