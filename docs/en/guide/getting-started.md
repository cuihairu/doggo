# Getting Started

[English](/en/guide/getting-started) | [简体中文](/guide/getting-started)

## Requirements

| Dependency | Version | Used for |
| --- | --- | --- |
| .NET SDK | 10.0 | server, scrapers, bots |
| Docker + Docker Compose | stable | deployment (MySQL, Redis, etc.) |
| Node.js | 20+ | documentation site (VitePress) |

## Local development

```bash
# Clone the repository
git clone https://github.com/cuihairu/doggo.git
cd doggo

# Build the backend solution
dotnet build server/Doggo.slnx

# Start the infrastructure (MySQL 8.4 + Redis 7, with health checks)
cp deploy/.env.example deploy/.env   # change passwords and ports as needed
docker compose -f deploy/docker-compose.yml up -d

# Install the EF tools and apply the database migrations
dotnet tool install --global dotnet-ef
cd server
dotnet ef database update --project src/Doggo.Infrastructure --startup-project src/Doggo.Api
```

If ports 3306 / 6379 on the host are already taken, change `MYSQL_PORT` / `REDIS_PORT` in `deploy/.env` and override the connection strings with environment variables when starting the application:

```bash
ConnectionStrings__Default="Server=localhost;Port=13306;Database=doggo;User=doggo;Password=..."
ConnectionStrings__Redis="localhost:16379"
```

### Verification

```bash
curl http://localhost:<port>/healthz
# {"status":"ok","database":true,"redis":true}
```

## Documentation site

The docs are built with VitePress and live in `docs/`:

```bash
pnpm install

# Local preview, defaults to http://localhost:5173
pnpm docs:dev

# Build the static pages into docs/.vitepress/dist
pnpm docs:build
```

## Roadmap

- [x] Project documentation and architecture design (`docs/`)
- [x] Domain model and data layer (`server/src/Doggo.Domain`, EF migrations run with the deployment)
- [x] News scraper and aggregation pipeline (`server/src/Doggo.Scraper`)
- [x] Skin market sync (based on vapor) (`server/src/Doggo.Infrastructure/Vapor`, scheduled by the Worker)
- [x] Case-opening simulation (`server/src/Doggo.Infrastructure/Cases`)
- [x] REST API and user accounts (`server/src/Doggo.Api`, JWT + Steam OpenID)
- [x] Admin console (`admin/` frontend + `/api/admin`)
- [x] Third-party price crawler (`server/src/Doggo.PriceCrawler`, merged into the Scraper process)
- [x] Funnel analytics (event channel + daily aggregation + admin funnel page)
