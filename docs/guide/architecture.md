# 系统架构

[English](/en/guide/architecture) | [简体中文](/guide/architecture)

## 总览

Doggo 采用 monorepo 组织,按职责拆分为多个服务模块,全部基于 C# / .NET(目标 .NET 10 LTS),原生运行于 Linux,通过 Docker Compose 编排交付。

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

## 模块职责

| 模块 | 职责 | 技术选型 |
| --- | --- | --- |
| `Doggo.Api` | 平台核心服务:资讯聚合、行情查询、开箱玩法、用户体系、漏斗事件上报与分析、REST API | ASP.NET Core Minimal API |
| `Doggo.Scraper` | **资讯爬虫**:抓取 CS2 / Dota 2 官方公告、更新日志、赛事资讯等外部资讯源,清洗、去重、入库(调度亦在本项目) | .NET Worker + HttpClient / Playwright |
| `Doggo.PriceCrawler` | **第三方站点价格爬虫**:站点适配器、调度频控、清洗去重、趋势与比价,由 Scraper 进程挂载 | C# 类库 + AngleSharp |
| Analytics(漏斗) | 四步转化事件上报、T-1 日聚合与漏斗查询(实体在 `Doggo.Domain/Analytics`,聚合在 Worker,查询在 Api) | 随 Api/Worker 交付 |
| `Doggo.Worker` | 后台任务调度:行情同步、订单超时取消、对账补偿、roll 房开奖、积分过期、漏斗每日聚合 | .NET Worker Service |
| `Doggo.Domain` | 领域模型与共享契约:饰品、箱子、资讯、用户、开箱记录 | C# 类库 |
| `web/` | C 端 Web 用户站(资讯、行情、开箱、订单) | React + TypeScript |
| `admin/` | 管理后台前端 | Ant Design Pro(React + TypeScript) |
| `miniapp/` | 小程序 | Taro + React + TypeScript |
| `mobile/` | 手机端 App | Taro + React + TypeScript |
| [vapor](https://github.com/cuihairu/vapor)(外部依赖,同一作者) | **Steam 侧能力**:饰品行情与库存数据同步、交易机器人、开箱动作执行。只对接 Steam 网络,不负责外部资讯抓取 | .NET + SteamKit2 |

## 分工边界:vapor 与资讯爬虫

- **vapor 只负责 Steam**。所有需要与 Steam 网络交互的能力——市场行情抓取、库存同步、交易报价、开箱动作——都通过 vapor 的 REST API / Webhook 集成,复用其控制平面 / 代理架构与插件体系(如 CaseOpening、MarketWatch、GameData 插件)。
- **资讯爬虫自建**。CS2 / Dota 2 的官方博客、更新公告、赛事资讯等外部资讯源与 Steam 无关,由 `Doggo.Scraper` 独立实现,输出标准化的资讯条目进入聚合管道。

## 数据流

1. **资讯流**:`Doggo.Scraper` 定时抓取资讯源 → 清洗/去重/打标 → 写入数据库 → API 供前台展示。
2. **行情流**:`Doggo.Worker` 定时调用 vapor 控制平面(POST /v1/jobs + 轮询)拉取市场挂牌 → 落快照并折叠日线;vapor 推送的 Webhook 事件经验签与幂等后进入处理;vapor 不可用时 API 继续读最近快照,同步任务指数退避。
3. **开箱流**:用户发起开箱 → `Doggo.Api` 按箱子配置与概率算法结算 → 记录开箱结果 → (可选)vapor 执行真实的 Steam 侧交割(饰品发放/交易)。

## 基础设施

- **数据库**:MySQL(主存储,utf8mb4)
- **缓存**:Redis(热点行情、资讯列表、会话)
- **部署**:Docker Compose 一键编排;GitHub Actions CI/CD
- **可观测**:结构化日志(Prometheus 指标为计划项,暂未接入)

## 技术栈原则

- **统一 C#**:服务端、爬虫、机器人全部使用 C# / .NET,一套语言、一套工具链。
- **开源优先**:优先选择成熟的开源库(SteamKit2、Playwright for .NET 等),避免自研黑盒。
- **Linux 优先**:所有组件以 Linux 容器为一等公民。
