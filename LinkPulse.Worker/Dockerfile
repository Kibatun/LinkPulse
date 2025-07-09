FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LinkPulse.Worker/LinkPulse.Worker.csproj", "LinkPulse.Worker/"]
RUN dotnet restore "LinkPulse.Worker/LinkPulse.Worker.csproj"
COPY . .
WORKDIR "/src/LinkPulse.Worker"
RUN dotnet build "./LinkPulse.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LinkPulse.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LinkPulse.Worker.dll"]
