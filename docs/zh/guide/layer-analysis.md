# 分层分析

本页按源码归纳“职责层”，不把目录名当层名。逐层回答实现位置、配置 schema 声明位置、专属协议位置、用户文案挂载、状态存储归属，以及调用双方和通道。

## 层地图

### L1 契约与规则层

- **职责**：定义业务形态、状态迁移、抽样、价格与漏斗词汇，不依赖 ASP.NET Core、EF Core、MySQL 或 Redis。
- **实现**：实体与规则位于 `server/src/Doggo.Domain`，例如用户在 `server/src/Doggo.Domain/Users/UserEntities.cs`、订单在 `server/src/Doggo.Domain/Orders/OrderEntities.cs`、开箱在 `server/src/Doggo.Domain/Cases/CaseEntities.cs`、价格采集在 `server/src/Doggo.Domain/Price/PriceCrawlEntities.cs`。
- **配置 schema**：`AnalyticsEventDictionary` 在 `server/src/Doggo.Domain/Analytics/AnalyticsEventDictionary.cs:13` 声明事件与属性白名单；`AnalyticsOptions` 在 `server/src/Doggo.Infrastructure/Analytics/AnalyticsOptions.cs:4` 定义明细保留期。
- **专属协议/文案**：无。传输 DTO 属 L3；界面文案属 L6。
- **状态/调用面**：无专属存储。`Doggo.Api`、`Doggo.Worker`、`Doggo.Scraper`、`Doggo.PriceCrawler` 消费这些类型；`Doggo.Infrastructure` 经项目引用使用，见 `server/src/Doggo.Infrastructure/Doggo.Infrastructure.csproj:14`。
- **生命周期**：编译进所有服务端宿主；无状态类型与纯规则在进程启动时可用。

### L2 持久化与应用服务层

- **职责**：持有事务边界、EF 映射、跨表用例、外部 Steam/OpenID/微信客户端和运营不变式。
- **实现**：`DoggoInfrastructureExtensions` 在 `server/src/Doggo.Infrastructure/DoggoInfrastructureExtensions.cs:11` 注册 MySQL 与 Redis；`DoggoDbContext` 在 `server/src/Doggo.Infrastructure/Persistence/DoggoDbContext.cs:15` 暴露域集合并重写审计；应用服务包括 `OrderService`（`server/src/Doggo.Infrastructure/Orders/OrderService.cs:55`）、`GroupBuyingService`（`server/src/Doggo.Infrastructure/Cases/GroupBuyingService.cs:33`）、`RollService`（`server/src/Doggo.Infrastructure/Cases/RollService.cs:31`）、`MarketStore`（`server/src/Doggo.Infrastructure/Market/MarketStore.cs:19`）。
- **配置 schema**：连接字符串在 `server/src/Doggo.Infrastructure/DoggoInfrastructureExtensions.cs:17` 读取；`AuthOptions` 声明于 `server/src/Doggo.Infrastructure/Auth/AuthOptions.cs:7`；`VaporOptions` 声明于 `server/src/Doggo.Infrastructure/Vapor/VaporOptions.cs:4`；`WechatOptions` 声明于 `server/src/Doggo.Infrastructure/Auth/WechatSessionClient.cs:21`；邀请与签到选项分别声明于 `server/src/Doggo.Infrastructure/Marketing/InviteOptions.cs:5` 和 `server/src/Doggo.Infrastructure/Marketing/SignOptions.cs:5`。
- **专属协议/文案**：无。
- **状态/调用面**：MySQL 持有权威业务与漏斗状态；Redis 持有 Vapor Webhook 幂等认领，见 `server/src/Doggo.Infrastructure/Vapor/WebhookIdempotencyStore.cs:51`；资讯、行情、商品、用户、订单、漏斗均不使用进程内存持久状态。L3/L4 进程内调用应用服务；L2 经 EF Core 调 MySQL，经 StackExchange.Redis 调 Redis，经 HTTP 调 Steam/OpenID、微信或 Vapor。
- **生命周期**：各宿主把 `DoggoDbContext` 与用例服务注册为 scoped；Vapor 与幂等客户端分别是 HTTP client 或 singleton；MySQL 数据跨部署存活，Redis 失败时幂等认领退化为进程内记录。

### L3 对接与编排层

