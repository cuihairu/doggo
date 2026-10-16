# 价格爬虫模块设计

[English](/en/design/price-crawler-design) | [简体中文](/design/price-crawler-design)

> 状态：已实现（2026-10-13，提交 0b08b2c）。本文为设计输入，实现期记录见 §11。
> 关联文档：`docs/design/overview.md`、`docs/design/data-model.md`。

## 1. 目标与非目标

### 目标

- 监控第三方网站上 CS2 / Dota2 相关商品的价格，形成可持续积累的外部价格序列。
- 输出趋势指标（日环比、周同比、区间涨跌），供前端展示与后台分析。
- 与站内行情（vapor → `MarketPriceSnapshot` / `MarketPriceDaily`）做对照，产出爬取价与站内价的价差报告。
- 模块独立：`Doggo.PriceCrawler` 为独立 classlib，可独立打包（NuGet），doggo 内以项目引用接入；站点适配器、调度、清洗、落库、频控、合规全部在模块内，不污染既有项目。

### 非目标

- 不抓取 Steam 官方社区市场行情。Steam 侧行情由 vapor 推送、`MarketPriceSnapshot` 承接，本模块只抓"其他网站"。
- 不做自动比价调价，不回写、不覆盖 `MarketPriceSnapshot` / `MarketPriceDaily`；爬取数据只读对照。
- 不做交易、下单、挂单、库存同步。
- 不重写 `Doggo.Scraper`，不改资讯管道；只复用其骨架（见 2.3）。
- 不做通用爬虫框架；站点适配器按站点逐个实现。
- 不替代既有趋势链路（`MarketPriceDaily` OHLC），趋势指标只基于外部爬取序列计算。

## 2. 模块边界与形态

### 2.1 形态

| 项 | 决策 |
| --- | --- |
| 项目 | 新建 `server/src/Doggo.PriceCrawler/`，.NET classlib（`Microsoft.NET.Sdk`） |
| 模块内容 | 适配器接口与实现、调度器、robots 检查、清洗管道、换算与聚合查询、admin endpoint 扩展方法、`PriceCrawlerOptions` |
| 依赖方向 | `Doggo.PriceCrawler` → `Doggo.Domain`（+ AngleSharp，HTML 解析用）；不直接依赖 `Doggo.Infrastructure` |
| 落库方式 | 模块内定义仓储接口（映射/序列/查询），由 `Doggo.Infrastructure` 实现并注册（与 `MarketStore` 同层） |
| 实体位置 | `Doggo.Domain/Price/`（与 `Market` 平级），配置与迁移在 `Doggo.Infrastructure` |

理由：打包边界干净，将来独立成项目时不背 EF/MySql 依赖；在 doggo 内只是多一行注册。

### 2.2 宿主接入建议

建议：一期并入 `Doggo.Scraper` 进程。

| 方案 | 做法 | 优点 | 代价 |
| --- | --- | --- | --- |
| A 并入 Scraper | `Doggo.Scraper` 引用 `Doggo.PriceCrawler`，`Program.cs` 多注册一个 HostedService + 配置节 + HttpClient | 复用 Hosting 骨架/配置/DB 注入/日志；零新增部署单元；`Dockerfile.worker` 已按 PROJECT 参数化，不改 | 同进程共享 CPU/带宽/内存 |
| B 独立 Worker | 新建 `Doggo.PriceCrawler.Worker`，`Dockerfile.worker` 加第三个 PROJECT | 进程隔离、独立扩缩容、故障不互相传导 | 多一份部署、配置、监控；一期站点少，性价比低 |

推荐 A：一期站点数量少、抓取频率低，独立进程的运维成本大于隔离收益；资讯与价格互不依赖（各自 BackgroundService、各自 scope、独立 HttpClient 与限流），同进程风险可控。

拆离路径：`Doggo.PriceCrawler` 不依赖 `Doggo.Scraper` 任何类型，接口与实体独立；将来拆独立 Worker 只需新建一个入口项目（拷贝 `Program.cs` 模式）+ `Dockerfile.worker` 增加一个 PROJECT，模块代码不动。

