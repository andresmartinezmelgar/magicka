FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Arcana.Core/Arcana.Core.csproj src/Arcana.Core/
COPY src/Arcana.Server/Arcana.Server.csproj src/Arcana.Server/
RUN dotnet restore src/Arcana.Server/Arcana.Server.csproj
COPY src/Arcana.Core/ src/Arcana.Core/
COPY src/Arcana.Server/ src/Arcana.Server/
RUN dotnet publish src/Arcana.Server/Arcana.Server.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/logs && chown app:app /app/logs
USER app
ENV GAME_PORT=27015 DISCOVERY_PORT=27016 DOTNET_EnableDiagnostics=0 ARCANA_LOG_DIRECTORY=/app/logs
EXPOSE 27015/tcp 27016/udp
LABEL org.opencontainers.image.title="Arcana Arena Server" org.opencontainers.image.version="0.12.9"
ENTRYPOINT ["dotnet", "Arcana.Server.dll"]
