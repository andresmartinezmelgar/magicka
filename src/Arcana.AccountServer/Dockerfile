FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/Arcana.AccountServer/Arcana.AccountServer.csproj -c Release -o /app/publish --nologo
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ACCOUNT_PORT=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Arcana.AccountServer.dll"]