### 2.3 复用清单（Doggo.Scraper）

| 复用对象 | 来源 | 复用方式 |
| --- | --- | --- |
| Worker 骨架 | `ScraperWorker.cs` | 同模式新建 `PriceCrawlerWorker`（BackgroundService + 每轮 scope） |
| 配置绑定 | `ScraperOptions` + `Program.cs` | 同模式新建 `PriceCrawlerOptions`，`Bind("PriceCrawler")` |
| 抓取抽象 | `IPageFetcher` / `HttpPageFetcher` | 直接注入复用；超时/重试逻辑已在其中 |
| Playwright 抓取 | `PlaywrightPageFetcher` | 待拍板（见 10.4）；启用则注册到 crawler |
| 适配器注册表 | `SourceAdapterRegistry` | 同模式新建 `SiteAdapterRegistry`（`IEnumerable` 注入 + 字典查找） |
| 熔断/失败计数思路 | `ScraperScheduler` | 同思路在新调度器实现；失败计数存自己的表 |
| 指纹去重思路 | `NewsFingerprint` | 模式参考；价格去重新建（窗口语义不同） |
| EF/审计约定 | `DoggoDbContext` | 新实体沿用 Guid v7 + `CreatedAtUtc`/`UpdatedAtUtc` + varchar(255) |
| Dockerfile.worker | PROJECT 参数 | 一期不改；拆离时加 PROJECT |

不直接复用：`HtmlSanitizer`（资讯正文清洗，价格场景不需要）；`AngleSharp` 的用法模式可参考 `HtmlSourceAdapter`（CSS selector + ConfigJson），但价格解析按站点定制。

### 2.4 新建清单（Doggo.PriceCrawler）

| 组件 | 职责 |
| --- | --- |
| `ISiteAdapter` + 站点适配器 | 每站点一个实现：发现条目、抓取价格、唯一键、币种、限速建议 |
| `PriceCrawlerWorker` / Scheduler | 每站点独立循环、错峰、全局并发闸、失败退避、熔断 |
| `RobotsChecker` + 缓存 | robots.txt 抓取/解析/缓存/Disallow 判定/Crawl-delay |
| `PriceFingerprint` | 页面指纹与去重键（URL 归一化 + 窗口键，参考 `NewsFingerprint` 模式） |
| 清洗管道 | 解析校验、币种归一、窗口去重、异常值标记、换算落库 |
| `ExchangeRateReader` | 只读 `ExchangeRate`，换算 CNY（见 4.6） |
| `PriceCrawlerQueries` | 序列查询、价差报告、趋势计算（查询时算） |
| `PriceCrawlerAdminEndpoints` | admin 查询/确认接口（endpoint 扩展方法） |
| `PriceCrawlerOptions` + 配置节 | 见第 9 章 |

### 2.5 与既有链路的边界

| 链路 | 边界 |
| --- | --- |
| vapor → `MarketPriceSnapshot` | 不动；Steam 官方行情唯一来源 |
| `MarketPriceDaily` | 只读，作为价差对照的站内基准 |
| `Doggo.Scraper` 资讯 | 互不依赖；同进程但各自 HostedService/scope/HttpClient |
| `Doggo.Api` | 只加 admin 查询注册；不改既有接口 |

## 3. 站点适配器模型

### 3.1 接口草图

