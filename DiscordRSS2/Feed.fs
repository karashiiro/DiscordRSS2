module Feed

open DSharpPlus.CommandsNext
open DSharpPlus.CommandsNext.Attributes
open Microsoft.Extensions.DependencyInjection
open Quartz
open System.Collections.Concurrent
open System.Threading.Tasks

open Rss

type FeedState() =
    let entries = ConcurrentDictionary<string, Set<string>>()

    member _.Entries = entries

[<PersistJobDataAfterExecution>]
[<DisallowConcurrentExecution>]
type FeedJob(state0: FeedState) =
    let state = state0

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

                    let! feed = Rss.AsyncLoad(feedUrl)
                    for e in feed.Entries do
                        if not (feedSeen |> Set.contains e.Id) then
                            feedSeen <- feedSeen.Add e.Id
                            state.Entries.set_Item(feedKey, feedSeen)
                            printf "%s - %s\n" e.Title e.Link.Href
                with e ->
                    raise (JobExecutionException(e))
            }

type FeedModule() =
    inherit BaseCommandModule()

    [<Command "subscribe"; Description "Subscribe to an RSS feed.">]
    member _.subscribe (ctx: CommandContext, feed: string) =
        task {
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let job =
                JobBuilder.Create<FeedJob>()
                |> fun jb -> jb.WithIdentity("job1", "group1")
                |> fun jb -> jb.UsingJobData("feedUrl", feed)
                |> fun jb -> jb.Build()
            let trigger =
                TriggerBuilder.Create()
                |> fun tb -> tb.WithIdentity("trigger1", "group1")
                |> fun tb -> tb.StartNow()
                |> fun tb -> tb.WithSimpleSchedule(fun x -> x.WithIntervalInSeconds(5).RepeatForever() |> ignore)
                |> fun tb -> tb.Build()
            let! _ = scheduler.ScheduleJob(job, trigger)
            let! _ = ctx.RespondAsync("Subscribed to feed!")
            ()
        }
        :> Task

    [<Command "unsubscribe"; Description "Unsubscribe from an RSS feed.">]
    member _.unsubscribe (ctx: CommandContext) =
        task {
            let factory = ctx.Services.GetRequiredService<ISchedulerFactory>()
            let! scheduler = factory.GetScheduler()
            let! result = scheduler.DeleteJob(JobKey("job1", "group1"))
            let! _ =
                match result with
                | true -> ctx.RespondAsync("Unsubscribed from feed!")
                | false -> ctx.RespondAsync("Failed to unsubscribe from feed!")
            ()
        }
        :> Task