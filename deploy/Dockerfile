# Build from repository root: docker build -f deploy/Dockerfile -t dsmarketservice:latest .
# NuGet.Config includes ./local-packages for Manonero.Logger.Kafka 6.0.0-beta.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY . .
RUN dotnet restore src/DSMarketService/DSMarketService.csproj --configfile NuGet.Config
RUN dotnet publish src/DSMarketService/DSMarketService.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Ho_Chi_Minh \
    DOTNET_GCServer=1
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DSMarketService.dll"]
