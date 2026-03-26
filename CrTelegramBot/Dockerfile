FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CrTelegramBot/CrTelegramBot.csproj CrTelegramBot/
RUN dotnet restore CrTelegramBot/CrTelegramBot.csproj

COPY . .
WORKDIR /src/CrTelegramBot
RUN dotnet publish CrTelegramBot.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CrTelegramBot.dll"]