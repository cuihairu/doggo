<div align="center">

<img src="docs/public/logo.svg" width="64" height="64" alt="Doggo" />

# Doggo

**An open-source gaming info platform — CS2 / Dota 2 news aggregation, skin market data, case opening**

[English](README.md) | [简体中文](README.zh.md)

<p align="center">
<a href="https://github.com/cuihairu/doggo/releases"><img alt="Status" src="https://img.shields.io/badge/status-in%20development-orange"></a> <a href="https://github.com/cuihairu/doggo/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/cuihairu/doggo/actions/workflows/ci.yml/badge.svg"></a> <a href="https://codecov.io/gh/cuihairu/doggo"><img alt="codecov" src="https://codecov.io/gh/cuihairu/doggo/branch/main/graph/badge.svg"></a> <a href="LICENSE"><img alt="license" src="https://img.shields.io/badge/license-Apache%202.0-blue.svg"></a> <a href="https://dotnet.microsoft.com/"><img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4"></a> <a href="https://learn.microsoft.com/dotnet/csharp/"><img alt="C#" src="https://img.shields.io/badge/C%23-14-239120"></a> <a href="https://www.typescriptlang.org/"><img alt="TypeScript" src="https://img.shields.io/badge/TypeScript-3178C6"></a> <a href="https://react.dev/"><img alt="React" src="https://img.shields.io/badge/React-61DAFB"></a> <a href="https://taro-docs.jd.com/"><img alt="Taro" src="https://img.shields.io/badge/Taro-blue"></a> <a href="https://pro.ant.design/"><img alt="AntDesignPro" src="https://img.shields.io/badge/AntDesignPro-0170FE"></a> <a href="https://www.mysql.com/"><img alt="MySQL" src="https://img.shields.io/badge/MySQL-4479A1"></a> <a href="https://redis.io/"><img alt="Redis" src="https://img.shields.io/badge/Redis-DC382D"></a> <a href="docs/"><img alt="docs" src="https://img.shields.io/badge/docs-VitePress-646CFF"></a>
</p>

</div>

Doggo is an open-source gaming info platform focused on **CS2 (Counter-Strike 2)** and **Dota 2**. It covers news aggregation, skin market data, case opening, and Steam automation. All server-side components are written in **C# / .NET**, run natively on Linux, and deploy with Docker.

## Features

- 📰 **News aggregation** — pulls official announcements, patch notes and tournament news for CS2 / Dota 2, then cleans, deduplicates and displays them
- 📈 **Skin market data** — weapon case and skin catalog with Steam market price tracking and trend charts
- 🎁 **Case opening** — case opening built on real Steam drops, with inventory management and opening records
- 🤖 **Steam automation** — market sync, inventory and trading on the Steam side, integrated via [vapor](https://github.com/cuihairu/vapor)
- 🛠️ **Admin console** — operations UI for users, products, cases, content, orders and statistics
- 📱 **WeChat miniapp** — news, market, case opening, orders and user center on Taro

## Tech stack

| Layer | Choice |
| --- | --- |
| Server / API | ASP.NET Core (.NET 10) |
| Scraper / background jobs | .NET Worker Service + Playwright |
| Steam automation | [vapor](https://github.com/cuihairu/vapor) (SteamKit2) |
| Storage | MySQL + Redis |
| Web / admin | React + TypeScript / Ant Design Pro |
| Mini program / mobile | Taro + React + TypeScript |
| Docs | VitePress |

Boundary: vapor handles everything that talks to the Steam network; the CS2 / Dota 2 news scraper is built in this repository.

## Documentation

The docs live in [`docs/`](docs/) and are built with VitePress:

```bash
pnpm install
pnpm docs:dev        # local preview at http://localhost:5173
pnpm docs:build      # build static pages
```

A good starting point:

- [Introduction](docs/guide/introduction.md)
- [Features](docs/guide/features.md)
- [Architecture](docs/guide/architecture.md)
- [Legacy platform analysis](docs/design/legacy-analysis.md)

## Repository layout

The root is organized by application; source code lives under each app directory:

```
doggo/
├── docs/          # VitePress documentation
├── server/        # .NET backend solution (Doggo.slnx, projects under src/)
├── web/           # user-facing web frontend (React + TypeScript)
├── admin/         # admin console frontend (Ant Design Pro)
├── miniapp/       # WeChat mini program (Taro + React)
├── mobile/        # mobile app (Taro + React)
├── deploy/        # Docker Compose, nginx configs
├── LICENSE        # Apache License 2.0
└── README.md
```

## Status

The server (news, market, orders, cases, auth with Steam and WeChat login), admin console, WeChat miniapp, the Android app and the Docker deploy stack are implemented; 243 of 245 checklist items in [todo.md](todo.md) are done — the open ones are two external dependencies (the vapor integration and the miniapp review), listed in that file. Tests run on every push (see `.github/workflows/ci.yml`) and nightly images publish to GHCR. Discussions and contributions are welcome via Issues and Pull Requests.

## License

[Apache License 2.0](LICENSE)

## Local testing by change type

CI and local development split by the size of the change — a one-line doc edit should not trigger a full build.

1. **Docs changes** (`*.md`, `docs/**`, images): no build. The main CI workflow's
   `paths-ignore` already skips these paths.
2. **Code changes**: run only the affected subset, not the full suite.
   - `dotnet test server/<changed csproj>`
3. **Full suite** (`dotnet test server/Doggo.slnx`): only for changes to *.csproj / Doggo.slnx,
   `.github/**`, Docker/Compose configuration, and before a release.

