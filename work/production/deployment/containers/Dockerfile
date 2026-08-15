# syntax=docker/dockerfile:1.7

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400-noble-amd64@sha256:5657c5f725f2e8923f31b2eb9d743662f2e0be50a2bee41de685fc9f12ae68ef
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.11-noble-amd64@sha256:282c2e90dd35c6a720b744f4848d3dce9de4bfb404011270cc8ee63f07e56c36
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.11-noble-amd64@sha256:3e4906ba425366b848fc1899c4414cbc4d1c70f423a4e5a0d5c39c226e134f21

FROM ${SDK_IMAGE} AS build
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
WORKDIR /src

COPY src/Task.Domain/Task.Domain.csproj src/Task.Domain/
COPY src/Task.Application/Task.Application.csproj src/Task.Application/
COPY src/Task.Infrastructure/Task.Infrastructure.csproj src/Task.Infrastructure/
COPY src/Task.Api/Task.Api.csproj src/Task.Api/
COPY src/Task.Worker/Task.Worker.csproj src/Task.Worker/
COPY src/Task.BackupAgent/Task.BackupAgent.csproj src/Task.BackupAgent/
COPY src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj src/Task.DatabaseMigrator/
COPY verification/container-task-store/Task.ContainerValidation.csproj verification/container-task-store/

RUN dotnet restore src/Task.Api/Task.Api.csproj --runtime linux-x64 \
    && dotnet restore src/Task.Worker/Task.Worker.csproj --runtime linux-x64 \
    && dotnet restore src/Task.BackupAgent/Task.BackupAgent.csproj --runtime linux-x64 \
    && dotnet restore src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj --runtime linux-x64 \
    && dotnet restore verification/container-task-store/Task.ContainerValidation.csproj --runtime linux-x64

COPY src/ src/
COPY verification/container-task-store/ verification/container-task-store/

RUN dotnet publish src/Task.Api/Task.Api.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-api -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} \
    && dotnet publish src/Task.Worker/Task.Worker.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-worker -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} \
    && dotnet publish src/Task.BackupAgent/Task.BackupAgent.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-backup-agent -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} \
    && dotnet publish src/Task.DatabaseMigrator/Task.DatabaseMigrator.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-database-migrator -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA} \
    && dotnet publish verification/container-task-store/Task.ContainerValidation.csproj -c Release --no-restore --runtime linux-x64 --self-contained false -o /publish/task-container-validation -p:Version=${VERSION} -p:SourceRevisionId=${GIT_SHA}

FROM ${ASPNET_IMAGE} AS task-api
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

FROM ${RUNTIME_IMAGE} AS task-worker
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="Task.Worker" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-worker/ ./
USER app
ENTRYPOINT ["dotnet", "Task.Worker.dll"]

FROM ${RUNTIME_IMAGE} AS task-backup-agent
ARG VERSION=0.0.0-local
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="Task.BackupAgent" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}"
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-backup-agent/ ./
USER app
ENTRYPOINT ["dotnet", "Task.BackupAgent.dll"]

FROM ${RUNTIME_IMAGE} AS task-database-migrator
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
FROM ${RUNTIME_IMAGE} AS task-container-validation
WORKDIR /app
COPY --from=build --chown=app:app /publish/task-container-validation/ ./
USER app
ENTRYPOINT ["dotnet", "Task.ContainerValidation.dll"]
