# Design Overview


## Goals

Rewrite the legacy platform's business capabilities in a unified C# / .NET stack as an open-source edition, and at the same time add the news aggregation capability that the original edition lacked (CS2 / Dota 2).

## Module mapping

| Legacy module | Open-source edition module | Notes |
| --- | --- | --- |
| server + service (Java) | `Doggo.Api` (ASP.NET Core) | Consumer REST API and business logic |
| bot (Java bridge layer + external bot process) | [vapor](https://github.com/cuihairu/vapor) | The control plane / proxy architecture directly covers the old "bridge layer + external process" responsibilities; the original Redis Stream protocol is replaced by vapor's REST API / webhooks |
| scraper (Selenium price crawler) | vapor (Steam side) + `Doggo.Scraper` (news) | Market data converges on the Steam side; the news scraper is a new capability |
| admin (Java backend API) | The admin route group of `Doggo.Api` | No separate process |
| tracking (ad attribution) | none | Out of scope for the open-source edition |
| React admin console | `admin/` frontend | Stays on React, using Ant Design Pro |
| (new in the open-source edition) | `web/`, `miniapp/`, `mobile/` clients | All share `Doggo.Api`; React + TypeScript, with miniapp and mobile using Taro for cross-platform |
| common / dao | `Doggo.Domain` / `Doggo.Infrastructure` | EF Core replaces MyBatis-Plus |

## Key design decisions

1. **Do not rewrite the bot; reuse vapor.** The hardest part of the legacy platform to maintain was the "external bot process + custom message protocol". vapor already implements credential management, SteamGuard, trading, inventory, case-opening actions and the plugin system; Doggo is only the integrator.
2. **The platform does not control opening probabilities.** Case openings use real Steam drops; Doggo owns only the flow state machine and records. Gameplay that needs probabilities (roll rooms) uses an auditable weighted algorithm (AliasMethod).
3. **News is an independent pipeline.** CS2 / Dota 2 news sources have nothing to do with Steam; `Doggo.Scraper` implements them in-house and emits standardised entries. News data and market data do not depend on each other.
4. **Payments are pluggable.** The legacy platform hardcoded several domestic payment channels; the open-source edition abstracts them into a payment interface with channel implementations provided as needed.
5. **Money uses integer minor units** (cents) to avoid floating-point error; multi-currency is display conversion only.

## Tech choices

| Concern | Choice | Why |
| --- | --- | --- |
| Runtime | .NET 10 (LTS) | Long-term support, first-class on Linux |
| Web framework | ASP.NET Core | Built-in JWT authentication, OpenAPI |
| ORM | EF Core + MySql.EntityFrameworkCore | Pomelo has no EF Core 10 version yet; provider registration is concentrated in one place so it can be switched with one line later |
| Cache | Redis (StackExchange.Redis) | Same cache / counter use as the legacy platform |
| News scraping | HttpClient + AngleSharp / Playwright for .NET | Static pages and dynamic pages handled separately |
| Steam integration | vapor (SteamKit2) | See above |
| Scheduling | Quartz.NET or the built-in PeriodicTimer | Timed jobs and compensation |
| Frontend language | TypeScript | Unified across all clients |
| Consumer web (`web/`) | React + Vite | The user site |
| Admin console (`admin/`) | Ant Design Pro (UmiJS) | A mature mid-to-backend choice |
| Miniapp (`miniapp/`) | Taro + React | One code base compiling to WeChat / Alipay miniapps |
| Mobile (`mobile/`) | Taro + React | Shares most code with the miniapp, compiling to React Native / H5 |
| Documentation | VitePress | This site |

## Repository layout

The root is layered by "application"; source directories are not placed directly at the top level:

```
doggo/
├── docs/                  # VitePress 文档站点
├── server/                # .NET 后端解决方案
│   ├── Doggo.slnx
│   ├── src/
│   │   ├── Doggo.Api/         # REST API 与业务服务
│   │   ├── Doggo.Scraper/     # 资讯爬虫(Worker Service,挂载价格爬虫)
│   │   ├── Doggo.Worker/      # 定时任务与后台作业
│   │   ├── Doggo.PriceCrawler/ # 第三方站点价格爬虫(独立类库)
│   │   ├── Doggo.Domain/      # 领域模型
│   │   └── Doggo.Infrastructure/ # EF Core、Redis、外部集成
│   └── tests/
├── web/                   # C 端 Web 用户站(React + TypeScript)
├── admin/                 # 管理后台前端(Ant Design Pro)
├── miniapp/               # 小程序(Taro + React + TypeScript)
├── mobile/                # 手机端 App(Taro + React + TypeScript)
└── deploy/                # Docker Compose、nginx 配置
```

Principle: one API service (`Doggo.Api`) backs every client; clients share no runtime code with each other, only the REST contract.
