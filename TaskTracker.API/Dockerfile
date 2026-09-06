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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "TaskTracker.API.dll"]
