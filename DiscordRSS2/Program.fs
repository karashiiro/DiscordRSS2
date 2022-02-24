﻿open DSharpPlus
open DSharpPlus.CommandsNext
open DSharpPlus.CommandsNext.Attributes
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Quartz
open System
open System.Threading.Tasks

open Jobs

type SimpleModule() =
    inherit BaseCommandModule()

    [<Command "ping"; Description "ping pong">]
    member _.ping (ctx: CommandContext) =
        task {
            let! _ = ctx.RespondAsync("pong")
            ()
        }
        :> Task

let discord token (services: IServiceProvider) =
    let config = DiscordConfiguration()
    config.Token <- token
    config.TokenType <- TokenType.Bot
    config.LoggerFactory <- services.GetRequiredService<ILoggerFactory>()

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
    |> fun sv -> sv.AddSingleton<DiscordClient>(fun s -> discord (Environment.GetEnvironmentVariable("PRIMA_BOT_TOKEN")) s)
    |> fun sv -> sv.AddSingleton<FeedState>()
    |> fun sv -> sv.AddQuartz (fun q ->
        q.UseMicrosoftDependencyInjectionJobFactory()
        
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
                |> fun j -> j.UsingJobData("feedUrl", "https://www.reddit.com/.rss")
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

    // Force the Discord client to be initialized
    host.Services.GetRequiredService<DiscordClient>() |> ignore

    do! host.StartAsync()
    do! Task.Delay(-1)
    ()
}

main
|> Async.AwaitTask
|> Async.RunSynchronously