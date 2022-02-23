module Jobs

open Quartz;

open Rss;

[<PersistJobDataAfterExecution>]
[<DisallowConcurrentExecution>]
type FeedJob() =
    interface IJob with
        member _.Execute context =
            task {
                try
                    let dataMap = context.JobDetail.JobDataMap
                    let data = dataMap.GetString("seen") |> RssEntries.Parse
                    let mutable seenEntries = data.Entries |> Seq.map (fun x -> x.Id) |> set

                    let! feed = Rss.AsyncLoad("https://www.reddit.com/.rss")
                    for e in feed.Entries do
                        if not (seenEntries |> Set.contains e.Id) then
                            seenEntries <- seenEntries.Add e.Id
                            printf "%s - %s\n" e.Title e.Link.Href

                    let newData = RssEntries.fromEntries(seenEntries |> Seq.map RssEntries.Entry |> Array.ofSeq) |> RssEntries.serialize
                    dataMap.Put("seen", newData)
                with e ->
                    raise (JobExecutionException(e))
            }