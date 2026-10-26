ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.14-noble-amd64@sha256:de1261f1b137a580e6620fa3f20323efa7bb131a2aeb103cab95077d3445122d
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.403-noble-amd64@sha256:9c7b6b263d9d4c2e4718e16dae6d75cd8f4b5ccf206f1e7d48a98eb429981b03
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
