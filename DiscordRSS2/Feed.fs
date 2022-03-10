module Feed

open DSharpPlus
open DSharpPlus.CommandsNext
open DSharpPlus.CommandsNext.Attributes
open DSharpPlus.Entities
open FeedState
open Microsoft.Data.Sqlite
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Quartz
open Rss
open System
open System.Net
open System.Runtime.InteropServices
open System.Threading.Tasks

type ChannelId =
    | NumberCId of uint64
    | StringCId of string

type Url =
    | StringUrl of string
    | ParsedUrl of Uri

let private key channel url =
    let channel' =
        match channel with
        | NumberCId n -> n.ToString()
        | StringCId s -> s
    let url' =
        match url with
        | StringUrl s -> Uri(s)
        | ParsedUrl u -> u
    sprintf "%s-%s%s" channel' url'.Host url'.PathAndQuery

[<PersistJobDataAfterExecution>]
[<DisallowConcurrentExecution>]
type FeedJob(state0: FeedState, client0: DiscordClient, logger0: ILogger<FeedJob>) =
    let state = state0
    let client = client0
    let logger = logger0

    let embed (entry: Rss.Entry) =
        DiscordEmbedBuilder()
        |> fun eb -> eb.WithTitle (if entry.Title.Length < 256 then entry.Title else (sprintf "%s..." entry.Title[..252]))
        |> fun eb -> eb.WithUrl entry.Link.Href
        |> fun eb ->
            match entry.Thumbnail with
            | Some t -> eb.WithThumbnail t.Url
            | None -> eb
        |> fun eb -> eb.WithDescription(sprintf "Posted on <t:%d:f> by [%s](%s)" (entry.Published.ToUnixTimeSeconds()) entry.Author.Name entry.Author.Uri)
        |> fun eb -> eb.Build()

    let rec execute (f: (DiscordEmbed -> Task)) (s: Set<string>) (el: Rss.Entry list) =
        match el with
        | e :: tail ->
            if not (s |> Set.contains e.Id) then
                let s' = execute f (s.Add e.Id) tail
                f (embed e) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
                s'
            else
                execute f (s.Add e.Id) tail
        | _ -> s

    let spfidentify =
         sprintf "for %s in channel %d"

    interface IJob with
        member _.Execute context =
            task {
                try
                    let dataMap = context.JobDetail.JobDataMap
                    let feedUrl = dataMap.GetString("feedUrl")
                    let feedChannelId = dataMap.GetString("feedChannel") |> uint64
                    let! feedChannel = client.GetChannelAsync(feedChannelId)

                    logger.LogInformation("Starting RSS update " + spfidentify feedUrl feedChannelId)

                    let feedKey = key (NumberCId feedChannelId) (StringUrl feedUrl)

                    let feedSeen =
                        match state.Retrieve(feedKey) with
                        | Ok fs -> fs
                        | Error err ->
                            logger.LogWarning(sprintf "Failed to retrieve feed state %s (this is normal for new jobs): %s" (spfidentify feedUrl feedChannelId) err)
                            logger.LogInformation("Creating new feed state " + spfidentify feedUrl feedChannelId)
                            match state.Create(feedKey) with
                            | Ok fs -> fs
                            | Error reason -> failwith reason
                    let feedCountStart = feedSeen.Count

                    let publish (e: DiscordEmbed) =
                        task {
                            let! _ = feedChannel.SendMessageAsync(e)
                            do! Task.Delay(200)
                        }
                        :> Task

                    try
                        let! feed = Rss.AsyncLoad(feedUrl)
                        let entries = feed.Entries |> Seq.rev |> List.ofSeq
                        let feedCountEnd =
                            match state.Update(feedKey, (execute publish feedSeen entries)) with
                            | Ok feedSeen' -> feedSeen'.Count
                            | Error reason -> failwith reason
                        logger.LogInformation(sprintf "Completed RSS update %s - found %d new entries" (spfidentify feedUrl feedChannelId) (feedCountEnd - feedCountStart))
                    with :?WebException as ex ->
                        logger.LogWarning(sprintf "Failed to complete web request %s (%s): %s" (spfidentify feedUrl feedChannelId) (ex.Status.ToString()) ex.Message)
                with ex ->
                    raise (JobExecutionException(ex))
            }

type FeedModule(config0: IConfiguration) =
    inherit BaseCommandModule()

    let config = config0

    [<Command "subscribe"; Description "Subscribe to an RSS feed.">]
    member _.subscribe (ctx: CommandContext, feed: string, [<Optional; DefaultParameterValue(null: DiscordChannel)>] channel: DiscordChannel) =
        task {
            let feedKey = key (NumberCId channel.Id) (StringUrl feed)
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
            use db = new SqliteConnection(config["Database:Feeds"])
            do! db.OpenAsync()

            let insert = db.CreateCommand()
            insert.CommandText <- @"INSERT INTO feeds (feed_key, feed_group, feed_url, feed_channel) VALUES ($key, $group, $url, $channel)"
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
            let feedKey = key (NumberCId channel.Id) (StringUrl feed)
            let feedChannel = (if isNull channel then ctx.Channel.Id else channel.Id) |> sprintf "%d"

            // Delete the job from the scheduler
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let! result = scheduler.DeleteJob(JobKey(feedKey, feedChannel))

            // Delete the job information from the database
            use db = new SqliteConnection(config["Database:Feeds"])
            do! db.OpenAsync()

            let insert = db.CreateCommand()
            insert.CommandText <- @"DELETE FROM feeds WHERE feed_url = $url AND feed_channel = $channel)"
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