```csharp
namespace Doggo.PriceCrawler;

public interface ISiteAdapter
{
    string SiteKey { get; }                       // 站点标识，如 "example-market"
    IReadOnlyList<string> SupportedCurrencyCodes { get; } // 站点价格币种（ISO 4217）
    SiteRateLimit SuggestedRateLimit { get; }     // 适配器建议的间隔/并发/时段

    // 发现：按查询条件找出站点上的外部条目（站内已有 MarketItem 驱动）
    Task<IReadOnlyList<ExternalItemRef>> DiscoverAsync(DiscoveryQuery query, CancellationToken ct);

    // 抓取：按外部标识批量取当前报价
    Task<IReadOnlyList<ExternalPriceFetch>> FetchPricesAsync(IReadOnlyList<string> externalIds, CancellationToken ct);
}

public sealed record SiteRateLimit(int MinIntervalSeconds, int MaxConcurrent, string? AllowedWindowUtc);

public sealed record DiscoveryQuery(string? MarketHashName, string? ClassId, string? Keyword);

public sealed record ExternalItemRef(string ExternalId, string Title, string DetailUrl);

public sealed record ExternalPriceFetch(string ExternalId, long PriceCents, string CurrencyCode, DateTimeOffset ObservedAtUtc);
```

约定：

- `ExternalId` 是站点商品在适配器内的唯一标识（URL slug / 商品 id 等），同一站点内稳定；跨站点不可比。
- `PriceCents` 为站点币种的最小单位分；币种由 `CurrencyCode` 声明，适配器不得隐式换算。
- `ObservedAtUtc` 是适配器观察到该价格的 UTC 时间（页面标注时间优先，否则抓取时间）。
- 抓取失败/条目缺失：适配器返回空集或跳过该条目，不抛异常（网络异常除外）；调度器按结果记 HTTP 状态。

### 3.2 适配器注册与发现

- 注册：`builder.Services.AddSingleton<ISiteAdapter, XxxSiteAdapter>()`（每站点一个），调度器注入 `IEnumerable<ISiteAdapter>` 构建 `SiteAdapterRegistry`（仿 `SourceAdapterRegistry`）。
- 站点清单来自配置 `PriceCrawler:Sites`（Key/Enabled/IntervalMinutes/MaxItemsPerRun 等）；未配置或未启用的站点即使适配器已注册也不调度。
- 适配器实现内部：HTML 站用 AngleSharp 解析（参考 `HtmlSourceAdapter` 的 CSS selector + ConfigJson 模式）；JSON API 站直接反序列化；渲染型站点经 `IPageFetcher`（Playwright，待拍板）。
- 适配器版本号（SemVer）随 `AdapterVersion` 字段落库，便于回溯解析逻辑变更。

### 3.3 发现与抓取流程

```
发现（低频，默认每日一次，可配置）：
  站内 MarketItem（可按 game/白名单筛选）→ 调 DiscoverAsync → 新 ExternalId 写 PriceCrawlItemMap（Unconfirmed）

抓取（按站点间隔，默认 3600s）：
  取该站点 Unconfirmed + Confirmed 映射（限 MaxItemsPerRun）→ FetchPricesAsync → 原始记录 → 清洗管道 → 序列
```

## 4. 数据模型

### 4.1 实体总览

| 实体 | 用途 |
| --- | --- |
| `PriceCrawlItemMap` | 外部条目 ↔ 站内 MarketItemId 映射，含人工确认状态 |
| `PriceCrawlRaw` | 原始抓取记录（原始价、币种、HTTP 状态、页面指纹） |
| `PriceCrawlSeries` | 清洗后价格序列（原始价 + CNY 换算价，窗口去重，异常标记） |
| `PriceCrawlTrend`（可选） | 日级趋势指标物化表（默认不启用，见 4.5） |

通用约定（沿用 `DoggoDbContext`）：主键 Guid v7 应用侧生成；`CreatedAtUtc`/`UpdatedAtUtc` 由 `SaveChanges` 自动覆盖；string 默认 varchar(255)；时间 UTC。实体放 `Doggo.Domain/Price/`，配置与迁移在 `Doggo.Infrastructure`（`ApplyConfigurationsFromAssembly` 自动应用 `IEntityTypeConfiguration`）。

### 4.2 PriceCrawlItemMap（外部商品映射）

