namespace EventSourcing.EntityFramework

open System
open EventSourcing

open Newtonsoft.Json

// implements an event-store using Microsofts EntityFramework
// the events are serialized using JSON.net

module EventStore =

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

        member this.EntityIds() : Collections.Generic.HashSet<_> =
            new Collections.Generic.HashSet<_>(this.EventRows.Select(fun row -> row.entityId))

        member this.Add (id : EntityId, event : 'e) : EventRow =
            let versions = this.EventRows
                              .Where(fun e -> e.entityId = id)
                              .OrderByDescending(fun e -> e.version)
                              .Select(fun e -> e.version);
            let version = if Seq.isEmpty versions then 1 else Seq.head versions + 1
            let ereignis = 
                this.EventRows.Add(
                    { number        = -1
                    ; insertTime    = DateTime.Now
                    ; entityId      = id
                    ; version       = version
                    ; jsonData      = serialize event })
            if this.SaveChanges() <> 1 then failwith "a EventRow was not saved"
            ereignis

        member this.LoadProjection(p : Projection.T<_,_,'a>, id : EntityId) : 'a =
            this.EventRows
                .Where(fun e -> e.entityId = id)
                .OrderBy(fun e -> e.version)
                .Select(fun e -> e.jsonData)
                .AsEnumerable()
            |> Seq.map deserialize
            |> Projection.fold p

    let create (connection) : IEventStore =
        
        let useContext (f : StoreContext -> 'a) =
            use context = new StoreContext(connection)
            f context

        let entityIds () =
            useContext (fun c -> c.EntityIds())


        let addEvent (id : EntityId) (e : 'e) =
            useContext (fun c -> c.Add (id, e)) |> ignore

        let projectEvents (p : Projection.T<'e,_,'a>) (id : EntityId) : 'a =
            useContext (fun c -> c.LoadProjection (p, id))

        { new IEventStore with
            member __.entityIds ()  = entityIds ()
            member __.add id ev     = addEvent id ev
            member __.playback p id = projectEvents p id }
