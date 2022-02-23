open DSharpPlus;
open DSharpPlus.CommandsNext;
open DSharpPlus.CommandsNext.Attributes;
open Quartz;
open Quartz.Impl;
open Quartz.Logging;
open System;
open System.Threading.Tasks;

open Jobs;
open LogProvider;
open Rss;

type SimpleModule() =
    inherit BaseCommandModule()

    [<Command "ping">]
    [<Description "ping pong">]
    member _.ping (ctx: CommandContext) =
        task {
            let! _ = ctx.RespondAsync("pong")
            ()
        }

let discord token =
    let config = DiscordConfiguration()
    config.Token <- token
    config.TokenType <- TokenType.Bot

    let client = new DiscordClient(config)

    let commandsConfig = CommandsNextConfiguration()
    commandsConfig.StringPrefixes <- ["~"]

    let commands = client.UseCommandsNext(commandsConfig)
    commands.RegisterCommands<SimpleModule>()

    client

let main = task {
    LogProvider.SetCurrentLogProvider(ConsoleLogProvider())

    let factory = StdSchedulerFactory()
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

    let discordClient = discord (Environment.GetEnvironmentVariable("PRIMA_BOT_TOKEN"))
    do! discordClient.ConnectAsync()

    do! Task.Delay(TimeSpan.FromSeconds(120))
    do! scheduler.Shutdown()
    ()
}

main.Wait()