| 字段 | 类型 | 含义 | 索引/约束 |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| SiteKey | string(32) | 站点标识 | UQ(SiteKey, ExternalId) |
| ExternalId | string(255) | 站点商品唯一标识 | UQ(SiteKey, ExternalId) |
| ExternalTitle | string(255) | 站点商品名（冗余快照） | |
| ExternalUrl | string(2048) | 详情页 | |
| MarketItemId | Guid? | 站内商品；未确认 null | IX(MarketItemId) |
| MapStatus | int | 0 Unconfirmed / 1 Confirmed / 2 Rejected | IX(MapStatus) |
| MatchSource | int | 0 Auto（发现）/ 1 Manual（人工绑定） | |
| CreatedAtUtc / UpdatedAtUtc | DateTime | 审计自动 | |

说明：唯一约束 (SiteKey, ExternalId) 防止同一站点同商品重复建映射；MarketItemId 可空，确认后回填。

### 4.3 PriceCrawlRaw（原始抓取记录）

| 字段 | 类型 | 含义 | 索引/约束 |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| SiteKey | string(32) | 站点标识 | IX(SiteKey, ExternalId, ObservedAtUtc) |
| ExternalId | string(255) | 条目标识 | 同上 |
| PriceCents | long? | 解析后的原始价（站点币种，分）；解析失败 null | |
| CurrencyCode | string(8) | ISO 币种 | |
| RawPriceText | string(64) | 页面原始价文本（含符号，便于回溯） | |
| PageFingerprint | string(64) | 归一化页面 SHA-256（`PriceFingerprint`） | |
| HttpStatusCode | int | 最近一次请求状态码 | |
| ObservedAtUtc | DateTime | 观察时间 | 同上 |
| FetchedAtUtc | DateTime | 抓取完成时间（排重用） | |
| AdapterVersion | string(32) | 适配器版本 | |

说明：原始记录只增不改，用于回溯与对账；保留期见待拍板 10.8。

### 4.4 PriceCrawlSeries（清洗后价格序列）

| 字段 | 类型 | 含义 | 索引/约束 |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| MapId | Guid | 关联映射 | IX(MapId) |
| MarketItemId | Guid | 站内商品（仅 Confirmed 映射落序列） | IX(MarketItemId, ObservedAtUtc) |
| SiteKey | string(32) | 站点标识 | UQ(SiteKey, ExternalId, WindowKey) |
| ExternalId | string(255) | 条目标识 | 同上 |
| PriceCents | long | 清洗后原始价（站点币种，分） | |
| CurrencyCode | string(8) | ISO 币种 | |
| ConvertedCents | long? | 换算 CNY 价；汇率缺失 null | |
| ExchangeRate | decimal? | 所用汇率（原值留存） | |
| ObservedAtUtc | DateTime | 观察时间（窗口对齐） | |
| WindowKey | string(32) | 去重窗口键（yyyyMMddHHmm，按 DedupWindowMinutes 对齐） | UQ(SiteKey, ExternalId, WindowKey) |
| IsSuspect | bool | 异常值标记（偏离滚动中位数） | |
| CreatedAtUtc | DateTime | 审计自动 | |

去重口径：UQ(SiteKey, ExternalId, WindowKey) 保证同一条目同一窗口只有一条，"取最后值"由清洗管道保证（见第 6 章）。

### 4.5 PriceCrawlTrend（趋势指标，可选物化）

| 字段 | 类型 | 含义 | 索引/约束 |
| --- | --- | --- | --- |
| Id | Guid | PK | PK |
| MarketItemId | Guid | 站内商品 | UQ(MarketItemId, SiteKey, Date) |
| SiteKey | string(32) | 站点 | 同上 |
| Date | DateOnly | UTC 日 | 同上 |
| CloseCents | long | 当日最后清洗价（CNY 换算后；无汇率则原币） | |
| DayOverDayPct | decimal? | 日环比 | |
| WeekOverWeekPct | decimal? | 周同比 | |
| PeriodChangePct | decimal? | 区间涨跌（近 7/30 日，查询参数决定） | |
| ComputedAtUtc | DateTime | 计算时间 | |

