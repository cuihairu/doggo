<div align="center">

<img src="docs/public/logo.svg" width="120" alt="Doggo" />

# Doggo

**An open-source gaming info & community platform — CS2 / Dota 2 news aggregation, skin market data, case opening**

[English](README.md) | [简体中文](README.zh.md)

[![license](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-13-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![docs](https://img.shields.io/badge/docs-VitePress-646CFF)](docs/)

</div>

Doggo is an open-source gaming info & community platform focused on **CS2 (Counter-Strike 2)** and **Dota 2**. It covers news aggregation, skin market data, case opening, and Steam automation. All server-side components are written in **C# / .NET**, run natively on Linux, and deploy with Docker.

## Features

- 📰 **News aggregation** — pulls official announcements, patch notes and tournament news for CS2 / Dota 2, then cleans, deduplicates and displays them
- 📈 **Skin market data** — weapon case and skin catalog with Steam market price tracking and trend charts
- 🎁 **Case opening** — case opening built on real Steam drops, with inventory management and opening records
- 🤖 **Steam automation** — market sync, inventory and trading on the Steam side, integrated via [vapor](https://github.com/cuihairu/vapor)
- 🛠️ **Admin console** — operations UI for users, products, cases, content, orders and statistics

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
├── server/        # .NET backend solution (sln + projects under src/)
├── web/           # user-facing web frontend (React + TypeScript)
├── admin/         # admin console frontend (Ant Design Pro)
├── miniapp/       # WeChat mini program (Taro + React)
├── mobile/        # mobile app (Taro + React)
├── deploy/        # Docker Compose, nginx configs
├── LICENSE        # Apache License 2.0
└── README.md
```

## Status

Early stage — documentation and architecture come first, code follows module by module. Discussions and contributions are welcome via Issues and Pull Requests.

## License

[Apache License 2.0](LICENSE)
