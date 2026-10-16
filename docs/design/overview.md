# 设计总览

[English](/en/design/overview) | [简体中文](/design/overview)

## 目标

把原平台的业务能力用统一的 C# / .NET 技术栈重写为开源版本,同时补齐原有版本缺失的资讯聚合能力(CS2 / Dota 2)。

## 模块映射

| 原平台模块 | 开源版模块 | 说明 |
| --- | --- | --- |
| server + service(Java) | `Doggo.Api`(ASP.NET Core) | C 端 REST API 与业务逻辑 |
| bot(Java 桥接层 + 外部机器人进程) | [vapor](https://github.com/cuihairu/vapor) | 控制平面/代理架构直接覆盖原"桥接层 + 外部进程"的职责;原 Redis Stream 协议被 vapor 的 REST API / Webhook 取代 |
| scraper(Selenium 价格爬虫) | vapor(Steam 侧)+ `Doggo.Scraper`(资讯) | 行情收敛到 Steam 侧;资讯爬虫为新增能力 |
| admin(Java 后台 API) | `Doggo.Api` 的 admin 路由区 | 不再单独起进程 |
| tracking(广告回传) | 无 | 不在开源版范围 |
| React 管理后台 | `admin/` 前端 | 沿用 React 生态,选型 Ant Design Pro |
| (开源版新增) | `web/`、`miniapp/`、`mobile/` 客户端 | 共用 `Doggo.Api`;React + TypeScript,小程序与手机端用 Taro 跨端 |
| common / dao | `Doggo.Domain` / `Doggo.Infrastructure` | EF Core 替代 MyBatis-Plus |

## 关键设计决策

1. **机器人不重写,复用 vapor。** 原平台最难维护的部分是"外部机器人进程 + 自定义消息协议"。vapor 已实现凭据管理、SteamGuard、交易、库存、开箱动作与插件体系,Doggo 只做集成方。
2. **平台不控制开箱概率。** 开箱走真实 Steam 掉落,Doggo 只负责流程状态机与记录;需要概率的玩法(roll 房)使用可审计的权重算法(AliasMethod)。
3. **资讯为独立管道。** CS2 / Dota 2 资讯源与 Steam 无关,由 `Doggo.Scraper` 自建,输出标准化条目;与行情数据互不依赖。
4. **支付可插拔。** 原平台硬编码多个国内支付渠道;开源版抽象为支付接口,渠道实现按需提供。
5. **金额用整型最小单位**(分),避免浮点误差;多币种仅做展示换算。

## 技术选型

| 关注点 | 选型 | 理由 |
| --- | --- | --- |
| 运行时 | .NET 10 (LTS) | 长期支持,Linux 一等公民 |
| Web 框架 | ASP.NET Core | 内置 JWT 认证、OpenAPI |
| ORM | EF Core + MySql.EntityFrameworkCore | Pomelo 尚无 EF Core 10 版本;provider 注册收敛在一处,后续可一行切换 |
| 缓存 | Redis(StackExchange.Redis) | 与原平台一致的缓存/计数器用途 |
| 资讯抓取 | HttpClient + AngleSharp / Playwright for .NET | 静态页与动态页分治 |
| Steam 集成 | vapor(SteamKit2) | 见上 |
| 调度 | Quartz.NET 或内置 PeriodicTimer | 定时任务与补偿 |
| 前端语言 | TypeScript | 全部客户端统一 |
| C 端 Web(`web/`) | React + Vite | 用户站 |
| 管理后台(`admin/`) | Ant Design Pro(UmiJS) | 中后台成熟方案 |
| 小程序(`miniapp/`) | Taro + React | 一套代码编译到微信/支付宝等小程序 |
| 手机端(`mobile/`) | Taro + React | 与小程序共享大部分代码,编译到 React Native / H5 |
| 文档 | VitePress | 本站点 |

## 仓库布局

根目录按"应用"分层,不直接放源码目录:

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

原则:同一 API 服务(`Doggo.Api`)支撑全部客户端;客户端之间不共享运行时代码,只共享 REST 契约。
