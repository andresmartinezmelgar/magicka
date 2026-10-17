FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props VERSION NuGet.Config ./
COPY src/Arcana.Core/Arcana.Core.csproj src/Arcana.Core/
COPY src/Arcana.Server/Arcana.Server.csproj src/Arcana.Server/
RUN dotnet restore src/Arcana.Server/Arcana.Server.csproj
COPY src/Arcana.Core/ src/Arcana.Core/
COPY src/Arcana.Server/ src/Arcana.Server/
COPY scripts/Apply-WindowsUpdate.ps1 scripts/Apply-WindowsUpdate.ps1
RUN dotnet publish src/Arcana.Server/Arcana.Server.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/logs /app/data/rewards && chown -R app:app /app/logs /app/data
USER app
ENV GAME_PORT=27015 DISCOVERY_PORT=27016 DOTNET_EnableDiagnostics=0 ARCANA_LOG_DIRECTORY=/app/logs ARCANA_REWARD_OUTBOX=/app/data/rewards
EXPOSE 27015/tcp 27016/udp
ARG ARCANA_VERSION=0.21.0
LABEL org.opencontainers.image.title="Arcana Arena Server" org.opencontainers.image.version="$ARCANA_VERSION"
ENTRYPOINT ["dotnet", "Arcana.Server.dll"]
