open DSharpPlus
open DSharpPlus.CommandsNext
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Quartz
open System
open System.Text.RegularExpressions
open System.Threading.Tasks

open Feed

let discord token (services: IServiceProvider) =
    let config = DiscordConfiguration()
    config.Token <- token
    config.TokenType <- TokenType.Bot
    config.LoggerFactory <- services.GetRequiredService<ILoggerFactory>()

    let client = new DiscordClient(config)

    let commandsConfig = CommandsNextConfiguration()
    commandsConfig.StringPrefixes <- ["~"]
    commandsConfig.Services <- services

    let commands = client.UseCommandsNext(commandsConfig)
    commands.RegisterCommands<FeedModule>()

    client.ConnectAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

    client

let initDb =
    task {
        use db = new SqliteConnection("Data Source=feeds.db")
        do! db.OpenAsync()

        let dbInit = db.CreateCommand()
        dbInit.CommandText <- @"CREATE TABLE feeds (
            ID           INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
            FEED_KEY     TEXT                              NOT NULL,
            FEED_GROUP   TEXT                              NOT NULL,
            FEED_URL     TEXT                              NOT NULL,
            FEED_CHANNEL TEXT                              NOT NULL
        )"

        try
            let! _ = dbInit.ExecuteNonQueryAsync()
            ()
        with :? SqliteException as e when Regex.IsMatch(e.Message, @"table \w* already exists") ->
            ()
    }

let configureServices _ (services: IServiceCollection) =
    initDb
    |> Async.AwaitTask
    |> Async.RunSynchronously
    
    services
    |> fun sv -> sv.AddSingleton<DiscordClient>(fun s -> discord (Environment.GetEnvironmentVariable("PRIMA_BOT_TOKEN")) s)
    |> fun sv -> sv.AddSingleton<FeedState>()
    |> fun sv -> sv.AddQuartz (fun q ->
        q.UseMicrosoftDependencyInjectionJobFactory()
        q.UsePersistentStore(fun opts ->
            // TODO: Fails to create tables in database
            opts.UseProperties <- true
            opts.UseMicrosoftSQLite("Data Source=jobs.db")
            opts.UseJsonSerializer()))
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