物化 vs 查询时算：默认**查询时算**（数据量小、口径可随时调整、无额外写入）。当序列规模增长（站点 × 条目 × 日频）或 admin 高频查询时，启用此表做日级缓存，每日收盘后批量刷新。是否物化见待拍板 10.3。

### 4.6 与 ExchangeRates 的关系

- `ExchangeRate`（CurrencyCode, Rate, Source）为既有实体，语义"多币种汇率（展示用）"。本模块**只读**，不写入、不刷新（刷新链路若有则另行设计）。
- 换算方向约定：1 单位 CurrencyCode = Rate CNY（待拍板 10.6 确认；若既有数据方向相反则改乘为除）。
- ConvertedCents = round(PriceCents × Rate)；汇率缺该币种 → ConvertedCents = null、ExchangeRate = null，序列保留原币值，不阻断。
- 汇率快照留存：`ExchangeRate` 字段记录所用原值，便于历史回溯（当前汇率可能变化）。

### 4.7 注册与迁移

- 实体注册：`DoggoDbContext` 增加 `DbSet<PriceCrawlItemMap> PriceCrawlItemMaps` 等四行（与其他域一致）。
- 配置：`IEntityTypeConfiguration` 放 `Doggo.Infrastructure/Persistence/`（`ApplyConfigurationsFromAssembly` 自动应用）；唯一索引与索引在配置中声明。
- 迁移：在 `Doggo.Infrastructure` 的 Migrations 目录追加（沿用既有迁移流程）。

## 5. 调度与频控

### 5.1 调度模型

| 项 | 设计 |
| --- | --- |
| 模型 | 每站点一个独立异步循环（站点队列），站点内串行，站点间受全局并发闸限制 |
| 站点间隔 | 每站点独立 IntervalMinutes（默认 60），配置项 |
| 全局并发 | SemaphoreSlim 跨站点共享，默认 8（配置项） |
| 错峰 | 首次唤醒延迟 = 站点索引 × 分散秒数 + 随机抖动，避免整点并发 |
| 发现节奏 | 每站点每日一次（默认 04:00 UTC 前后，可配置），与抓取循环解耦 |
| 站点级熔断 | 连续失败 ≥ 阈值 → 冷却期内跳过（见 5.2） |
| 优雅退出 | CancellationToken 贯通；每轮 scope 独立，异常不扩散到其他站点 |

### 5.2 失败退避（指数 + 抖动）

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| MaxAttempts | 3 | 单次任务最大尝试次数 |
| BaseDelaySeconds | 5 | 退避基数 |
| Factor | 2 | 指数因子：第 n 次失败后延迟 = Base × 2^n |
| CapSeconds | 1800 | 最大退避 30 分钟 |
| JitterRatio | 0.25 | 抖动比例：最终延迟 = min(Cap, Base × 2^n) × (1 + rand × 0.25) |
| CircuitFailureThreshold | 10 | 连续失败熔断阈值 |
| CircuitCooldownMinutes | 60 | 熔断冷却时间 |
| 成功清零 | — | 任一次成功 → 连续失败计数清零 |

说明：退避在站点级生效（站点休眠），不影响其他站点；退避与 5.1 的间隔叠加（取较大者）。

### 5.3 robots.txt 检查

流程：

1. 首次访问某 host：GET /robots.txt，解析 Disallow / Crawl-delay / Sitemap。
2. 缓存：IMemoryCache（或 Redis）按 host 缓存，TTL 默认 24h（配置项 `Robots:CacheHours`）。
3. 抓取前逐 URL 判定：路径前缀命中 Disallow → 跳过并计数（不请求）。
4. Crawl-delay 声明值 > 站点配置间隔 → 取较大值。
5. robots.txt 获取失败：默认 fail-open（视为无限制）+ 日志；可配置 fail-close。
6. 逐站点启用开关（`PriceCrawler:Sites:RobotsEnabled`），默认开；站点启用前需人工确认合规（待拍板 10.9）。

