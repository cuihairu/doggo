<div align="center">

<img src="docs/public/logo.svg" width="64" height="64" alt="Doggo" />

# Doggo

**开源游戏资讯与社区平台 —— CS2 / Dota 2 资讯聚合 · 饰品行情 · 开箱**

[English](README.md) | [简体中文](README.zh.md)

[![CI](https://github.com/cuihairu/doggo/actions/workflows/ci.yml/badge.svg)](https://github.com/cuihairu/doggo/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/cuihairu/doggo/branch/main/graph/badge.svg)](https://codecov.io/gh/cuihairu/doggo)
[![license](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![TypeScript](https://img.shields.io/badge/TypeScript-3178C6)](https://www.typescriptlang.org/)
[![React](https://img.shields.io/badge/React-61DAFB)](https://react.dev/)
[![Taro](https://img.shields.io/badge/Taro-blue)](https://taro-docs.jd.com/)
[![AntDesignPro](https://img.shields.io/badge/AntDesignPro-0170FE)](https://pro.ant.design/)
[![MySQL](https://img.shields.io/badge/MySQL-4479A1)](https://www.mysql.com/)
[![Redis](https://img.shields.io/badge/Redis-DC382D)](https://redis.io/)
[![docs](https://img.shields.io/badge/docs-VitePress-646CFF)](docs/)

</div>

Doggo 是一个聚焦 **CS2(反恐精英 2)** 与 **Dota 2** 的开源游戏资讯与社区平台,提供资讯聚合、饰品行情跟踪、开箱玩法与 Steam 自动化能力。全部服务端组件使用 **C# / .NET** 构建,原生支持 Linux,通过 Docker 部署。

## 功能

- 📰 **资讯聚合** —— 抓取 CS2 / Dota 2 官方公告、更新日志与赛事资讯,清洗去重后统一展示
- 📈 **饰品行情** —— 武器箱与饰品资料库,Steam 市场价格跟踪与趋势展示
- 🎁 **开箱** —— 基于真实 Steam 掉落的开箱玩法,含库存管理与开箱记录
- 🤖 **Steam 自动化** —— Steam 侧行情同步、库存与交易,通过 [vapor](https://github.com/cuihairu/vapor) 集成
- 🛠️ **管理后台** —— 用户、商品、箱子、资讯、订单与统计的运营管理界面

## 技术栈

| 层 | 选型 |
| --- | --- |
| 服务端 / API | ASP.NET Core(.NET 10) |
| 爬虫 / 后台任务 | .NET Worker Service + Playwright |
| Steam 自动化 | [vapor](https://github.com/cuihairu/vapor)(SteamKit2) |
| 存储 | MySQL + Redis |
| Web / 后台 | React + TypeScript / Ant Design Pro |
| 小程序 / 手机端 | Taro + React + TypeScript |
| 文档 | VitePress |

分工边界:vapor 负责与 Steam 网络交互的全部能力;CS2 / Dota 2 资讯爬虫在本仓库内自建。

## 文档

文档基于 VitePress,位于 [`docs/`](docs/) 目录:

```bash
pnpm install
pnpm docs:dev        # 本地预览 http://localhost:5173
pnpm docs:build      # 构建静态页面
```

建议从下面几篇开始:

- [项目简介](docs/guide/introduction.md)
- [功能总览](docs/guide/features.md)
- [系统架构](docs/guide/architecture.md)
- [原有平台功能分析](docs/design/legacy-analysis.md)

## 仓库结构

根目录按"应用"分层,源码放在各应用目录内:

```
doggo/
├── docs/          # VitePress 文档站点
├── server/        # .NET 后端解决方案(Doggo.slnx,项目位于 src/)
├── web/           # C 端 Web 用户站(React + TypeScript)
├── admin/         # 管理后台前端(Ant Design Pro)
├── miniapp/       # 小程序(Taro + React)
├── mobile/        # 手机端 App(Taro + React)
├── deploy/        # Docker Compose、nginx 配置
├── LICENSE        # Apache License 2.0
└── README.md
```

## 项目状态

服务端、管理后台、微信小程序与 Android 手机端已实现,任务拆分与进度见 [TODO.md](TODO.md)。欢迎通过 Issue 与 PR 参与讨论与共建。

## 许可证

[Apache License 2.0](LICENSE)
