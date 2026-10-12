# TODO

> 任务按里程碑组织,勾选表示完成。目标架构见 [docs/design/overview.md](docs/design/overview.md),数据模型见 [docs/design/data-model.md](docs/design/data-model.md),开箱设计见 [docs/design/case-opening.md](docs/design/case-opening.md)。

## M0 项目骨架与基础设施

### 仓库工程化
- [x] README(中英双语)
- [x] VitePress 文档站点
- [x] 功能分析与设计文档
- [x] `.editorconfig`(C# + TypeScript 统一代码风格)
- [x] `Directory.Build.props`(统一 .NET 版本号、nullable、analyzers)
- [x] gitignore 复核(根级已覆盖 bin/obj,无需补充)
- [x] GitHub Actions CI:server `dotnet build` + `dotnet test` + Codecov 上传 + 文档构建
- [x] README 增加 CI 与 Codecov 徽章
- [x] PR 模板(`.github/PULL_REQUEST_TEMPLATE.md`)
- [x] 分支保护(main:禁强推/禁删除 + 线性历史;不强制 PR,enforce_admins 关闭,保留 owner 直推工作流)

### server 解决方案骨架
- [x] `server/` 下创建 `Doggo.slnx`(SDK 10 默认新格式)
- [x] `src/Doggo.Domain` 类库(无外部依赖)
- [x] `src/Doggo.Infrastructure` 类库(引用 Domain;EF Core + MySQL + Redis)
- [x] `src/Doggo.Api` Web 项目(引用 Infrastructure;Minimal API)
- [x] `src/Doggo.Scraper` Worker Service(引用 Infrastructure)
- [x] `src/Doggo.Worker` Worker Service(引用 Infrastructure)
- [x] `tests/Doggo.Domain.Tests` 测试项目(xUnit)
- [x] 项目间引用关系复核:Api/Scraper/Worker → Infra → Domain
- [x] `dotnet build server/Doggo.slnx` 全绿
- [x] Api 暴露 `/healthz` 健康检查(MySQL + Redis 探活)
- [x] 各项目基础 `appsettings.json` + `appsettings.Development.json`
- [x] MySQL provider:MySql.EntityFrameworkCore 10.0.9(Pomelo 尚无 EF10 版,注册点已收敛可切换)

### 本地开发环境
- [x] `deploy/docker-compose.yml`(mysql + redis,带健康检查)
- [x] `deploy/` 下提供 `.env.example`
- [x] EF Core 连接配置与启动迁移验证(InitialCreate 已应用)
- [x] Redis 连接配置验证

## M1 领域模型与数据层

### 用户域
- [x] UserAccount 实体(注册凭据、昵称、头像、邀请码、状态)
- [x] UserSteamProfile / UserTradeUrl / UserSteamBackupCode 实体
- [x] UserCoin / UserCoinLog 实体(含批次过期字段,UserCoinBatch 按 FIFO 过期)
- [x] UserCoupon / Coupon / RedeemCode 实体
- [x] UserLoginLog / UserVisit / InviteRewardLog / SignRecord 实体

### 商品域
- [x] Product 实体(类型、classId、多级价格字段)
- [x] CasePlan / CasePlanOption 实体(加价率、返现、数量档位)
- [x] PromotionPlan / QuantityOption 实体
- [x] CaseContent 实体(七档品质)
- [x] Recommend / ExchangeRate 实体

### 订单域
- [x] UserOrder 实体(入口、价格四件套、抵扣字段、状态、serviceMode)
- [x] PaymentRecord / RefundRecord 实体

### 开箱域
- [x] GroupBuying / GroupBuyingSeat 实体(乐观锁版本)
- [x] UserPrize / UserPrizeLog 实体(饰品完整字段)
- [x] RollRoom / RollRoomTemplate / RollRoomPrize / RollRoomParticipant 实体

### 资讯域
- [x] NewsSource / NewsItem 实体(指纹去重键)
- [x] Article / Notice / Banner / Partner 实体

### 行情域与系统域
- [x] MarketItem / MarketPriceSnapshot / MarketPriceDaily / SyncCursor 实体
- [x] AdminAccount / Role / Permission / RolePermission 实体
- [x] SiteSetting / SupportQrcode / ShortUrl / NotificationChannel 实体
- [x] 通用约定落地:应用侧主键生成(Guid v7,ValueGeneratedNever)
- [x] 通用约定落地:软删除过滤器、UTC 时间处理、SaveChanges 审计
- [x] 首个 EF 迁移生成并在本地库验证建表(56 张表)
- [x] 数据层行为测试:ID 生成 / 审计时间戳 / 软删除过滤(InMemory)

## M2 资讯聚合(自建爬虫,第一个垂直切片)

### 爬虫管道
- [x] Doggo.Scraper 通用抓取框架:调度(PeriodicTimer/Quartz)+ 站点注册表
- [x] HttpClient 抓取 + AngleSharp 解析的基础 SourceAdapter 抽象
- [x] 动态页 Playwright for .NET 的 SourceAdapter 实现
- [x] 条目清洗:HTML 白名单过滤、图片代理或转存策略
- [x] 指纹去重(标题+正文哈希)落库前判重
- [x] 失败重试与站点级熔断(连续失败暂停)
- [x] SyncCursor 游标记录(增量拉取)

### 数据源接入
- [x] CS2 官方公告源(Steam News RSS / 官方博客)
- [x] CS2 更新日志源
- [x] Dota 2 官方公告源
- [x] Dota 2 更新与版本改动源
- [x] 赛事资讯源(至少一个)

### 资讯 API
- [x] `GET /api/news`(分页、游戏/分类/标签过滤)
- [x] `GET /api/news/{id}`(详情,阅读计数)
- [x] 运营内容接口:`GET /api/notices`、`GET /api/banners`、`GET /api/articles/{id}`
- [x] 集成测试:抓取→清洗→去重→落库→API 输出

## M3 行情同步(vapor 集成)

- [x] vapor 客户端封装:`Doggo.Infrastructure/Vapor`(REST 调用 + API Key 配置)
- [x] Webhook 接收端点(验签、幂等)
- [x] MarketItem 资料同步任务(classId、市场名、图标)
- [x] MarketPriceSnapshot 价格快照同步任务
- [x] 按日聚合任务(快照 → 日线,供趋势图)
- [x] `GET /api/market/items`(饰品/箱子列表、搜索)
- [x] `GET /api/market/items/{id}/prices`(历史价格序列)
- [x] 汇率接口:`GET /api/exchange-rates`
- [x] vapor 不可用时的降级策略(读最近快照、任务退避)

## M4 商品与订单

### 商品 API
- [x] `GET /api/products`(箱子/钥匙列表、排序)
- [x] `GET /api/products/{id}`(详情 + 套餐选项 + 内容物展示)
- [x] `GET /api/products/{id}/skus`(按履约方式的库存与价格)
- [x] `GET /api/promotions`(限量促销)
- [x] `GET /api/recommends`(推荐位)
- [x] 价格计算服务:采购价/参考价/修正价/加价率/数量档位阶梯

### 订单状态机
- [x] 下单服务(装配价格、占库存/座位、支付期限)
- [x] 订单状态机实现(待支付→待结算→待取回→取回中→已完成;取消/退款)
- [x] 超时未支付取消任务
- [x] 支付抽象接口 `IPaymentProvider`(创建支付、回调验证、退款)
- [x] 回调幂等处理(状态条件更新)
- [x] 对账补偿任务(取回检查、状态回退)

### 交易链接与饰品取回
- [x] 用户交易链接 CRUD + 有效性校验接口
- [x] vapor trading action 发起取回
- [x] 取回结果回调处理(订单完成/回退)
- [x] 取回重试策略与错误记录

## M5 开箱系统

### 批次与座位
- [x] 批次创建(账号批次、座位数、乐观锁)
- [x] 占座服务(条件更新,售罄判定)
- [x] 开箱请求 → vapor CaseOpening action 提交
- [x] 开箱结果事件处理 → 生成 UserPrize + 推进座位状态
- [x] 座位状态机全链路测试(售卖中→…→完成)

### 奖品
- [x] 奖品记录查询 API(我的奖品列表/详情)
- [x] 奖品估价关联(行情价快照)
- [x] 开箱日志(UserPrizeLog)查询

### roll 房
- [x] AliasMethod 抽样算法实现 + 单元测试(权重分布验证)
- [x] 房间模板与规则配置(登录领取/每单参与/邀请码等)
- [x] 房间创建 → 进行中 → 开奖状态机
- [x] 开奖任务(到期检查 + 权重抽取)
- [x] 奖品发放(库存饰品走交易 / 积分入账)
- [x] 房间自动滚动下一期
- [x] `GET /api/roll/rooms`、`GET /api/roll/rooms/{id}` C 端接口

## M6 用户体系与营销

### 认证
- [x] 注册/登录(账号密码 + JWT 签发)
- [x] 刷新 token / 登出
- [x] Steam OpenID 登录绑定(绑定成功路径依赖真实 Steam 账号登录,拒绝路径已对 steamcommunity 实测)
- [x] 登录日志与访问日志中间件

### 积分与优惠
- [x] 积分账本服务(余额、FIFO 消费、退款回补)
- [x] 积分批次过期任务(已实测:回填过期批次后 worker 扫描清零并写 Expire 流水)
- [x] 兑换码核销(积分/优惠券两种)
- [x] 优惠券:领取、可用商品判定、下单抵扣、退款回补(已实测:领券下单抵扣、取消订单回补券和积分)
- [x] 邀请奖励:绑定邀请码、奖励入账(已实测:被邀请人订单完成后邀请人入账 500 积分)
- [x] 签到:自然周周期、补签规则(已实测:当日签到、重复 400、凭完成订单补签、同单复用/跨周期/未来日均 400)

## M7 客户端

### web/(React + TypeScript + Vite)
- [x] 脚手架初始化(Vite + TS + 路由 + 请求封装)
- [x] 设计系统基座:主题、布局、公共组件(资讯卡片/行情表/饰品卡)
- [x] 资讯列表/详情页
- [x] 行情列表/详情页(价格趋势图,SVG 折线)
- [x] 商品/箱子列表与详情页(SKU 选择下单)
- [x] 订单与支付流程页(模拟支付/取消/状态轮询)
- [x] 开箱结果展示与我的奖品页
- [x] roll 房页面(列表/详情/参与/开奖)
- [x] 用户中心(资料/交易链接/积分/优惠券/签到)
- [x] Steam 登录接入(入口跳转 API /api/auth/steam/login,绑定成功路径依赖真实 Steam 账号)

### admin/(Ant Design Pro)
- [x] 脚手架初始化(UmiJS + Pro)(@umijs/max + pro-components,pnpm build 全绿)
- [x] 登录与 RBAC 权限接入(role claim + Admin policy;非管理员 403 已实测)
- [x] 资讯管理:源管理、条目管理、文章/公告/Banner(五页签,源启停/公告上下线/Banner 下线)
- [x] 商品管理:商品/套餐/选项/促销/汇率(上架开关、促销启停、汇率人工修正;套餐/选项随爬虫导入,后台只读查看)
- [x] 订单管理:查询、退款、奖品查看(分页 + 退款弹窗 + 订单奖品列表)
- [x] 用户管理:账号、流水、登录日志(禁用开关 + 金币流水/登录日志抽屉)
- [x] roll 房管理:模板、房间、奖品配置(模板启停、房间列表、奖品池查看;奖品由房间创建流产生)
- [x] 数据统计:面板、走势、排行(计数卡 + 近 14 天走势 + 商品营收排行)
- [x] 系统设置:站点配置、权限、IP 白名单(按设计落地:配置走 deploy/.env + compose 环境变量;权限即 IsAdmin 标记见用户管理;IP 白名单在 deploy/nginx/doggo.conf.template 的 admin 段,不设在线修改入口)

### miniapp/(Taro + React + TypeScript)
- [x] Taro 脚手架初始化(微信小程序)(Taro 4.3 + React 18,webpack 锁 5.94.0,`taro build --type weapp` 通过)
- [x] 登录(wx.login 换 JWT)(对接 `POST /api/auth/wechat`;真机/devtools 联调需正式 AppID/Secret,外部依赖)
- [x] 资讯/行情核心页(首页资讯流 + 行情页 K 线走势)
- [x] 商品与开箱结果页(商品列表/详情下单 + 我的奖品)
- [x] 用户中心页(资料/金币/优惠券/每周签到)
- [x] 提审合规自查(类目、隐私协议)(见 `docs/guide/miniapp-compliance.md`;主体资质与正式域名属外部依赖)

### mobile/(Taro + React + TypeScript)
- [x] Taro RN 端初始化,与 miniapp 共享层抽包(`shared/` 源码包供两端复用,miniapp re-export 零改页面;`mobile/` metro bundle 构建通过)
- [x] 核心页面适配(RN 差异处理)(账密登录替代 wx.login、RN 原生组件+暗色主题、订单页本地单号查询;metro bundle 含全部 9 页)
- [x] 打包流水线(Android APK 优先)(mobile.yml:pnpm install → taro build:rn → expo prebuild → gradlew assembleRelease;run 38163196751 出 app-release.apk;踩坑三条:expo 从项目根解析 @react-native/assets-registry/expo-asset 需升为直接依赖、babel-preset-taro rn 链三个 babel 插件同因、模板 splashscreen.xml 引用的 color/splashscreen_background 需 app.json splash 配置注入)

## M8 部署与运维

- [x] `server/src/Doggo.Api` Dockerfile(多阶段 + migrator target,镜像实测可启动)
- [x] Scraper / Worker Dockerfile(一个 `Dockerfile.worker` 以 `PROJECT` 参数出两个镜像)
- [x] `web/` 构建产物 nginx 容器或静态部署(web/Dockerfile,SPA fallback + /api 反代)
- [x] `admin/` 构建产物部署配置(admin/Dockerfile + nginx.conf)
- [x] `deploy/docker-compose.prod.yml`(8 服务全栈,migrator 先行;本机全栈实跑五项验收通过)
- [x] nginx 配置模板(deploy/nginx/doggo.conf.template,域名/HTTPS/反代/admin IP 白名单)
- [x] GitHub Actions 发布流水线(并入 daily.yml:GHCR nightly 镜像 + DEPLOY_ENABLED 门控部署;不发正式版)
- [x] 部署文档(docs/guide/deployment.md)
- [x] 备份策略文档(deploy/backup/backup-mysql.sh,7 日备 + 8 周备)

## 持续事项

- [x] 每个里程碑完成后同步更新 docs(vitepress)与 README(features.md 状态列已对齐实现;新增手机端/部署两节;README 补小程序与当前进度)
- [x] 核心业务逻辑(状态机、价格计算、AliasMethod、积分账本)保持 ≥80% 单测覆盖(实测合并覆盖率:Domain 87.5%,Infrastructure 剔除迁移生成码后 90.7%;OrderService 94.8%/RollService 89.0%/CoinService 95.0%,daily.yml 持续采集)
- [x] 敏感配置(数据库、vapor API Key、JWT 密钥)只走环境变量,不落仓库(全仓扫描无真实密钥,appsettings 仅占位默认值,deploy/.env 已忽略,compose/.env.example 为模板)

## M9 手机版本（2026-10-11 用户令：「再加一个需求，创建手机版本」）

> 现有底子：`miniapp/`（微信小程序，Taro 4.3 + React 18）已落地含提审合规自查；`mobile/`（Taro RN 端）已交付 Android APK 并通过模拟器验收（截图留证）；`web/`（React 19 + Vite 8）为主站。

- [x] **手机端交付**：在现有底子上做出**可用的手机版本**——主路径按已有规划走 Taro RN（复用 miniapp 共享层，避免两端两套代码）；核心页面移动端齐套：首页/开箱、行情、资讯、个人中心、订单（对应 web 现有功能面逐页对齐）
- [x] **功能对齐清单**：web 已实现功能逐项映射到手机端（本机可用 / 手机后置 / 不做 打标），写进本节，防两端漂移

  | web 功能 | 手机端对应 | 打标 |
  | --- | --- | --- |
  | 资讯列表/详情 | 首页资讯流 + 资讯详情（HTML 转纯文本，RN 无 RichText） | 本机可用 |
  | 行情列表/详情（SVG 趋势图） | 行情列表 + 点选加载 30 日收盘 SVG 折线图与数值涨跌幅 | 本机可用 |
  | 商品/箱子列表与详情下单 | 商品列表 + 详情套餐按钮下单（mock 支付实走）+ 优惠券抵扣选择器 + 无套餐商品数量档按档计价下单 | 本机可用 |
  | 订单与支付流程页（支付轮询/取消） | 下单即支付 + 订单页按单号查状态（本地记录近 10 单），待支付单 4s 轮询 + 支付/取消按钮 | 本机可用 |
  | 开箱结果展示与我的奖品 | 「我的奖品」页（奖品列表 + 行情价） | 本机可用；奖品到达依赖 vapor 取回链路 |
  | roll 房（列表/参与/开奖） | 无 | 不做（miniapp 同样未做） |
  | 用户中心（资料/金币/优惠券/签到） | 个人中心页四项齐 | 本机可用 |
  | 交易链接管理 | 无（下单取回链路才需要） | 手机后置（vapor 联调后接） |
  | Steam OpenID 登录 | 无（RN 端用账号密码注册/登录） | 不做（跳转浏览器完成 OpenID 的体验不适配手机端） |
  | 微信小程序登录 | 无（wx.login 为小程序专属） | 不做（属 miniapp 能力） |

- [x] **移动端体验硬项**：触控目标 ≥44px、安全区适配、弱网降级、暗色跟随——随实现验收，不单独留尾
- [x] **手机后置补齐 1/3**：行情 30 日收盘 SVG 趋势折线图（web MarketDetailPage 对齐，react-native-svg 直绘）
- [x] **手机后置补齐 2/3**：订单支付轮询（4s）与未支付取消订单（web OrderPayPage 对齐）
- [x] **手机后置补齐 3/3**：下单优惠券抵扣选择器（/api/coupons/mine + createOrder userCouponId）
- [x] **数量档下单补齐**：CreateOrderRequest 增 quantity（服务端按档计价，clamp 1..1000，套餐档数量优先）+ mobile 无套餐商品数量档选择与按档计价下单；web 数量套餐接真数据修复（原 /skus 按 enabled 过滤恒空且读不存在的 priceCents，现按真实 SkuDto 渲染套餐档 + quantityTiers 阶梯、下单带 quantity；契约断言 quantity/planId 已补进 ProductApiTests）
- **验收（硬口径）**：真机或模拟器**安装可跑**（iOS/Android 至少一端，截图留证）+ 核心流程实走（登录 → 浏览 → 开箱 → 订单查得到）；Taro 两端共享层无复制粘贴漂移；测试绿才 push、commit 英文简短、不发版。注：开箱后的奖品交付依赖 vapor 取回链路（各端一致），手机端验收以「下单 + 支付 + 订单状态可查」为实走边界

## M10 运营内容与 C 端死接口清零（2026-10-12 自主派工：TODO 清零后扫出服务端零前端使用的接口）

> 背景：`POST /api/redeem`、`GET /api/notices`、`GET /api/banners`、`GET /api/articles/{id}`、`GET /api/recommends`、`GET /api/exchange-rates` 六个服务端接口没有任何前端调用；另 M9 打标「手机后置」的交易链接管理（CRUD 不依赖 vapor）。本批全部接通。

- [x] **web 用户中心兑换码**：`POST /api/redeem`（积分码刷新余额、券码刷新优惠券列表，结果与错误就地展示）
- [x] **web 首页运营内容**：公告条（`/api/notices`）+ Banner 横滑（`/api/banners`，LinkUrl 内部路由跳转/外链新开）
- [x] **web 商店推荐位**：`GET /api/recommends` 横滑卡片，直达商品详情
- [x] **shared 层扩展**：`redeemCode` / `fetchNotices` / `fetchTradeUrls` / `addTradeUrl` 四个共享端点函数
- [x] **小程序/手机端用户中心兑换码**：两端共用 shared 函数；顺带修复小程序优惠券过滤错向（原 `status === 1` 把已用当可用，对齐 mobile 的未使用+未过期口径）
- [x] **手机端 Steam 交易链接管理**（M9 手机后置项收口）：绑定/列表/状态展示，CRUD 不依赖 vapor
- [x] **小程序/手机端首页公告条**：与 web 首页公告同源（`/api/notices`）
- [x] **web 行情约合人民币**：非 CNY 快照价按 `/api/exchange-rates` 汇率显示「≈ ¥」（仅展示换算，符合设计）
- [x] **web 文章详情页**：`/articles/:id`（`GET /api/articles/{id}`，运营 Banner 链接可指向）
- [x] **文档同步**：features.md 状态列对齐（运营内容/汇率/积分优惠券/交易链接/手机端五行）
- [x] **用户端金币流水明细**：新增 `GET /api/coins/logs`（自身份、分页、按时间倒序，CoinApiTests 两例：未授权 401、仅本人行+分页）；web/小程序/手机端用户中心均展示明细（web 表格 10 条，移动两端近 5 条）
- [x] **领券中心**：`GET /api/coupons`（可领列表）+ `POST /api/coupons/{id}/claim`（领取，服务端强制每人/总量限制）接通三端——web 用户中心领券表格，小程序/手机端领券卡片，领取后刷新我的优惠券

## M11 邀请返利可见化与前端搜索分页（2026-10-13 自主派工：TODO 清零后按路线图与最近 git log 自定位）

> 背景：扫出两处「有数据模型 / 有查询参数，但客户端零暴露」的缺口——`InviteRewardLog` 只写不读（features.md 称邀请返利已完成，用户却看不到邀请了几人、得了多少奖励）；`ProductQuery` / `MarketItemQuery` 的 `q` / `type` / `game` / `page` 参数服务端已支持，web 与移动端零使用。

- [x] **邀请返利明细（跨三端）**：新增 `GET /api/invites`（自身份、分页、按时间倒序，返回 total / totalReward / items 并 join `UserAccounts` 回填被邀请人昵称）；`InviteApiTests` 三例（未授权 401、只统计本人行且回填昵称、分页取第二页）；shared 增 `fetchInviteRewards` / `InviteRewardView`；web 用户中心「邀请返利」区块（累计人数与奖励 + 明细表），小程序/手机端个人中心邀请返利卡片
- [x] **web 行情搜索与分页**：关键词搜索框（回车或按钮提交）+ 游戏筛选（通用/CS2/Dota2）+「加载更多」翻页（每页 50，显示已加载/总数）
- [x] **web 商店搜索与分页**：关键词搜索框 + 类型筛选（钥匙/箱子/箱子组件）+「加载更多」翻页（每页 24），推荐位横滑保留
- [x] **小程序/手机端商品搜索**：shared `fetchProducts` 增可选 `{ q }` 过滤（既有 `fetchProducts(1)` 调用返回原样 URL，不受影响）；两端商品列表加搜索框与「加载更多」
- [x] **加载更多计数修正**：三端翻页计数改用「已取回的服务端行数」而非「已上架子集」——`total` 覆盖自用/团开商品，用子集会与 total 不符，末页后按钮仍显示并重复取回同一页
- [x] **文档同步**：features.md 三行更新（行情列表与搜索、新增商品列表与搜索、邀请与签到）
- **验收口径（可数）**：服务端 `dotnet test` 355 例全绿（基线 352 + 邀请新增 3 例）；web `pnpm build` + `oxlint` 通过；小程序 `tsc --noEmit` + webpack 构建通过；手机端 `tsc --noEmit` + `build:rn` 通过。共 4 次提交（feat: invite rewards across clients / feat: market and product search with pagination / feat: product search on miniapp and mobile / fix: load more counts server rows），每次测试绿后才提交并 push；全程无 tag、无 release、无 force push
