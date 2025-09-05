FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
ARG PUID=0
ARG PGID=0
ENV PUID=$PUID
ENV PGID=$PGID
RUN groupadd -g $PGID appgroup && useradd -u $PUID -g appgroup -s /bin/bash appuser

USER appuser
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Yamoh/Yamoh.csproj", "Yamoh/"]
RUN dotnet restore "Yamoh/Yamoh.csproj"
COPY src/Yamoh/. ./Yamoh/
COPY src/Custom.Build.props ./
COPY global.json ./
WORKDIR "/src/Yamoh"
RUN dotnet build "./Yamoh.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Yamoh.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Yamoh.dll"]
