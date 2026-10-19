FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG HTTP_PROXY=http://proxy.fpts.com.vn:8080
ARG HTTPS_PROXY=http://proxy.fpts.com.vn:8080
ARG NO_PROXY=localhost,127.0.0.1,.fpts.com.vn

ENV HTTP_PROXY=${HTTP_PROXY} \
    HTTPS_PROXY=${HTTPS_PROXY} \
    NO_PROXY=${NO_PROXY} \
    http_proxy=${HTTP_PROXY} \
    https_proxy=${HTTPS_PROXY} \
    no_proxy=${NO_PROXY}

WORKDIR /repo

# Restore layer: copy only dependency manifests + local Manonero nupkg first.
COPY NuGet.Config ./NuGet.Config
COPY local-packages/ ./local-packages/
COPY src/DSMarketService.Contracts/DSMarketService.Contracts.csproj src/DSMarketService.Contracts/
COPY src/DSMarketService.Infrastructure/DSMarketService.Infrastructure.csproj src/DSMarketService.Infrastructure/
COPY src/DSMarketService/DSMarketService.csproj src/DSMarketService/
RUN dotnet restore src/DSMarketService/DSMarketService.csproj --configfile NuGet.Config

COPY . .
RUN dotnet publish src/DSMarketService/DSMarketService.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ASP.NET Core web app requires aspnet runtime, NOT the worker-only runtime image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Ho_Chi_Minh \
    DOTNET_GCServer=1

COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DSMarketService.dll"]
