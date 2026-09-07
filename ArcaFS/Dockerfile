# ArcaFS web container — SDK and runtime use the same 10.0-noble (glibc) image family.

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS base
WORKDIR /app
EXPOSE 8080
RUN mkdir -p /app/data/files /app/data/db /app/data/logs /app/data/thumbnails \
    && apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ArcaFS/ArcaFS.csproj", "ArcaFS/"]
COPY ["ArcaFS.Core/ArcaFS.Core.csproj", "ArcaFS.Core/"]
COPY ["ArcaFS.Data/ArcaFS.Data.csproj", "ArcaFS.Data/"]
COPY ["ArcaFS.Data.Sqlite/ArcaFS.Data.Sqlite.csproj", "ArcaFS.Data.Sqlite/"]
COPY ["ArcaFS.Data.Sql/ArcaFS.Data.Sql.csproj", "ArcaFS.Data.Sql/"]
COPY ["ArcaFS.Data.Npgsql/ArcaFS.Data.Npgsql.csproj", "ArcaFS.Data.Npgsql/"]
COPY ["ArcaFS.Data.MySql/ArcaFS.Data.MySql.csproj", "ArcaFS.Data.MySql/"]
COPY ["ArcaFS.Models/ArcaFS.Models.csproj", "ArcaFS.Models/"]
COPY ["ArcaFS.Services/ArcaFS.Services.csproj", "ArcaFS.Services/"]
RUN dotnet restore "./ArcaFS/ArcaFS.csproj"
COPY . .
WORKDIR "/src/ArcaFS"
# Restore client-side libraries (wwwroot/lib) since they are not always in source control.
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli --version 2.1.113 && \
    export PATH="${PATH}:/root/.dotnet/tools" && \
    libman restore --verbose
RUN dotnet build "./ArcaFS.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ArcaFS.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN cp -r /src/ArcaFS/wwwroot/lib /app/publish/wwwroot/ || true
RUN if [ -f /app/publish/ArcaFS.styles.css ]; then cp /app/publish/ArcaFS.styles.css /app/publish/wwwroot/; fi

FROM base AS final
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ArcaFS.dll"]
