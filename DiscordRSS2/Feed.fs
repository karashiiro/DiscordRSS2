module Feed

open DSharpPlus
open DSharpPlus.CommandsNext
open DSharpPlus.CommandsNext.Attributes
open DSharpPlus.Entities
open Microsoft.Extensions.DependencyInjection
open Quartz
open System
open System.Collections.Concurrent
open System.Threading.Tasks

open Rss
open System.Runtime.InteropServices

type FeedState() =
    let entries = ConcurrentDictionary<string, Set<string>>()

    member _.Entries = entries

[<PersistJobDataAfterExecution>]
[<DisallowConcurrentExecution>]
type FeedJob(state0: FeedState, client0: DiscordClient) =
    let state = state0
    let client = client0

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

                    let! feed = Rss.AsyncLoad(feedUrl)
                    for e in feed.Entries |> Seq.rev do
                        if not (feedSeen |> Set.contains e.Id) then
                            feedSeen <- feedSeen.Add e.Id
                            state.Entries.set_Item(feedKey, feedSeen)
                            let! _ = feedChannel.SendMessageAsync(embed e)
                            do! Task.Delay(200)
                            ()
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
            let feedGroup = (if channel = null then ctx.Channel.Id else channel.Id) |> sprintf "%d"
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let job =
                JobBuilder.Create<FeedJob>()
                |> fun jb -> jb.WithIdentity(feedKey, feedGroup)
                |> fun jb -> jb.UsingJobData("feedUrl", feed)
                |> fun jb -> jb.UsingJobData("feedChannel", feedGroup)
                |> fun jb -> jb.Build()
            let trigger =
                TriggerBuilder.Create()
                |> fun tb -> tb.WithIdentity("trigger__" + feedKey, feedGroup)
                |> fun tb -> tb.StartNow()
                |> fun tb -> tb.WithSimpleSchedule(fun x -> x.WithIntervalInSeconds(5).RepeatForever() |> ignore)
                |> fun tb -> tb.Build()
            let! _ = scheduler.ScheduleJob(job, trigger)
            let! _ = ctx.RespondAsync("Subscribed to feed!")
            ()
        }
        :> Task

    [<Command "unsubscribe"; Description "Unsubscribe from an RSS feed.">]
    member _.unsubscribe (ctx: CommandContext, feed: string, [<Optional; DefaultParameterValue(null: DiscordChannel)>] channel: DiscordChannel) =
        task {
            let feedKey = parseUrlToKey (Uri(feed))
            let feedGroup = (if channel = null then ctx.Channel.Id else channel.Id) |> sprintf "%d"
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let! result = scheduler.DeleteJob(JobKey(feedKey, feedGroup))
            let! _ =
                match result with
                | true -> ctx.RespondAsync("Unsubscribed from feed!")
                | false -> ctx.RespondAsync("Failed to unsubscribe from feed!")
            ()
        }
        :> Task