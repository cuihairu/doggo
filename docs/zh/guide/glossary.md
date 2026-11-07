# 术语表

本表按职责层定义概念。文件引用指向权威实现或声明，而不是仅仅指向名称相似的目录。

| 术语 | 英文 | 定义 | file:line |
| --- | --- | --- | --- |
| 契约与规则层 | Contract and rules layer | 与框架无关的实体、状态机、概率、价格和漏斗词汇，供所有服务端进程消费。 | `server/src/Doggo.Domain/DomainMarker.cs:1` |
| 订单状态机 | Order state machine | 订单在创建、已支付、履约中、已完成、已取消、已退款之间的合法迁移。 | `server/src/Doggo.Domain/Orders/OrderStateMachine.cs:8` |
| 抽奖房状态机 | Roll-room state machine | 抽奖房在待开始、运行中、开奖中、已开奖、领奖中、已结束之间的生命周期门槛。 | `server/src/Doggo.Domain/Cases/RollRoomStateMachine.cs:8` |
| 座位状态机 | Seat state machine | 拼箱单个座位的生命周期和乐观版本不变式。 | `server/src/Doggo.Domain/Cases/SeatStateMachine.cs:6` |
| 别名抽样法 | Alias method | 用于选择箱内奖品的常数时间离散分布抽样算法。 | `server/src/Doggo.Domain/Cases/AliasMethod.cs:9` |
| 价格规则 | Price rules | 生效价、数量阶梯、套餐选项优先级和返现计算。 | `server/src/Doggo.Domain/Products/PriceCalculator.cs:23` |
| 漏斗事件字典 | Analytics event dictionary | 服务端接受的事件名与属性键白名单，以及 schema 版本。 | `server/src/Doggo.Domain/Analytics/AnalyticsEventDictionary.cs:9` |
| 持久化与应用服务层 | Persistence and application-services layer | scoped EF/事务用例、映射、外部认证/Steam 客户端和跨表业务操作。 | `server/src/Doggo.Infrastructure/DoggoInfrastructureExtensions.cs:11` |
| 应用数据库上下文 | Application database context | 暴露全部 MySQL 域集合并应用审计变更的 EF Core 工作单元。 | `server/src/Doggo.Infrastructure/Persistence/DoggoDbContext.cs:15` |
| 模型配置 | Model configurations | 所有持久域的 EF 表、键、索引、转换和 JSON 列映射声明。 | `server/src/Doggo.Infrastructure/Persistence/Configurations/ModelConfigurations.cs:1` |
| 订单服务 | Order service | 创建、支付回调、取消、退款、提货、过期和对账的事务所有者。 | `server/src/Doggo.Infrastructure/Orders/OrderService.cs:55` |
| 拼箱服务 | Group-buying service | 箱套餐座位、归属检查、乐观并发、开箱、奖品和 Vapor 回滚的事务所有者。 | `server/src/Doggo.Infrastructure/Cases/GroupBuyingService.cs:33` |
| 抽奖服务 | Roll service | 抽奖房、参与、密码学开奖、中奖领奖和周期滚动的事务所有者。 | `server/src/Doggo.Infrastructure/Cases/RollService.cs:31` |
| 行情存储 | Market store | 市场条目、快照、日线、同步游标的读写方，以及 Vapor 挂牌的应用方。 | `server/src/Doggo.Infrastructure/Market/MarketStore.cs:19` |
| Vapor 客户端 | Vapor client | 面向 Steam 控制平面异步任务的类型化 HTTP 适配器，使用 Bearer 鉴权。 | `server/src/Doggo.Infrastructure/Vapor/VaporClient.cs:40` |
| Vapor Webhook | Vapor webhook | 带时间戳防重放验签的入站通知契约。 | `server/src/Doggo.Infrastructure/Vapor/VaporWebhook.cs:23` |
| 幂等认领存储 | Idempotency-claim store | 用 Redis SET NX 阻断 Webhook 重放的认领；Redis 不可用时退化为进程内记录。 | `server/src/Doggo.Infrastructure/Vapor/WebhookIdempotencyStore.cs:11` |
| 漏斗记录器 | Analytics recorder | 加入业务 SaveChanges 批次或在白名单过滤后持久匿名事件行的统一记录器。 | `server/src/Doggo.Infrastructure/Analytics/AnalyticsRecorder.cs:17` |
| 对接与编排层 | Service orchestration layer | 独立部署的 REST/认证/访问日志宿主，把外部调用转换为应用服务调用。 | `server/src/Doggo.Api/Program.cs:25` |
| 端点地图 | Endpoint map | 拥有 REST 面、请求 DTO、授权和响应形态的 ASP.NET Core 路由分组模块。 | `server/src/Doggo.Api/Endpoints/OrderEndpoints.cs:33` |
| 管理端点 | Admin endpoints | 覆盖内容、目录、订单、用户、抽奖房、价格审查、设置和统计的 admin-only 后台面。 | `server/src/Doggo.Api/Endpoints/AdminEndpoints.cs:27` |
| 访问日志中间件 | Visit-log middleware | 响应后写一条访问记录且不让 HTTP 响应失败的中间件。 | `server/src/Doggo.Api/VisitLogMiddleware.cs:13` |
| 事件限流器 | Event rate limiter | 面向匿名漏斗批次的进程内固定窗口限流器。 | `server/src/Doggo.Api/Endpoints/EventEndpoints.cs:195` |
| Vapor Webhook 端点 | Vapor webhook endpoint | API 的入站 `/api/vapor/webhook` 协议适配与认领/释放编排。 | `server/src/Doggo.Api/Endpoints/VaporEndpoints.cs:10` |
| 调度与异步层 | Scheduling and async layer | 独立执行行情、订单、抽奖、积分、漏斗和保留期工作的后台宿主。 | `server/src/Doggo.Worker/Program.cs:18` |
| 市场同步 Worker | Market-sync worker | 创建/轮询 Vapor 任务并把结果挂牌折叠为持久行情状态的定时任务。 | `server/src/Doggo.Worker/Market/MarketSyncWorker.cs:17` |
| 对账 Worker | Reconciliation worker | 补偿卡住的支付、退款和提货的周期任务。 | `server/src/Doggo.Worker/Orders/OrderReconciliationWorker.cs:13` |
| 漏斗聚合 Worker | Funnel-aggregation worker | 在事件明细保留前做 T-1 漏斗日统计重算。 | `server/src/Doggo.Worker/Analytics/FunnelAggregateWorker.cs:12` |
| 外部采集层 | External ingestion layer | 资讯与价格采集进程/库：调度不可信源、清洗载荷、去重并持久化结果。 | `server/src/Doggo.Scraper/Program.cs:11` |
| 资讯源适配器 | News source adapter | 把 RSS、Steam App News 或配置化 HTML 规范化为公共资讯候选。 | `server/src/Doggo.Scraper/News/INewsSourceAdapter.cs:6` |
| 资讯源注册表 | Source-adapter registry | 按配置的资讯源类型选择适配器。 | `server/src/Doggo.Scraper/News/SourceAdapterRegistry.cs:9` |
| 资讯调度器 | Scraper scheduler | 资讯抓取的按源到点、失败阈值和冷却状态机。 | `server/src/Doggo.Scraper/News/ScraperScheduler.cs:10` |
| 资讯存储 | News store | 持久化规范资讯候选，并在抓取后维护来源计数。 | `server/src/Doggo.Scraper/News/NewsStore.cs:9` |
| 价格爬虫 Worker | Price-crawler worker | 经调度器发现并抓取配置价格站点的 hosted loop。 | `server/src/Doggo.PriceCrawler/PriceCrawlerWorker.cs:18` |
| 价格站点适配器 | Price-site adapter | 把站点 HTML/配置或编码逻辑转换为规范价格观察值。 | `server/src/Doggo.PriceCrawler/Html/ConfiguredHtmlSiteAdapter.cs:14` |
| 价格采集调度器 | Price-crawler scheduler | 应用全局/站点间隔、并发、夜间窗口、robots、重试和熔断策略。 | `server/src/Doggo.PriceCrawler/PriceCrawlerScheduler.cs:36` |
| 价格入库器 | Price-crawl ingestor | 持久化原始观察值、映射外部 ID、清洗/去重序列并更新站点状态。 | `server/src/Doggo.PriceCrawler/PriceCrawlIngestor.cs:22` |
| 价格审查端点 | Price-review endpoint | 把外部采集 ID 映射到市场条目的 Admin API 面。 | `server/src/Doggo.PriceCrawler/PriceCrawlerAdminEndpoints.cs:11` |
| 客户端体验层 | Client experience layer | 平台特定用户/运营界面、本地展示状态和 REST 转换。 | `web/src/main.tsx:11` |
| 浏览器 API 客户端 | Browser API client | 支持 Cookie 会话、ProblemDetails 解析和透明刷新的 Web fetch 封装。 | `web/src/api/client.ts:22` |
| 共享 Taro 客户端 | Shared Taro client | 小程序/RN 的请求、令牌、storage 接缝、session 和漏斗传输实现。 | `shared/src/client.ts:8` |
| 共享端点函数 | Shared endpoint functions | 描述小程序/RN REST 路径、参数和响应视图的类型化函数。 | `shared/src/endpoints.ts:1` |
| 客户端事件队列 | Client event queue | 可离线入队并批量上报的漏斗本地队列。 | `shared/src/events.ts:1` |
| 管理台访问控制 | Admin access control | 由认证后的 admin role claim 驱动的运行时门禁。 | `admin/src/access.ts:2` |
| 运行面与共享库层 | Runtime surfaces and shared-library layer | 数据库/缓存、迁移、代理/静态托管、编排以及可复用 Taro 客户端库。 | `deploy/docker-compose.prod.yml:35` |
| 生产栈 | Production stack | MySQL、Redis、migrator、API、Scraper、Worker、web 和 admin 的有序部署。 | `deploy/docker-compose.prod.yml:6` |
| 数据库迁移器 | Database migrator | 在 API 启动前完成的一次性 EF 迁移容器。 | `deploy/docker-compose.prod.yml:35` |
| 前端反代 | Frontend reverse proxy | 生产中把 `/api` 路由到服务并托管已构建 Web 资源。 | `deploy/nginx/doggo.conf.template:19` |
| 客户端标识 | Client identifier | 被解析为 AnalyticsClient 的 `X-Doggo-Client` 值，用于访问与事件。 | `server/src/Doggo.Infrastructure/Analytics/AnalyticsClients.cs:10` |
| 访客键 | Visitor key | IP 与 User-Agent 的稳定加盐哈希，用于关联匿名行为且不保存原始 IP。 | `server/src/Doggo.Infrastructure/Analytics/VisitorKeys.cs:8` |
| 会话标识 | Session identifier | 客户端生成的持久标识，经 `X-Doggo-Session` 发送，闲置 30 分钟后刷新。 | `shared/src/client.ts:179` |
