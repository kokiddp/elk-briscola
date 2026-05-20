# syntax=docker/dockerfile:1.7

# Stage 1 — restore + publish on the .NET 10 SDK image. Copy csproj
# files first so `dotnet restore` lands in its own layer and is reused
# across iterations when only source files change.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:dc8430e6024d454edadad1e160e1973be3cabbb7125998ef190d9e5c6adf7dbb AS build
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

# Stage 2 — runtime. We pull curl from the SDK image (where apt resolves
# cleanly) and copy the binary + transitive .so deps into the chiseled
# runtime. The chiseled-extra image otherwise has no shell, package
# manager, or curl, so the HEALTHCHECK couldn't probe /healthz.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:dc8430e6024d454edadad1e160e1973be3cabbb7125998ef190d9e5c6adf7dbb AS curl-stage
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
# Stage out only what curl depends on (resolved from `ldd` at SDK build
# time). The chiseled runtime is Ubuntu noble like the SDK, so libc /
# libssl / nss layouts match.
RUN mkdir -p /opt/curl /opt/curl/usr/bin /opt/curl/lib /opt/curl/lib64 \
 && cp /usr/bin/curl /opt/curl/usr/bin/ \
 && cp -L $(ldd /usr/bin/curl | awk '/=> \//{print $3}') /opt/curl/lib/ \
 && cp -L /lib64/ld-linux-x86-64.so.2 /opt/curl/lib64/

# Build-time smoke check: invoke the staged curl via the dynamic linker
# against ONLY the staged libs. If any transitive .so is missing the
# `ldd` resolution would emit one of them blank or the loader fails
# below — either way the build halts, instead of producing an image
# whose HEALTHCHECK fails at deploy time. Mirrors what the runtime
# image's HEALTHCHECK does, with the same restricted library search.
RUN /opt/curl/lib64/ld-linux-x86-64.so.2 --library-path /opt/curl/lib \
        /opt/curl/usr/bin/curl --version

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:f923ccdc2dcd2da06c3e8dae929b9fef5b9041612c95784d79c371a6a2c25b04 AS runtime
WORKDIR /app

# Drop curl + its dependencies into the chiseled rootfs.
COPY --from=curl-stage /opt/curl/usr/bin/curl /usr/bin/curl
COPY --from=curl-stage /opt/curl/lib/ /lib/x86_64-linux-gnu/
COPY --from=curl-stage /opt/curl/lib64/ld-linux-x86-64.so.2 /lib64/ld-linux-x86-64.so.2

COPY --from=build /app/publish ./

# Bind to all interfaces inside the container — the docker-compose
# service routes :8080 in.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    DOTNET_NOLOGO=1
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
    CMD ["curl", "-fsS", "http://localhost:8080/healthz"]

ENTRYPOINT ["dotnet", "Briscola.Api.dll"]
