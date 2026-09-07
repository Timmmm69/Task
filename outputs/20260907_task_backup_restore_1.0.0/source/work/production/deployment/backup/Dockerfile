ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.11-noble-amd64@sha256:3e4906ba425366b848fc1899c4414cbc4d1c70f423a4e5a0d5c39c226e134f21
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400-noble-amd64@sha256:5657c5f725f2e8923f31b2eb9d743662f2e0be50a2bee41de685fc9f12ae68ef
FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY deployment/containers/NuGet.Config ./NuGet.Config
COPY src/ src/
RUN dotnet restore src/Task.BackupAgent/Task.BackupAgent.csproj -r linux-x64 --locked-mode \
    && dotnet publish src/Task.BackupAgent/Task.BackupAgent.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /publish

FROM ${RUNTIME_IMAGE}
USER root
RUN sed -i 's/ noble-backports//g; s|http://|https://|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends postgresql-16 pgbackrest python3 python3-cryptography ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupmod -g 1001 postgres && usermod -u 1001 -g 1001 postgres \
    && mkdir -p /app /opt/task-backup /var/lib/postgresql/data /var/lib/task-backup /backup/local /backup/offhost /restore /run/task-backup /run/postgresql \
    && chown -R postgres:postgres /var/lib/postgresql /var/lib/task-backup /backup /restore /run/task-backup /run/postgresql
RUN chown -R postgres:postgres /etc/postgresql && pg_dropcluster --stop 16 main \
    && rm -f /etc/ssl/private/ssl-cert-snakeoil.key /etc/ssl/certs/ssl-cert-snakeoil.pem
COPY --from=build /publish/ /app/
COPY deployment/backup/ /opt/task-backup/
RUN chmod 755 /opt/task-backup/runner.py /opt/task-backup/entrypoint.py
ENV PATH=/usr/lib/postgresql/16/bin:$PATH
USER postgres
ENTRYPOINT ["/opt/task-backup/entrypoint.py"]
CMD ["agent"]
