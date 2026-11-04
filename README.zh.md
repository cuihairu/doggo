<div align="center">
  <img src="docs/public/logo.svg" width="64" height="64" alt="Doggo" />
</div>

<div align="center">

# Doggo

面向 CS2 / Dota 2 的开源游戏资讯平台：资讯聚合 · 饰品行情 · 开箱

[English](README.md) | [简体中文](README.zh.md)

</div>

<div align="center">

<p align="center"><a href="https://github.com/cuihairu/doggo/releases"><img alt="Status" src="https://img.shields.io/badge/status-in%20development-orange"></a> <a href="https://github.com/cuihairu/doggo/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/cuihairu/doggo/actions/workflows/ci.yml/badge.svg"></a> <a href="https://codecov.io/gh/cuihairu/doggo"><img alt="codecov" src="https://codecov.io/gh/cuihairu/doggo/branch/main/graph/badge.svg"></a> <a href="LICENSE"><img alt="license" src="https://img.shields.io/badge/license-Apache%202.0-blue.svg"></a> <a href="https://dotnet.microsoft.com/"><img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4"></a> <a href="https://learn.microsoft.com/dotnet/csharp/"><img alt="C#" src="https://img.shields.io/badge/C%23-14-239120"></a> <a href="https://www.typescriptlang.org/"><img alt="TypeScript" src="https://img.shields.io/badge/TypeScript-3178C6"></a> <a href="https://react.dev/"><img alt="React" src="https://img.shields.io/badge/React-61DAFB"></a> <a href="https://taro-docs.jd.com/"><img alt="Taro" src="https://img.shields.io/badge/Taro-blue"></a> <a href="https://pro.ant.design/"><img alt="AntDesignPro" src="https://img.shields.io/badge/AntDesignPro-0170FE"></a> <a href="https://www.mysql.com/"><img alt="MySQL" src="https://img.shields.io/badge/MySQL-4479A1"></a> <a href="https://redis.io/"><img alt="Redis" src="https://img.shields.io/badge/Redis-DC382D"></a> <a href="https://cuihairu.github.io/doggo/zh/"><img src="https://img.shields.io/badge/%E6%96%87%E6%A1%A3-blue" alt="docs"></a></p>

</div>

Doggo 是一个面向 CS2（反恐精英 2）与 Dota 2 的开源游戏资讯平台，做资讯聚合、饰品行情、开箱与 Steam 自动化。服务端组件全部用 C# / .NET 编写，运行在 Linux 上，通过 Docker 部署。

## 功能

- 资讯聚合：抓取 CS2 / Dota 2 官方公告、更新日志与赛事资讯，清洗去重后统一展示
- 饰品行情：武器箱与饰品资料库，跟踪 Steam 市场价格与趋势
- 开箱：基于真实 Steam 掉落的开箱玩法，含库存管理与开箱记录
- Steam 自动化：Steam 侧行情同步、库存与交易，通过 [vapor](https://github.com/cuihairu/vapor) 集成
- 管理后台：用户、商品、箱子、资讯、订单与统计的运营管理界面
- 微信小程序：资讯、行情、开箱、订单与个人中心，基于 Taro

## 技术栈

| 层 | 选型 |
| --- | --- |
| 服务端 / API | ASP.NET Core（.NET 10） |
| 爬虫 / 后台任务 | .NET Worker Service + Playwright |
| Steam 自动化 | [vapor](https://github.com/cuihairu/vapor)（SteamKit2） |
| 存储 | MySQL + Redis |
| Web / 后台 | React + TypeScript / Ant Design Pro |
| 小程序 / 手机端 | Taro + React + TypeScript |
| 文档 | VitePress |

分工边界：与 Steam 网络的交互全部由 vapor 负责；CS2 / Dota 2 资讯爬虫在本仓库内实现。

## 文档

文档基于 VitePress，位于 [`docs/`](docs/) 目录：

```bash
pnpm install
pnpm docs:dev        # 本地预览 http://localhost:5173
pnpm docs:build      # 构建静态页面
```

主要页面：

- [项目简介](docs/guide/introduction.md)
- [功能总览](docs/guide/features.md)
- [系统架构](docs/guide/architecture.md)
- [原有平台功能分析](docs/design/legacy-analysis.md)

## 仓库结构

根目录按"应用"分层，源码放在各应用目录内：

```
doggo/
├── docs/          # VitePress 文档站点
├── server/        # .NET 后端解决方案（Doggo.slnx，项目位于 src/）
├── web/           # C 端 Web 用户站（React + TypeScript）
├── admin/         # 管理后台前端（Ant Design Pro）
├── miniapp/       # 小程序（Taro + React）
├── mobile/        # 手机端 App（Taro + React）
├── deploy/        # Docker Compose、nginx 配置
├── LICENSE        # Apache License 2.0
└── README.md
```

## 项目状态

已实现的部分：服务端（资讯、行情、订单、开箱、Steam 与微信登录）、管理后台、微信小程序、Android 手机端，以及 Docker 部署栈。剩余工作见 [todo.md](todo.md)：355 项清单已完成 331 项，未完成的是两项外部依赖（vapor 集成与小程序审核）、等待真实 MySQL 环境的深审并发/一致性项，以及未勾的代码评审发现，明细列在该文件中。每次 push 都会跑测试（见 `.github/workflows/ci.yml`），nightly 镜像发布到 GHCR。欢迎提 Issue 与 PR。

## 许可证

[Apache License 2.0](LICENSE)

## 本地测试口径

CI 与本地测试按改动面分成三种情况：改一行文档不该触发全量编译。

1. **文档改动**（`*.md`、`docs/**`、图片）：不编译。主 CI workflow 的
   `paths-ignore` 已跳过这些路径。
2. **代码改动**：只跑改动子集，不全量。
   - `dotnet test server/<改动 csproj>`
3. **全量**（`dotnet test server/Doggo.slnx`）：仅 *.csproj / Doggo.slnx、
   `.github/**`、Docker/Compose 配置变更，以及发布前触发。
