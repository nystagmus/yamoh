FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Yamoh/Yamoh.csproj", "Yamoh/"]
RUN dotnet restore "Yamoh/Yamoh.csproj"
COPY src/Yamoh/. ./Yamoh/
COPY src/Custom.Build.props ./
COPY src/global.json ./
WORKDIR "/src/Yamoh"
RUN dotnet build "./Yamoh.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Yamoh.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Yamoh.dll"]
