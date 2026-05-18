# syntax=docker/dockerfile:1.7

# Stage 1 — restore + publish on the .NET 10 SDK image. Copy csproj
# files first so `dotnet restore` lands in its own layer and is reused
# across iterations when only source files change.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Solution + Directory.Build.props (TargetFramework, analyzer config etc.)
# + per-project csprojs (restore layer cache).
COPY Briscola.sln Directory.Build.props ./
COPY tests/Directory.Build.props tests/
COPY src/Briscola.Domain/Briscola.Domain.csproj                                       src/Briscola.Domain/
COPY src/Briscola.Application/Briscola.Application.csproj                             src/Briscola.Application/
COPY src/Briscola.Infrastructure/Briscola.Infrastructure.csproj                       src/Briscola.Infrastructure/
COPY src/Briscola.Infrastructure.Sqlite.Migrations/Briscola.Infrastructure.Sqlite.Migrations.csproj       src/Briscola.Infrastructure.Sqlite.Migrations/
COPY src/Briscola.Infrastructure.Postgres.Migrations/Briscola.Infrastructure.Postgres.Migrations.csproj   src/Briscola.Infrastructure.Postgres.Migrations/
COPY src/Briscola.Api/Briscola.Api.csproj                                             src/Briscola.Api/
COPY tools/Briscola.StressTest/Briscola.StressTest.csproj                             tools/Briscola.StressTest/
COPY tests/Briscola.Domain.Tests/Briscola.Domain.Tests.csproj                         tests/Briscola.Domain.Tests/
COPY tests/Briscola.Application.Tests/Briscola.Application.Tests.csproj               tests/Briscola.Application.Tests/
COPY tests/Briscola.Api.IntegrationTests/Briscola.Api.IntegrationTests.csproj         tests/Briscola.Api.IntegrationTests/

# Restore the API project — its transitive graph pulls in Domain /
# Application / Infrastructure / Migrations, so restoring once is enough
# for the publish step. The test + stress projects are NOT in the publish
# graph (we don't ship the harness or the test runners in the image).
RUN dotnet restore src/Briscola.Api/Briscola.Api.csproj

# Source.
COPY src/ src/

# Publish to a self-contained directory. -p:PublishProfile=DefaultContainer
# would also work but pinning to a plain `dotnet publish` keeps the
# Dockerfile portable across IDEs.
RUN dotnet publish src/Briscola.Api/Briscola.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
    && rm -rf /app/publish/*.pdb

# Stage 2 — runtime. The chiseled-extra image is a slim distroless-style
# ASP.NET runtime that already ships curl (HEALTHCHECK) + ICU + tzdata,
# and runs as a non-root user (uid 1654) by default. No apt install needed.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

# Bind to all interfaces inside the container — the docker-compose
# service routes :8080 in.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_NOLOGO=1
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Briscola.Api.dll"]