- **职责**：为浏览器、小程序、RN App 和管理台提供一个独立部署的 REST 面；完成认证、端点授权、访问日志和应用服务编排。
- **实现**：`Program` 在 `server/src/Doggo.Api/Program.cs:25` 组装安全、客户端和存储；端点注册从前缀开始，见 `server/src/Doggo.Api/Program.cs:139`；`VisitLogMiddleware` 在 `server/src/Doggo.Api/VisitLogMiddleware.cs:13` 的 `finally` 中写访问日志；管理台角色检查在 `server/src/Doggo.Api/Endpoints/AdminEndpoints.cs:27` 这类端点映射内声明。
- **配置 schema**：`AuthOptions`、`WechatOptions`、`VaporOptions`、`InviteOptions`、`SignOptions` 在 `server/src/Doggo.Api/Program.cs:29` 绑定所属节；OpenAPI 仅 Development 暴露，见 `server/src/Doggo.Api/Program.cs:125`。
- **专属协议/文案**：公共协议是 REST，分布在端点文件而不是单一 schema 文件。分组包括 `/api/auth`、`/api/products`、`/api/orders`、`/api/group-buyings`、`/api/roll`、`/api/events`、`/api/admin`；`/api/vapor/webhook` 定义入站 Vapor 协议，见 `server/src/Doggo.Api/Endpoints/VaporEndpoints.cs:10`；此处不挂静态 UI 文案。
- **状态/调用面**：基本无状态。简单管理查询直接读 MySQL，事务用例调用 L2；事件限流器有意为进程内存，见 `server/src/Doggo.Api/Endpoints/EventEndpoints.cs:195`。Web/管理台走同源 HTTP；小程序与 RN 经 `shared/src/client.ts:112` 以 JSON 和 Bearer 调用；Vapor 推送签名 Webhook，失败时释放 Redis 幂等认领以便重试。
- **生命周期**：一个 ASP.NET Core 宿主。启动构建 DI 与路由；请求依次认证、授权、访问日志、路由处理；`/healthz` 检查 MySQL 与 Redis，见 `server/src/Doggo.Api/Program.cs:153`；Compose 先执行一次性迁移，再启动 API，见 `deploy/docker-compose.prod.yml:35`。

### L4 调度与异步层

- **职责**：独立驱动延迟和周期任务，不阻塞请求线程，也不依赖 API 实例存活。
- **实现**：`Doggo.Worker/Program` 在 `server/src/Doggo.Worker/Program.cs:18` 注册所有循环。`MarketSyncWorker` 在 `server/src/Doggo.Worker/Market/MarketSyncWorker.cs:17` 轮询 Vapor 任务 API；其余入口分别是 `server/src/Doggo.Worker/Orders/OrderTimeoutWorker.cs:10`、`server/src/Doggo.Worker/Orders/OrderReconciliationWorker.cs:13`、`server/src/Doggo.Worker/Cases/RollRoomWorker.cs:10`、`server/src/Doggo.Worker/Marketing/CoinExpiryWorker.cs:10`、`server/src/Doggo.Worker/Analytics/FunnelAggregateWorker.cs:12`、`server/src/Doggo.Worker/Analytics/AnalyticsRetentionWorker.cs:13`。
- **配置 schema**：在 `server/src/Doggo.Worker/Program.cs:21` 绑定 `Vapor`、`Market:Sync`、`Order`、`Roll`、`Coin`、`Analytics`、`Invite`、`Sign`。
- **专属协议/文案**：无。出站 Vapor API 协议与 L2 共用 `server/src/Doggo.Infrastructure/Vapor/VaporClient.cs:40`。
- **状态/调用面**：无状态循环委托给 L2，所有持久结果落在 MySQL。市场同步经 HTTP 调 Vapor；其他循环查询/更新 MySQL。没有 UI 通道直接调用 L4。
- **生命周期**：一个 .NET `BackgroundService` 宿主。启动绑定配置并注册 Hosted Service；每个循环自带间隔或到点条件；部署为常驻独立进程，与 API、Scraper、前端解耦，见 `deploy/docker-compose.prod.yml:90`。

### L5 外部采集层

