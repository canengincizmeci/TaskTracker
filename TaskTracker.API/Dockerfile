FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

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

RUN ConnectionStrings__PostgreSql="Host=localhost;Port=5432;Database=tasktracker;Username=tasktracker;Password=build-placeholder" \
    TokenOptions__SecurityKey="docker-build-placeholder-security-key-123456789" \
    PasswordRecovery__HmacSecret="docker-build-placeholder-hmac-secret-123456789" \
    dotnet ef migrations bundle \
        --project TaskTracker.Core/TaskTracker.Core.csproj \
        --startup-project TaskTracker.API/TaskTracker.API.csproj \
        --context TaskTrackerDbContext \
        --self-contained \
        --target-runtime linux-x64 \
        --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrations

RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=migration-build /app/efbundle ./efbundle

ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "TaskTracker.API.dll"]