### 5.4 UA 声明与抓取时段

- UA：`DoggoPriceBot/1.0 (+https://<站点域名>/price-crawler; contact: <管理邮箱>)`，可配置；不伪装成浏览器。
- 时段窗口：默认不限制；可选 NightWindowUtc（如 02:00–06:00）暂停抓取，避开目标站点高峰。窗口内站点循环休眠。

## 6. 清洗去重口径

| 步骤 | 规则 |
| --- | --- |
| 解析校验 | PriceCents 必须 > 0 且 ≤ 上限（默认 10,000,000,000 分，配置项）；否则视为解析失败，不进入序列，计入原始记录的 HTTP 状态/错误 |
| 币种归一 | 站点声明币种 → ISO 4217 大写三字母；未知/缺失 → 默认丢弃并计数（待拍板 10.6 可改保留原值） |
| 窗口去重 | 同 (SiteKey, ExternalId) 按 DedupWindowMinutes（默认 60）对齐 WindowKey，窗口内取最后一条（FetchedAtUtc 最大） |
| 异常值 | 滚动中位数（过去 N=20 个清洗点）偏离 ±SuspectThresholdPct（默认 50%）→ IsSuspect=true，保留不丢弃；价差报告默认排除，查询可含 |
| 换算 | ConvertedCents = round(PriceCents × Rate)（汇率存在时）；缺失 → null |
| 跨站点 | 不合并：不同站点是不同行，对照按站点维度分别进行 |
| 映射门控 | 仅 MapStatus=Confirmed 的映射进入序列（MarketItemId）；Unconfirmed 只落原始记录与映射，供人工确认时参考历史 |

## 7. 聚合分析口径

### 7.1 对照维度与基准

| 维度 | 口径 |
| --- | --- |
| 条目 | 同 MarketItemId（映射 Confirmed） |
| 币种 | 统一 CNY：爬取价用 ConvertedCents；站内基准用 `MarketPriceDaily.CloseCents`（既有口径，平台币种 CNY） |
| 时间 | UTC 日对齐：每日取爬取序列最后一条（IsSuspect=false）的 ConvertedCents 作为日爬取价，与当日 Daily 对齐 |
| 缺失 | 某日无爬取点 → 该日无对照；Daily 缺失（vapor 未覆盖该 item）→ 标记"无站内基准"，不计价差 |

### 7.2 价差指标

| 指标 | 定义 |
| --- | --- |
| 绝对价差 SpreadCents | 爬取价 − 站内价（分，CNY） |
| 百分比价差 SpreadPct | (爬取价 − 站内价) ÷ 站内价 × 100，站内价为 0 时 null |
| 趋势指标（外部序列自身） | 日环比 = (今收 − 昨收) ÷ 昨收 × 100；周同比 = (今收 − 7 天前收) ÷ 7 天前收 × 100；区间涨跌 = (区间末 − 区间初) ÷ 区间初 × 100（默认近 7 日，可参数化） |

### 7.3 查询示例（草图）

1. 价差报告（EF，按 UTC 日 join `MarketPriceDaily`）：

```csharp
var rows = await db.PriceCrawlSeries
    .Where(s => !s.IsSuspect && s.ObservedAtUtc >= from && s.ObservedAtUtc < to)
    .GroupBy(s => new { s.MarketItemId, Day = DateOnly.FromDateTime(s.ObservedAtUtc) })
    .Select(g => new { g.Key.MarketItemId, g.Key.Day,
                      CrawlCents = g.OrderByDescending(s => s.ObservedAtUtc).First().ConvertedCents })
    .ToListAsync(ct);

var report = rows.Select(r => new
{
    r.MarketItemId,
    r.Day,
    StationCents = db.MarketPriceDailies
        .Where(d => d.MarketItemId == r.MarketItemId && d.Date == r.Day)
        .Select(d => (long?)d.CloseCents).FirstOrDefault(),
    r.CrawlCents
}).Select(x => new
{
    x.MarketItemId, x.Day, x.StationCents, x.CrawlCents,
    SpreadCents = x.CrawlCents - x.StationCents,
    SpreadPct = x.StationCents == 0 ? (decimal?)null
        : (x.CrawlCents - x.StationCents) * 100m / x.StationCents
}).ToList();
```

