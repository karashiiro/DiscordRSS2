open DSharpPlus
open DSharpPlus.CommandsNext
open DSharpPlus.CommandsNext.Attributes
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Quartz
open System
open System.Reflection
open System.Linq
open System.Threading.Tasks

open Jobs
open Rss

type SimpleModule() =
    inherit BaseCommandModule()

    [<Command "ping"; Description "ping pong">]
    member _.ping (ctx: CommandContext) =
        task {
            let! _ = ctx.RespondAsync("pong")
            ()
        }
        :> Task

let discord token =
    let config = DiscordConfiguration()
    config.Token <- token
    config.TokenType <- TokenType.Bot

    let client = new DiscordClient(config)

    let commandsConfig = CommandsNextConfiguration()
    commandsConfig.StringPrefixes <- ["~"]

    let commands = client.UseCommandsNext(commandsConfig)
    commands.RegisterCommands<SimpleModule>()

    client.ConnectAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

    client

let configureServices _ (services: IServiceCollection) =
    services
    |> fun sv -> sv.AddSingleton(discord (Environment.GetEnvironmentVariable("PRIMA_BOT_TOKEN")))
    |> fun sv -> sv.AddQuartz (fun q ->
        q.UseMicrosoftDependencyInjectionJobFactory()

        let seenEntries = RssEntries.empty |> RssEntries.serialize
        q.ScheduleJob<FeedJob>(
            (fun trigger ->
                trigger
                |> fun t -> t.WithIdentity("trigger1", "group1")
                |> fun t -> t.StartNow()
                |> fun t -> t.WithSimpleSchedule(fun x -> x.WithIntervalInSeconds(5).RepeatForever() |> ignore)
                |> ignore),
            (fun job ->
                job
                |> fun j -> j.WithIdentity("job1", "group1")
                |> fun j -> j.UsingJobData("seen", seenEntries)
                |> ignore)) |> ignore

        ())
    |> fun sv -> sv.AddQuartzHostedService (fun opts -> 
        opts.WaitForJobsToComplete <- true)
    |> ignore

let main = task {
    use host =
        Host.CreateDefaultBuilder()
        |> fun b -> b.ConfigureServices(configureServices)
        |> fun b -> b.Build()
    do! host.StartAsync()
    do! Task.Delay(TimeSpan.FromSeconds(120))
    ()
}

main
|> Async.AwaitTask
|> Async.RunSynchronously
