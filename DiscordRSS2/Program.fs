open Quartz;
open Quartz.Impl;
open Quartz.Logging;
open System;
open System.Threading.Tasks;

open Jobs;
open LogProvider;
open Rss;

let main = task {
    LogProvider.SetCurrentLogProvider(new ConsoleLogProvider())

    let factory = new StdSchedulerFactory()
    let! scheduler = factory.GetScheduler()

    do! scheduler.Start()

    let seenEntries = RssEntries.empty |> RssEntries.serialize
    let job =
        JobBuilder.Create<FeedJob>()
        |> fun jb -> jb.WithIdentity("job1", "group1")
        |> fun jb -> jb.UsingJobData("seen", seenEntries)
        |> fun jb -> jb.Build()

    let trigger =
        TriggerBuilder.Create()
        |> fun tb -> tb.WithIdentity("trigger1", "group1")
        |> fun tb -> tb.StartNow()
        |> fun tb -> tb.WithSimpleSchedule(fun x -> x.WithIntervalInSeconds(5).RepeatForever() |> ignore)
        |> fun tb -> tb.Build()

    let! _ = scheduler.ScheduleJob(job, trigger)

    do! Task.Delay(TimeSpan.FromSeconds(60))
    do! scheduler.Shutdown()
    ()
}

main.Wait()
