# 项目简介

[English](/guide/introduction.en) | [简体中文](/guide/introduction)

**Doggo** 是一个开源的游戏资讯平台,聚焦 **CS2(反恐精英 2)** 与 **Dota 2**,提供资讯聚合、饰品行情、开箱模拟与 Steam 机器人等能力。

项目源自一套已在生产环境使用的平台,现在把其中可公开的核心能力抽取出来,统一用 **C# / .NET** 重写为开源版本,支持在 Linux 上运行,通过 Docker 交付。

## 设计要点

- **Monorepo**:资讯抓取、行情同步、开箱玩法、Steam 机器人、管理后台在同一个仓库内,按模块拆分。
- **复用 vapor**:Steam 侧的数据与自动化(行情、库存、交易)基于 [vapor](https://github.com/cuihairu/vapor) 实现,vapor 与本项目同为同一作者维护的开源项目;CS2 / Dota 2 的外部资讯爬虫由本项目自建。
- **Linux 优先**:所有组件以 Linux 容器为一等公民,Docker Compose 编排。
- **文档先行**:架构、数据模型、玩法逻辑先文档化,再逐步实现。

## 核心能力

| 能力 | 说明 |
| --- | --- |
| 资讯聚合 | CS2 / Dota 2 官方动态、更新日志、赛事资讯的多源抓取与聚合展示 |
| 饰品行情 | 武器箱与饰品资料库,Steam 市场价格跟踪与趋势展示 |
| 开箱模拟 | 按官方掉落概率与磨损区间实现的开箱玩法,含库存与记录管理 |
| Steam 机器人 | 基于 vapor 的 Steam 侧自动化:行情同步、库存、交易 |
| 管理后台 | 用户、内容、饰品、订单、统计的运营管理界面 |

## 项目状态

M0-M12 里程碑已全部落地(服务端 469 例测试绿,web/admin/小程序/手机端构建通过)。剩余未完成项集中在 vapor 联调、小程序提审等外部依赖,清单见仓库根目录 TODO.md。欢迎通过 Issue 与 PR 参与讨论。

## 许可证

本项目基于 [Apache License 2.0](https://github.com/cuihairu/doggo/blob/main/LICENSE) 开源。