- **职责**：获取外部资讯与第三方价格观察值，规范和清洗数据，执行调度与合规控制，再持久化序列或文章。
- **实现**：一个 `Doggo.Scraper` 进程同时承载资讯与价格采集。宿主在 `server/src/Doggo.Scraper/Program.cs:11` 注册资讯适配器、存储、调度器和 Playwright，再在 `server/src/Doggo.Scraper/Program.cs:38` 挂载价格爬虫。资讯入口是 `server/src/Doggo.Scraper/ScraperWorker.cs`；RSS、Steam App News、HTML 适配器在 `server/src/Doggo.Scraper/News/Adapters`。价格 Worker 入口为 `server/src/Doggo.PriceCrawler/PriceCrawlerWorker.cs:18`；适配器、入库和调度器同库；管理审查端点由 API 挂载，声明在 `server/src/Doggo.PriceCrawler/PriceCrawlerAdminEndpoints.cs:23`。
- **配置 schema**：`Scraper` 在 `server/src/Doggo.Scraper/Program.cs:31` 绑定，默认值在 `server/src/Doggo.Scraper/appsettings.json:8`；`PriceCrawlerOptions` 声明于 `server/src/Doggo.PriceCrawler/PriceCrawlerOptions.cs:67`，在 `server/src/Doggo.PriceCrawler/PriceCrawlerServiceCollectionExtensions.cs:18` 绑定。
- **专属协议/文案**：无用户协议。适配器输入是 RSS/Atom、Steam App News JSON、普通 HTTP HTML 和 robots.txt；robots 处理实现在 `server/src/Doggo.PriceCrawler/Robots/RobotsChecker.cs:58`。`/api/admin/price-crawl/maps` 管理面由 `server/src/Doggo.PriceCrawler/PriceCrawlerAdminEndpoints.cs:23` 注册进 L3。
- **状态/调用面**：MySQL 持有资讯源/条目、采集映射、原始值、清洗序列和站点状态；没有 Redis 或进程内存持久状态。它通过 HTTP 调外部源；L3 暴露其管理端点，Admin 经 `admin/src/pages/Settings/index.tsx:191` 读取。
- **生命周期**：生产为一个常驻 Worker 进程。`NewsSourceSeeder` 初始化源，资讯调度循环运行，价格 Worker 与调度器分别运行，原始值按保留期清理；进程与 API、Worker 独立，见 `deploy/docker-compose.prod.yml:75`。

### L6 客户端体验层

- **职责**：提供平台特定的用户/运营旅程，保留本地展示与会话状态，并把动作转换为 REST 契约。
- **实现**：Web 入口为 `web/src/main.tsx:11`；管理台入口为 `admin/src/app.tsx:14`；小程序与 RN App 分别在 `miniapp/src/app.ts:11` 和 `mobile/src/app.ts:17` 初始化共享客户端身份。Web 页面在 `web/src/pages`，管理台在 `admin/src/pages`，Taro 页面在 `miniapp/src/pages` 和 `mobile/src/pages`。
- **配置 schema**：Web 使用 `web/src/api/client.ts:12` 读取 `VITE_API_BASE`；Taro 使用 `shared/src/client.ts:8` 读取 `TARO_APP_API_BASE`；管理台经 `admin/nginx.conf:20` 与生产反代 `deploy/nginx/doggo.conf.template:29` 代理同源 `/api`。
- **专属协议/文案**：路由级页面文案挂载在各应用源码树；管理台操作文案与页面组件同处。浏览器协议是 JSON REST 加同源 Cookie，见 `web/src/api/client.ts:40`；Taro 协议增加 Bearer 和 `X-Doggo-Client`，见 `shared/src/client.ts:116`；Web 漏斗批在 `web/src/api/events.ts:123`，Taro 应用经 `shared/src/events.ts:92` 入队。四端没有共享单一 REST schema 文件。
- **状态/调用面**：Web 与管理台无持久 UI store，发同源 Cookie；localStorage 只存漏斗 session/channel/queue，见 `web/src/api/client.ts:88`。小程序与 RN 把令牌、缓存和漏斗队列存入 Taro storage，并可在 `shared/src/client.ts:30` 注入适配器；mobile 在 `mobile/src/storage.ts:8` 替换不可用的 RN 同步 storage。四端均经 HTTPS 调 L3；Admin 额外消费 `/api/admin/*`；客户端不直接调 Vapor。
- **生命周期**：分别构建和托管。Compose 以静态 Nginx 镜像运行 Web/管理台并依赖 API，见 `deploy/docker-compose.prod.yml:105`；小程序与 RN 独立打包，在应用启动时初始化。

### L7 运行面与共享客户端库层

