ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
FROM ${DOTNET_SDK_IMAGE} AS build

WORKDIR /src

COPY TaskTracker.API/TaskTracker.API.csproj TaskTracker.API/
COPY TaskTracker.Bussiness/TaskTracker.Bussiness.csproj TaskTracker.Bussiness/
COPY TaskTracker.Core/TaskTracker.Core.csproj TaskTracker.Core/
COPY TaskTracker.DataAccess/TaskTracker.DataAccess.csproj TaskTracker.DataAccess/
COPY TaskTracker.Entities/TaskTracker.Entities.csproj TaskTracker.Entities/

RUN dotnet restore TaskTracker.API/TaskTracker.API.csproj

COPY TaskTracker.API/ TaskTracker.API/
COPY TaskTracker.Bussiness/ TaskTracker.Bussiness/
COPY TaskTracker.Core/ TaskTracker.Core/
COPY TaskTracker.DataAccess/ TaskTracker.DataAccess/
COPY TaskTracker.Entities/ TaskTracker.Entities/

RUN dotnet publish TaskTracker.API/TaskTracker.API.csproj \
    -c Release \
    --no-restore \
    -o /app/publish


FROM build AS migration-build

RUN dotnet tool install --global dotnet-ef --version 10.0.8

ENV PATH="/root/.dotnet/tools:${PATH}"

RUN dotnet ef migrations bundle \
        --project TaskTracker.Core/TaskTracker.Core.csproj \
        --startup-project TaskTracker.API/TaskTracker.API.csproj \
        --context TaskTrackerDbContext \
        --self-contained \
        --target-runtime linux-x64 \
        --output /app/efbundle

FROM ${DOTNET_RUNTIME_IMAGE} AS migrations

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=migration-build /app/efbundle ./efbundle

ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/tasktracker-bundle
USER $APP_UID

ENTRYPOINT ["./efbundle"]

FROM ${DOTNET_RUNTIME_IMAGE} AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .
RUN install -d -m 0700 -o "$APP_UID" /app/App_Data/task-files

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "TaskTracker.API.dll"]