（实际实现走模块内 `PriceCrawlerQueries`，避免在 Api 层拼 EF；此处为口径示意。）

2. 某 item 外部价格序列（按站点分组）：

```csharp
var points = await db.PriceCrawlSeries
    .Where(s => s.MarketItemId == marketItemId && s.ObservedAtUtc >= from && s.ObservedAtUtc < to)
    .OrderBy(s => s.ObservedAtUtc)
    .Select(s => new { s.SiteKey, s.ObservedAtUtc, s.PriceCents, s.CurrencyCode, s.ConvertedCents, s.IsSuspect })
    .ToListAsync(ct);
```

## 8. 接口面（admin 草图）

路由组 `/api/admin/price-crawl`（沿用 Admin 策略）。已实现（maps 列表与审阅、prices 序列、spread-report 比价），鉴权沿用 Admin 策略。

```
GET /api/admin/price-crawl/maps?siteKey=&mapStatus=&page=&pageSize=
→ { total, items: [ { mapId, siteKey, externalId, externalTitle, externalUrl,
                     marketItemId, marketHashName, mapStatus, matchSource, updatedAtUtc } ] }

GET /api/admin/price-crawl/prices/{marketItemId}?from=&to=&includeSuspect=
→ { marketItemId, points: [ { siteKey, observedAtUtc, priceCents, currencyCode,
                              convertedCents, isSuspect } ] }

GET /api/admin/price-crawl/spread-report?from=&to=&siteKey=
→ { rows: [ { marketItemId, marketHashName, date, crawlCents, stationCents,
             spreadCents, spreadPct } ] }

POST /api/admin/price-crawl/maps/{mapId}/confirm   { } → 200 { mapId, mapStatus: "Confirmed" }
POST /api/admin/price-crawl/maps/{mapId}/reject    { } → 200 { mapId, mapStatus: "Rejected" }
```

说明：endpoint 扩展方法 `MapPriceCrawlerAdminEndpoints(this IEndpointRouteBuilder)` 放 `Doggo.PriceCrawler`；`Doggo.Api` 在 AdminEndpoints 组内一行注册（或独立组）。鉴权沿用 Admin policy。

## 9. 配置项清单

```jsonc
{
  "PriceCrawler": {
    "Enabled": true,                    // 模块总开关
    "ScanIntervalSeconds": 300,         // 调度器重新检查间隔
    "GlobalConcurrentFetches": 8,        // 全局并发上限（跨站点 SemaphoreSlim）
    "FetchTimeoutSeconds": 60,          // 单次抓取超时
    "MaxItemsPerRun": 200,              // 每站点每次抓取条目数上限
    "Retry": {
      "MaxAttempts": 3, "BaseDelaySeconds": 5, "Factor": 2,
      "CapSeconds": 1800, "JitterRatio": 0.25
    },
    "CircuitBreaker": { "FailureThreshold": 10, "CooldownMinutes": 60 },
    "Robots": { "Enabled": true, "CacheHours": 24, "FailOpen": true },
    "UserAgent": "DoggoPriceBot/1.0 (+https://<host>/price-crawler)",
    "NightWindowUtc": { "Enabled": false, "Start": "02:00", "End": "06:00" },
    "Discover": { "IntervalHours": 24, "RunAtUtc": "04:00" },
    "DedupWindowMinutes": 60,
    "SuspectThresholdPct": 50,
    "RollingMedianPoints": 20,
    "MaxPriceCents": 10000000000,
    "Sites": [
      { "Key": "example-market", "Enabled": true, "IntervalMinutes": 60, "RobotsEnabled": true }
    ],
    "SeedDefaultSites": true
  }
}
```