- **职责**：提供数据库/缓存、迁移、代理与静态托管，以及小程序与 RN 复用的唯一 Taro 客户端实现。
- **实现**：MySQL/Redis 部署在 `deploy/docker-compose.prod.yml:7`；迁移任务在 `deploy/docker-compose.prod.yml:35`；Web/管理台代理与容器配置在 `deploy/nginx/doggo.conf.template:19`、`web/nginx.conf:1`、`admin/Dockerfile:1`。`@doggo/shared` 实现于 `shared/src/client.ts:8`、`shared/src/endpoints.ts` 和 `shared/src/events.ts:1`。
- **配置 schema**：数据库名与凭据来自 `deploy/docker-compose.prod.yml:1` 的环境变量；代理上游来自 `deploy/nginx/doggo.conf.template:29` 模板；共享客户端 API base 与 storage 行为由 L6 所述应用代码提供。
- **专属协议/文案**：相关协议是 MySQL 线协议、Redis 命令、HTTP 反代、容器编排和内部 TypeScript API。MySQL/Redis/Compose 没有用户文案；`@doggo/shared` 在 `shared/src/endpoints.ts` 保存端点请求/响应接口。
- **状态/调用面**：MySQL 与 Redis 持有自己的 volume；Nginx 无状态；Taro storage 是 L6 的持久化接缝。API/Scraper/Worker 调 MySQL 与 Redis；浏览器和 Taro 调 Nginx 或 L3；小程序/mobile 经 workspace link 引用共享代码，见 `miniapp/package.json` 与 `mobile/package.json`。
- **生命周期**：MySQL 与 Redis 使用命名 volume；迁移先运行一次；Nginx 容器依赖 API；共享代码编译进消费方 Taro bundle，而不是独立部署。

## 为什么这样拆

1. **把不变式与部署 I/O 分开**。领域类型和纯规则编译进所有服务端宿主，但不能绑定 EF、MySQL 或 ASP.NET Core。引用图可见：`server/src/Doggo.Domain/Doggo.Domain.csproj:1` 没有 PackageReference，而 `server/src/Doggo.Infrastructure/DoggoInfrastructureExtensions.cs:34` 引入 MySQL 与 Redis。
2. **让每个业务操作有唯一事务所有者**。应用服务持有 scoped `DoggoDbContext`、业务写入和审计副作用，而不是让端点各自发明事务行为。`server/src/Doggo.Infrastructure/Analytics/AnalyticsRecorder.cs:13` 明确加入调用方 SaveChanges 批次，`server/src/Doggo.Infrastructure/Orders/OrderService.cs:55` 持有订单状态迁移。
3. **让请求编排无状态并可独立扩容**。API 宿主不绑定持久队列或 UI store；简单投影直接读库，事务工作委托应用服务。扩 API 实例不会切分业务状态；`server/src/Doggo.Api/Program.cs:153` 显式检查两个后端。
4. **把周期工作从请求延迟与 API 可用性解耦**。行情任务、订单超时/对账、抽奖、金币过期、漏斗聚合、保留期清理都是注册于 `server/src/Doggo.Worker/Program.cs:48` 的 `BackgroundService`；移除 API 不影响补偿，部署也把 Worker 放成独立进程，见 `deploy/docker-compose.prod.yml:90`。
5. **隔离不可信源采集与合规**。重试、熔断、robots、超时和站点并发不同于普通请求处理。价格配置在 `server/src/Doggo.PriceCrawler/PriceCrawlerOptions.cs:71` 明确携带 retry/circuit/robots/site 策略；资讯源失败与熔断状态持久在 `server/src/Doggo.Scraper/News/ScraperScheduler.cs:47`。
6. **让客户端体验按平台特化，共享一条线上契约**。Web 用 Cookie，Taro 用 storage 加 Bearer，各端可选构建/运行时栈却调用同一 REST 面；`shared/src/client.ts:23` 明确注入 storage 是因 RN 行为不同。这样可以不把 React DOM、Ant Design Pro、微信与 RN 塞进一个运行时。
7. **让共享运行资源只在部署面拥有一次**。数据库/缓存凭据、迁移顺序、代理路由和容器依赖集中声明在 `deploy/docker-compose.prod.yml:35`；应用代码不发明环境相关连接或启动顺序。共享 Taro 客户端也优于复制请求/认证/漏斗行为，因为 `miniapp/src/api/endpoints.ts:5` 明确复用它。

## 术语

规范术语列表维护在[术语表](./glossary)。
