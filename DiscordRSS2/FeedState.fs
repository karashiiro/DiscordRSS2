module FeedState

open FSharp.Data
open Microsoft.Data.Sqlite
open System

type SeenListEntry = JsonProvider<"""{ "id": "h571so1237lx" }""", RootName="entry">

type FeedState() =
    let serialize (s: Set<string>) =
        s
        |> Set.toArray
        |> Array.map (SeenListEntry.Entry)
        |> Array.map (fun e -> e.JsonValue.ToString())
        |> fun x -> String.Join(',', x)
        |> fun x -> sprintf "[%s]" x

    let deserialize (s: string) =
        s
        |> SeenListEntry.ParseList
        |> Array.map (fun e -> e.Id)
        |> Set.ofArray

    member _.Create (feedKey) =
        try
            task {
                // Persist the state to the database
                use db = new SqliteConnection("Data Source=feeds.db")
                do! db.OpenAsync()

                let insert = db.CreateCommand()
                insert.CommandText <- @"INSERT INTO feed_state (seen_list, seen_feed) VALUES ($list, $feed)"
                insert.Parameters.AddWithValue("$list", serialize Set.empty<string>) |> ignore
                insert.Parameters.AddWithValue("$feed", feedKey) |> ignore

                let! _ = insert.ExecuteNonQueryAsync()

                ()
            }
            |> Async.AwaitTask
            |> Async.RunSynchronously

            Ok Set.empty<string>
        with ex ->
            Error ex.Message

    member _.Retrieve (feedKey) =
        try
            Ok (task {
                // Get the state from the database
                use db = new SqliteConnection("Data Source=feeds.db")
                do! db.OpenAsync()

                let query = db.CreateCommand()
                query.CommandText <- @"SELECT * FROM feed_state WHERE seen_feed = $feed LIMIT 1"
                query.Parameters.AddWithValue("$feed", feedKey) |> ignore

                let! reader = query.ExecuteReaderAsync()
                let sl = reader["seen_list"] :?> String
                return deserialize sl
            }
            |> Async.AwaitTask
            |> Async.RunSynchronously)
        with ex ->
            Error ex.Message

    member _.Update (feedKey, state) =
        try
            task {
                // Persist the state to the database
                use db = new SqliteConnection("Data Source=feeds.db")
                do! db.OpenAsync()

                let insert = db.CreateCommand()
                insert.CommandText <- @"UPDATE feed_state SET seen_list = $list WHERE feed_state.seen_feed = $feed"
                insert.Parameters.AddWithValue("$list", serialize state) |> ignore
                insert.Parameters.AddWithValue("$feed", feedKey) |> ignore

                let! _ = insert.ExecuteNonQueryAsync()

                ()
            }
            |> Async.AwaitTask
            |> Async.RunSynchronously

            Ok state
        with ex ->
            Error ex.Message

    member _.Delete (feedKey) =
        try
            task {
                // Persist the state to the database
                use db = new SqliteConnection("Data Source=feeds.db")
                do! db.OpenAsync()

                let insert = db.CreateCommand()
                insert.CommandText <- @"DELETE FROM feed_state WHERE feeds.seen_feed = $feed"
                insert.Parameters.AddWithValue("$feed", feedKey) |> ignore

                let! _ = insert.ExecuteNonQueryAsync()

                ()
            }
            |> Async.AwaitTask
            |> Async.RunSynchronously

            Ok feedKey
        with ex ->
            Error ex.Message