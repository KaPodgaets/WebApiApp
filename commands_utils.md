# WebApiApp

Useful shell commands for local development and Docker usage.

## Docker Compose

Start with the default production-oriented compose file:

```powershell
docker compose up
```

Start in detached mode:

```powershell
docker compose up -d
```

Start and rebuild the image first:

```powershell
docker compose up --build
```

Start detached and rebuild the image first:

```powershell
docker compose up -d --build
```

Stop and remove the running compose stack:

```powershell
docker compose down
```

Stop, remove containers, and remove volumes:

```powershell
docker compose down -v
```

View container logs:

```powershell
docker compose logs
```

Follow live logs:

```powershell
docker compose logs -f
```

## Development Compose

Start with the development compose file:

```powershell
docker compose -f docker-compose.dev.yml up
```

Start development compose in detached mode:

```powershell
docker compose -f docker-compose.dev.yml up -d
```

Start development compose and rebuild the image:

```powershell
docker compose -f docker-compose.dev.yml up --build
```

Stop the development compose stack:

```powershell
docker compose -f docker-compose.dev.yml down
```

## Direct .NET Commands

Restore dependencies:

```powershell
dotnet restore
```

Build the project:

```powershell
dotnet build WebApiApp.csproj
```

Run the app locally:

```powershell
dotnet run --project WebApiApp.csproj
```

Run the app on a fixed local URL:

```powershell
dotnet run --project WebApiApp.csproj --no-launch-profile --urls http://127.0.0.1:5276
```

Publish a release build:

```powershell
dotnet publish WebApiApp.csproj -c Release -o .\artifacts\publish\ /p:UseAppHost=false
```

## Useful Checks

Check the health endpoint when the app is running:

```powershell
curl http://127.0.0.1:5276/health
```

Open the MCP metadata endpoint:

```powershell
curl http://127.0.0.1:5276/api/mcp
```

The default Docker port mapping exposes the app on:

```text
http://localhost:8080
```
