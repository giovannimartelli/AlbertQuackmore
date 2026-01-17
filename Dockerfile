
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
USER $APP_UID
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy csproj files first to leverage layer caching
COPY src/AlbertQuackmore.TelegramBot/AlbertQuackmore.TelegramBot.csproj src/AlbertQuackmore.TelegramBot/

RUN dotnet restore "src/AlbertQuackmore.TelegramBot/AlbertQuackmore.TelegramBot.csproj"
COPY src/. src/.
RUN dotnet build src/AlbertQuackmore.TelegramBot/AlbertQuackmore.TelegramBot.csproj -c Release -o /app/build 

FROM build AS publish
RUN dotnet publish src/AlbertQuackmore.TelegramBot/AlbertQuackmore.TelegramBot.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","AlbertQuackmore.TelegramBot.dll"]
