# DiscordRSS2
A simple bot that pumps RSS feeds into channels.

## Installation
Make sure you have a bot token for the Discord API. Set the environment variable `DISCORDRSS_BOT_TOKEN` to your bot token. You can then either build the application yourself or use the Docker configurations in the repository.

### Docker
With Docker installed on your computer, create a folder called `data` in a clone of the repository and run `docker-compose up -d` to start the bot. Alternatively, build and run the image using the provided Dockerfile. The image expects a volume to be mounted to its `data` path, for saving its databases.

### Building
Publish the application in release mode from Visual Studio. You'll need to add the Quartz.NET [prerelease](https://www.myget.org/F/quartznet/api/v3/index.json) repository to your Nuget sources. Make sure to create a `data` folder for the bot to use.

## Usage
Subscribe a channel to a feed: `~subscribe <channel> <RSS URL>`

Unsubscribe a channel from a feed: `~unsubscribe <channel> <RSS URL>`
