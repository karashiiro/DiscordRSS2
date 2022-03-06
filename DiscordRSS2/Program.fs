open DSharpPlus
open DSharpPlus.CommandsNext
open Feed
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Quartz
open System
open System.IO
open System.Reflection
open System.Threading.Tasks

let loade name =
    task {
        use s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        use sr = new StreamReader(s)
        return! sr.ReadToEndAsync()
    }

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

let feedsDbPath = "feeds.db"
let jobsDbPath = "jobs.db"
let initDb =
    task {
        use db1 = new SqliteConnection(sprintf "Data Source=%s" feedsDbPath)
        do! db1.OpenAsync()
        let dbInit1 = db1.CreateCommand()
        let! feedsScript = loade "DiscordRSS2.feeds.sql"
        dbInit1.CommandText <- feedsScript
        let! _ = dbInit1.ExecuteNonQueryAsync()

        use db2 = new SqliteConnection(sprintf "Data Source=%s" jobsDbPath)
        do! db2.OpenAsync()
        let dbInit2 = db2.CreateCommand()
        let! quartzScript = loade "DiscordRSS2.Quartz.tables_sqlite.sql"
        dbInit2.CommandText <- quartzScript
        let! _ = dbInit2.ExecuteNonQueryAsync()

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
            opts.UseProperties <- true
            opts.UseMicrosoftSQLite(sprintf "Data Source=%s" jobsDbPath)
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