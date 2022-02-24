module Jobs

open Quartz
open System.Collections.Concurrent

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