## 10. 待拍板项

| # | 事项 | 默认建议 | 决策人 |
| --- | --- | --- | --- |
| 1 | 目标站点清单（首批站点、URL、API/HTML 形态） | 用户给出后定首批适配器 | 用户 |
| 2 | 映射是否需人工审核后才纳入价差报告 | 仅 Confirmed 纳入；Unconfirmed 只积累数据 | 用户 |
| 3 | 趋势指标物化 vs 查询时算 | 查询时算；规模增长后再物化 | 用户 |
| 4 | 是否允许 Playwright 渲染型抓取 | 默认不允许（仅 HttpClient）；站点需要再议 | 用户 |
| 5 | 宿主形态：并入 Scraper vs 独立 Worker | 一期并入 Doggo.Scraper 进程 | 用户 |
| 6 | ExchangeRate 方向约定与缺失汇率/未知币种处理 | 1 外币 = Rate CNY；缺失不换算、保留原值；未知币种丢弃计数 | 用户 |
| 7 | 抓取频率与夜间窗口 | 站点默认 60min；夜间窗口默认关 | 用户 |
| 8 | 数据保留策略（原始记录保留天数、序列保留、清理任务） | 原始 90 天、序列永久；后补清理任务 | 用户 |
| 9 | 目标站点合规授权（robots/ToS 是否允许价格抓取） | 逐站点人工确认后才启用 | 用户 |
| 10 | 首批监控范围（全量 MarketItem vs 白名单） | 按 game 全量 + 配置白名单 | 用户 |

## 11. 实现状态（2026-10-13，提交 0b08b2c）

待拍板 2-10 已按默认建议落地：仅 Confirmed 映射进序列与比价；趋势查询时算、未物化；未启用 Playwright（仅 HttpClient + AngleSharp）；宿主并入 Scraper 进程；汇率方向 1 外币 = Rate CNY，缺失不换算保留原值、未知币种丢弃计数；站点默认 60 分钟间隔、夜间窗口已实现；原始行 90 天批量清理；robots 门禁默认开；监控按 game 全量 + 配置白名单。1（目标站点清单）仍待用户给出，站点适配器为配置驱动，配置即接入。

实体落库为 ItemMap / Raw / Series / SiteState 四表（设计草表中的 Trend 物化表未建，趋势查询时算）；比价对照走 `RecentCompareCentsAsync` 滚动 20 点做疑点判定。模块 81 例 + Api 5 例测试先写后绿。

## 12. 前台暴露口径（2026-10-16 代决，用户可推翻）

前台只暴露聚合口径，不暴露逐站点明细：

| 项 | 口径 |
| --- | --- |
| 数值 | 近 7 天内每个已确认站点最新一条非疑点、已换算（CNY）的爬取价，跨站点取中位数 |
| 排除 | `IsSuspect=true` 与 `ConvertedCents` 为空（缺汇率）的观测点 |
| 站内基准 | `MarketPriceDaily` 最新收盘（§7.1） |
| 价差 | (外部中位数 − 站内收盘) ÷ 站内收盘 × 100；站内收盘为 0 或无外部数据时为 null |
| 展示位 | web 行情趋势页；列表页不加列，mobile / miniapp 一期不加 |
| 未覆盖 | 站点未接入或该 item 无序列时字段为 null，页面不渲染对照块 |

理由：疑点行与缺汇率的原值不能直接对外；聚合中位数是可直接引用的单一数；逐站点报价表的转发面留在 admin；用户判断「站内价是否合理」只需一个基准。实现不改写 `MarketPriceSnapshot` / `MarketPriceDaily`（§1 只读对照约束），不新增表、不物化（§10-3 默认查询时算）。

实现为 `GET /api/market/items/{id}/reference`（MarketEndpoints）：中位数与价差、疑点与未换算排除、无序列返回 null、未知 item 404 共 4 例测试。
