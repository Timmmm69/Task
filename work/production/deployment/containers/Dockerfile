# syntax=docker/dockerfile:1.7@sha256:b5f3b260a9678e1d83d2fce86eeddf79420b79147eaba2a25986f47133d73720

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.403-noble-amd64@sha256:9c7b6b263d9d4c2e4718e16dae6d75cd8f4b5ccf206f1e7d48a98eb429981b03
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-amd64@sha256:43cbcf11fd6f88c0375c9c0f7fe59af619abeb323befe5f57dc1502b5a05d8c3
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.14-noble-amd64@sha256:de1261f1b137a580e6620fa3f20323efa7bb131a2aeb103cab95077d3445122d

FROM ${SDK_IMAGE} AS build
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
WORKDIR /src
COPY deployment/containers/NuGet.Config ./NuGet.Config

COPY src/Task.Domain/Task.Domain.csproj src/Task.Domain/packages.lock.json src/Task.Domain/
COPY src/Task.Application/Task.Application.csproj src/Task.Application/packages.lock.json src/Task.Application/
COPY src/Task.Infrastructure/Task.Infrastructure.csproj src/Task.Infrastructure/packages.lock.json src/Task.Infrastructure/
COPY src/Task.Api/Task.Api.csproj src/Task.Api/packages.lock.json src/Task.Api/
COPY src/Task.Worker/Task.Worker.csproj src/Task.Worker/packages.lock.json src/Task.Worker/
COPY src/Task.BackupAgent/Task.BackupAgent.csproj src/Task.BackupAgent/packages.lock.json src/Task.BackupAgent/
COPY src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj src/Task.DatabaseMigrator/packages.lock.json src/Task.DatabaseMigrator/
COPY verification/container-task-store/Task.ContainerValidation.csproj verification/container-task-store/packages.lock.json verification/container-task-store/

RUN dotnet restore src/Task.Api/Task.Api.csproj --runtime linux-x64 --locked-mode \
    && dotnet restore src/Task.Worker/Task.Worker.csproj --runtime linux-x64 --locked-mode \
    && dotnet restore src/Task.BackupAgent/Task.BackupAgent.csproj --runtime linux-x64 --locked-mode \
    && dotnet restore src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj --runtime linux-x64 --locked-mode \
    && dotnet restore verification/container-task-store/Task.ContainerValidation.csproj --runtime linux-x64 --locked-mode

COPY src/ src/
COPY verification/container-task-store/ verification/container-task-store/

RUN dotnet publish src/Task.Api/Task.Api.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-api -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} -p:ContinuousIntegrationBuild=true -p:Deterministic=true \
    && dotnet publish src/Task.Worker/Task.Worker.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-worker -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} -p:ContinuousIntegrationBuild=true -p:Deterministic=true \
    && dotnet publish src/Task.BackupAgent/Task.BackupAgent.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-backup-agent -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} -p:ContinuousIntegrationBuild=true -p:Deterministic=true \
    && dotnet publish src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-database-migrator -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} -p:ContinuousIntegrationBuild=true -p:Deterministic=true \
    && dotnet publish verification/container-task-store/Task.ContainerValidation.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-container-validation -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} -p:ContinuousIntegrationBuild=true -p:Deterministic=true

FROM ${ASPNET_IMAGE} AS task-api-runtime
USER root
RUN sed -i 's|http://archive\.ubuntu\.com/ubuntu|https://archive.ubuntu.com/ubuntu|g; s|http://security\.ubuntu\.com/ubuntu|https://security.ubuntu.com/ubuntu|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends \
        libssl3t64=3.0.13-0ubuntu3.16 \
        openssl=3.0.13-0ubuntu3.16 \
        libgssapi-krb5-2=1.20.1-6ubuntu2.10 \
        libkrb5-3=1.20.1-6ubuntu2.10 \
        libk5crypto3=1.20.1-6ubuntu2.10 \
        libkrb5support0=1.20.1-6ubuntu2.10 \
        libkeyutils1=1.6.3-3build1 \
    && rm -rf /var/lib/apt/lists/* /var/cache/apt/* /var/log/apt/* /var/log/dpkg.log \
        /var/lib/apt/extended_states /var/cache/ldconfig/aux-cache \
        /var/lib/dpkg/status-old /var/lib/dpkg/lock /var/lib/dpkg/triggers/Lock

FROM task-api-runtime AS task-api
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="Task.Api" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-api/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Task.Api.dll"]

FROM ${RUNTIME_IMAGE} AS task-base-runtime
USER root
RUN sed -i 's|http://archive\.ubuntu\.com/ubuntu|https://archive.ubuntu.com/ubuntu|g; s|http://security\.ubuntu\.com/ubuntu|https://security.ubuntu.com/ubuntu|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends \
        libssl3t64=3.0.13-0ubuntu3.16 \
        openssl=3.0.13-0ubuntu3.16 \
    && rm -rf /var/lib/apt/lists/* /var/cache/apt/* /var/log/apt/* /var/log/dpkg.log \
        /var/lib/apt/extended_states /var/cache/ldconfig/aux-cache \
        /var/lib/dpkg/status-old /var/lib/dpkg/lock /var/lib/dpkg/triggers/Lock

FROM task-base-runtime AS task-worker
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="Task.Worker" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-worker/ ./
USER app
ENTRYPOINT ["dotnet", "Task.Worker.dll"]

FROM task-base-runtime AS task-backup-agent
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="Task.BackupAgent" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-backup-agent/ ./
USER app
ENTRYPOINT ["dotnet", "Task.BackupAgent.dll"]

FROM task-base-runtime AS task-database-migrator-runtime
USER root
RUN sed -i 's|http://archive\.ubuntu\.com/ubuntu|https://archive.ubuntu.com/ubuntu|g; s|http://security\.ubuntu\.com/ubuntu|https://security.ubuntu.com/ubuntu|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends \
        libgssapi-krb5-2=1.20.1-6ubuntu2.10 \
        libkrb5-3=1.20.1-6ubuntu2.10 \
        libk5crypto3=1.20.1-6ubuntu2.10 \
        libkrb5support0=1.20.1-6ubuntu2.10 \
        libkeyutils1=1.6.3-3build1 \
    && rm -rf /var/lib/apt/lists/* /var/cache/apt/* /var/log/apt/* /var/log/dpkg.log \
        /var/lib/apt/extended_states /var/cache/ldconfig/aux-cache \
        /var/lib/dpkg/status-old /var/lib/dpkg/lock /var/lib/dpkg/triggers/Lock

FROM task-database-migrator-runtime AS task-database-migrator
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="Task.DatabaseMigrator" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-database-migrator/ ./
USER app
ENTRYPOINT ["dotnet", "Task.DatabaseMigrator.dll"]

# Validation-only target. It is not a deployable service image.
FROM task-base-runtime AS task-container-validation
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-container-validation/ ./
USER app
ENTRYPOINT ["dotnet", "Task.ContainerValidation.dll"]
