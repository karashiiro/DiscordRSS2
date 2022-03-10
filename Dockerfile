# Build stage
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /source
COPY ./ ./
RUN dotnet nuget add source https://www.myget.org/F/quartznet/api/v3/index.json
RUN dotnet restore
RUN dotnet publish -c release -o build --no-restore

# Run stage
FROM mcr.microsoft.com/dotnet/aspnet:6.0 as runtime
WORKDIR /app
COPY --from=build /source/build ./
ENTRYPOINT ["dotnet", "DiscordRSS2.dll"]