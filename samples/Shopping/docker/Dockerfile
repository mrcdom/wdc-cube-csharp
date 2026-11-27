# The two Shopping web hosts, built from the repository root (the build context): target "server" is the REST API with
# the WebAssembly app, target "blazor" is Blazor Server.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-resolute AS build
WORKDIR /src
COPY . .
RUN dotnet restore samples/Shopping/Shopping.Server/Shopping.Server.csproj \
 && dotnet restore samples/Shopping/Shopping.Blazor/Shopping.Blazor.csproj

FROM build AS publish-server
RUN dotnet publish samples/Shopping/Shopping.Server/Shopping.Server.csproj -c Release -f net11.0 --no-restore -o /app

FROM build AS publish-blazor
RUN dotnet publish samples/Shopping/Shopping.Blazor/Shopping.Blazor.csproj -c Release -f net11.0 --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-resolute AS server
WORKDIR /app
COPY --from=publish-server /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Shopping.Server.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-resolute AS blazor
WORKDIR /app
COPY --from=publish-blazor /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Shopping.Blazor.dll"]
