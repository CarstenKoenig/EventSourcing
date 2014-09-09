namespace EventSourcing.Repositories

open System
open EventSourcing

open Newtonsoft.Json

// implements an event-store using Microsofts EntityFramework
// the events are serialized using JSON.net

module EntityFramework =

    open System.Data.Entity
    open System.ComponentModel.DataAnnotations
    open System.ComponentModel.DataAnnotations.Schema
    open System.Linq

    type Number   = int
    type Version  = int
    type JsonData = string

    [<CLIMutable>]
    type EventRow =
        { 
          [<Key; DatabaseGenerated (DatabaseGeneratedOption.Identity); Schema.Column(Order=0) >]
          number                : Number

          [<Schema.Column(Order=1) >]
          insertTime            : DateTime

          [<Index ("EntityKey", 1, IsUnique = true); Schema.Column(Order=2)>]
          entityId              : EntityId

          [<Index ("EntityKey", 2, IsUnique = true); Schema.Column(Order=3)>]
          version               : Version

          [<Schema.Column(TypeName= "text")>]
          jsonData              : string;
        }

    type StoreContext(connectionName : string) =
        inherit DbContext(connectionName)

        let serialize (a : 'a) : JsonData = JsonConvert.SerializeObject a
        let deserialize (json : JsonData) : 'a = JsonConvert.DeserializeObject<'a>(json)

        [<DefaultValue>]
        val mutable private _eventRows : DbSet<EventRow>
        member this.EventRows  
            with get() = this._eventRows
            and  set(v) = this._eventRows <- v

        member this.ClearTables() =
            this.Database.ExecuteSqlCommand("DELETE FROM EventRows")
            |> ignore

        member this.Exists(id : EntityId) : bool =
            this.EventRows.Any (fun row -> row.entityId = id)

        member this.Add (id : EntityId, addAfter : Version option, event : 'e) : Version =
            let versions = this.EventRows
                              .Where(fun e -> e.entityId = id)
                              .OrderByDescending(fun e -> e.version)
                              .Select(fun e -> e.version);
            let lastVersion = if Seq.isEmpty versions then 0 else Seq.head versions
            if Option.isSome addAfter && lastVersion <> addAfter.Value then
                failwith (sprintf "concurrency-error: expected to add event after version %d but found last version to be %d" addAfter.Value lastVersion)
            let version = lastVersion + 1
            let ereignis = 
                this.EventRows.Add(
                    { number        = -1
                    ; insertTime    = DateTime.Now
                    ; entityId      = id
                    ; version       = version
                    ; jsonData      = serialize event })
            if this.SaveChanges() <> 1 then failwith "a EventRow was not saved"
            version

        member this.LoadProjection(p : Projection.T<_,_,'a>, id : EntityId) : ('a * Version) =
            let pAndVer = p <|> Projection.sumBy (fun _ -> Some 1)
            this.EventRows
                .Where(fun e -> e.entityId = id)
                .OrderBy(fun e -> e.version)
                .Select(fun e -> e.jsonData)
                .AsEnumerable()
            |> Seq.map deserialize
            |> Projection.fold pAndVer

    type TransactionScope internal (connection, useTransactions) =
        let context = new StoreContext (connection)
        let trans = if useTransactions then Some <| context.Database.BeginTransaction() else None

        member this.execute (f : StoreContext -> 'a) = 
            f context
        member this.Commit() = 
            trans |> Option.iter (fun t -> t.Commit())
        member this.Rollback() = 
            trans |> Option.iter (fun t -> t.Rollback())

        interface ITransactionScope with
            member __.Dispose() = 
                trans |> Option.iter (fun t -> t.Dispose())
                context.Dispose()

    /// creates an event-repository using the given connection-string to form a entity-framework DbContext
    /// the used DB should contain a EventRows-table consisting holding EventRow data
    let create (connection, useTransactions : bool) : IEventRepository =
        
        let useContext (f : StoreContext -> 'a) =
            use context = new StoreContext(connection)
            f context

        let useTransaction (f : StoreContext -> 'a) (t : TransactionScope) =
            t.execute f

        let exists id =
            useContext (fun c -> c.Exists id)

        let addEvent (id : EntityId) (ver : Version option) (e : 'e) =
            useTransaction (fun c -> c.Add (id, ver, e))

        let restore (p : Projection.T<'e,_,'a>) (id : EntityId) =
            useTransaction (fun c -> c.LoadProjection (p, id))

        { new IEventRepository with
            member __.add (t,id,ver,event) = addEvent id ver event (t :?> TransactionScope)
            member __.exists id            = exists id
            member __.restore (t,id,p)     = restore p id (t :?> TransactionScope)
            member __.beginTransaction ()  = new TransactionScope (connection, useTransactions) :> ITransactionScope
            member __.rollback t           = (t :?> TransactionScope).Rollback()
            member __.commit   t           = (t :?> TransactionScope).Commit()
        }