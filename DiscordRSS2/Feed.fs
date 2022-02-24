﻿module Feed

open DSharpPlus
open DSharpPlus.CommandsNext
open DSharpPlus.CommandsNext.Attributes
open DSharpPlus.Entities
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Quartz
open System
open System.Collections.Concurrent
open System.Net
open System.Runtime.InteropServices
open System.Threading.Tasks

open Rss

type FeedState() =
    let entries = ConcurrentDictionary<string, Set<string>>()

    member _.Entries = entries

[<PersistJobDataAfterExecution>]
[<DisallowConcurrentExecution>]
type FeedJob(state0: FeedState, client0: DiscordClient, logger0: ILogger<FeedJob>) =
    let state = state0
    let client = client0
    let logger = logger0

    let embed (entry: Rss.Entry) =
        DiscordEmbedBuilder()
        |> fun eb -> eb.WithTitle entry.Title
        |> fun eb -> eb.WithUrl entry.Link.Href
        |> fun eb ->
            match entry.Thumbnail with
            | Some t -> eb.WithThumbnail t.Url
            | None -> eb
        |> fun eb -> eb.WithDescription(sprintf "Posted on <t:%d:f> by [%s](%s)" (entry.Published.ToUnixTimeSeconds()) entry.Author.Name entry.Author.Uri)
        |> fun eb -> eb.Build()

    interface IJob with
        member _.Execute context =
            task {
                try
                    let dataMap = context.JobDetail.JobDataMap

                    let feedKey =
                        (context.JobDetail.Key.Group, context.JobDetail.Key.Name)
                        ||> sprintf "%s-%s"
                    let feedUrl = dataMap.GetString("feedUrl")
                    let mutable feedSeen = state.Entries.GetOrAdd(feedKey, Set.empty)

                    let feedChannelId = dataMap.GetString("feedChannel") |> uint64
                    let! feedChannel = client.GetChannelAsync(feedChannelId)

                    try
                        let! feed = Rss.AsyncLoad(feedUrl)
                        for e in feed.Entries |> Seq.rev do
                            if not (feedSeen |> Set.contains e.Id) then
                                feedSeen <- feedSeen.Add e.Id
                                state.Entries.set_Item(feedKey, feedSeen)
                                let! _ = feedChannel.SendMessageAsync(embed e)
                                do! Task.Delay(200)
                                ()
                    with :?WebException as e ->
                        logger.LogWarning(sprintf "Failed to complete web request (%s): %s" (e.Status.ToString()) e.Message)
                with e ->
                    raise (JobExecutionException(e))
            }

type FeedModule() =
    inherit BaseCommandModule()

    let parseUrlToKey (uri: Uri) =
        uri.Host + uri.PathAndQuery

    [<Command "subscribe"; Description "Subscribe to an RSS feed.">]
    member _.subscribe (ctx: CommandContext, feed: string, [<Optional; DefaultParameterValue(null: DiscordChannel)>] channel: DiscordChannel) =
        task {
            let feedKey = parseUrlToKey (Uri(feed))
            let feedChannel = (if isNull channel then ctx.Channel.Id else channel.Id) |> sprintf "%d"

            // Create the job detail
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let job =
                JobBuilder.Create<FeedJob>()
                |> fun jb -> jb.WithIdentity(feedKey, feedChannel)
                |> fun jb -> jb.UsingJobData("feedUrl", feed)
                |> fun jb -> jb.UsingJobData("feedChannel", feedChannel)
                |> fun jb -> jb.Build()
            let trigger =
                TriggerBuilder.Create()
                |> fun tb -> tb.WithIdentity("trigger__" + feedKey, feedChannel)
                |> fun tb -> tb.StartNow()
                |> fun tb -> tb.WithSimpleSchedule(fun x -> x.WithIntervalInSeconds(5).RepeatForever() |> ignore)
                |> fun tb -> tb.Build()

            // Persist the job information to the database
            use db = new SqliteConnection("Data Source=feeds.db")
            do! db.OpenAsync()

            let insert = db.CreateCommand()
            insert.CommandText <- @"INSERT INTO feeds (FEED_KEY, FEED_GROUP, FEED_URL, FEED_CHANNEL) VALUES ($key, $group, $url, $channel)"
            insert.Parameters.AddWithValue("$key", feedKey) |> ignore
            insert.Parameters.AddWithValue("$group", feedChannel) |> ignore
            insert.Parameters.AddWithValue("$url", feed) |> ignore
            insert.Parameters.AddWithValue("$channel", feedChannel) |> ignore

            let! _ = insert.ExecuteNonQueryAsync()

            // Schedule the job
            let! _ = scheduler.ScheduleJob(job, trigger)

            let! _ = ctx.RespondAsync("Subscribed to feed!")
            ()
        }
        :> Task

    [<Command "unsubscribe"; Description "Unsubscribe from an RSS feed.">]
    member _.unsubscribe (ctx: CommandContext, feed: string, [<Optional; DefaultParameterValue(null: DiscordChannel)>] channel: DiscordChannel) =
        task {
            let feedKey = parseUrlToKey (Uri(feed))
            let feedChannel = (if isNull channel then ctx.Channel.Id else channel.Id) |> sprintf "%d"

            // Delete the job from the scheduler
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let! result = scheduler.DeleteJob(JobKey(feedKey, feedChannel))

            // Delete the job information from the database
            use db = new SqliteConnection("Data Source=feeds.db")
            do! db.OpenAsync()

            let insert = db.CreateCommand()
            insert.CommandText <- @"DELETE FROM feeds WHERE FEED_URL = $url AND FEED_CHANNEL = $channel)"
            insert.Parameters.AddWithValue("$url", feed) |> ignore
            insert.Parameters.AddWithValue("$channel", feedChannel) |> ignore

            let! _ = insert.ExecuteNonQueryAsync()

            let! _ =
                match result with
                | true -> ctx.RespondAsync("Unsubscribed from feed!")
                | false -> ctx.RespondAsync("Failed to unsubscribe from feed!")
            ()
        }
        :> Task