# System Architecture

[English](/guide/architecture.en) | [简体中文](/guide/architecture)

## Overview

Doggo is organised as a monorepo, split into service modules by responsibility. Everything is built on C# / .NET (targeting .NET 10 LTS), runs natively on Linux, and is delivered through Docker Compose.

```
                        ┌──────────────────────────────┐
                        │        Doggo Web/API         │
   浏览器 / 客户端 ────▶│  资讯聚合 · 行情查询 · 开箱玩法  │
                        │  用户体系 · REST API          │
                        └───────┬───────────┬──────────┘
                                │           │
              ┌─────────────────┘           └──────────────┐
              ▼                                            ▼
   ┌─────────────────────┐                    ┌─────────────────────────┐
   │      管理后台        │                    │        vapor            │
   │  内容/饰品/运营管理   │                    │  Steam 自动化控制平面     │
   └─────────────────────┘                    │  行情同步 · 库存 · 交易   │
                                              │  开箱动作 · 插件体系      │
                                              └───────────┬─────────────┘
                                                          ▼
              ┌─────────────────────┐               Steam 网络
   CS2/Dota2  │     资讯爬虫         │
   资讯源 ───▶ │ 多源抓取·清洗·去重   │
              └─────────────────────┘
```

## Module responsibilities

| Module | Responsibility | Tech choice |
| --- | --- | --- |
| `Doggo.Api` | Core platform service: news aggregation, market queries, case opening, user accounts, funnel event reporting and analysis, REST API | ASP.NET Core Minimal API |
| `Doggo.Scraper` | **News scraper**: fetches external CS2 / Dota 2 sources (official announcements, patch notes, esports news), cleans, deduplicates and stores them (scheduling lives here too) | .NET Worker + HttpClient / Playwright |
| `Doggo.PriceCrawler` | **Third-party site price crawler**: site adapters, scheduling and rate control, cleaning and dedup, trends and price comparison; mounted inside the Scraper process | C# class library + AngleSharp |
| Analytics (funnel) | Four-step conversion event reporting, T-1 daily aggregation and funnel queries (entities in `Doggo.Domain/Analytics`, aggregation in Worker, queries in Api) | shipped with Api/Worker |
| `Doggo.Worker` | Background task scheduling: market sync, order timeout cancellation, reconciliation and compensation, roll room draws, points expiry, funnel daily aggregation | .NET Worker Service |
| `Doggo.Domain` | Domain model and shared contracts: skins, cases, news, users, opening records | C# class library |
| `web/` | Consumer web site (news, market, case opening, orders) | React + TypeScript |
| `admin/` | Admin console frontend | Ant Design Pro (React + TypeScript) |
| `miniapp/` | WeChat miniapp | Taro + React + TypeScript |
| `mobile/` | Mobile app | Taro + React + TypeScript |
| [vapor](https://github.com/cuihairu/vapor) (external dependency, same author) | **Steam-side capabilities**: skin market and inventory data sync, trading bot, case-opening execution. Talks only to the Steam network; not responsible for external news scraping | .NET + SteamKit2 |

## Division of labour: vapor vs the news scraper

- **vapor owns Steam.** Everything that talks to the Steam network — market price fetching, inventory sync, trade offers, case-opening actions — is integrated through vapor's REST API / webhooks, reusing its control plane / proxy architecture and plugin system (the CaseOpening, MarketWatch, GameData plugins).
- **The news scraper is built in-house.** Official CS2 / Dota 2 blogs, update announcements and esports news have nothing to do with Steam; `Doggo.Scraper` implements them independently and feeds standardised news entries into the aggregation pipeline.

## Data flow

1. **News flow**: `Doggo.Scraper` fetches sources on a schedule → clean / deduplicate / tag → write to the database → the API serves them to the front end.
2. **Market flow**: `Doggo.Worker` calls the vapor control plane on a schedule (POST /v1/jobs + polling) to pull market listings → stores snapshots and folds them into dailies; webhook events pushed by vapor are signature-checked and deduplicated before processing; when vapor is unreachable the API keeps serving the latest snapshots and the sync job backs off exponentially.
3. **Case-opening flow**: a user starts an opening → `Doggo.Api` settles it against the case configuration and probability algorithm → the result is recorded → (optionally) vapor performs the real Steam-side delivery (item grant / trade).

## Infrastructure

- **Database**: MySQL (primary store, utf8mb4)
- **Cache**: Redis (hot market data, news lists, sessions)
- **Deployment**: one-command Docker Compose; GitHub Actions CI/CD
- **Observability**: structured logging (Prometheus metrics are a planned item, not yet wired)

## Tech stack principles

- **One C# stack**: server, scrapers and bots all use C# / .NET — one language, one toolchain.
- **Open source first**: prefer mature open-source libraries (SteamKit2, Playwright for .NET) over in-house black boxes.
- **Linux first**: every component treats the Linux container as a first-class citizen.
