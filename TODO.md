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
- [ ] 分支保护(在 GitHub 仓库设置中按需开启)

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
- [ ] UserAccount 实体(注册凭据、昵称、头像、邀请码、状态)
- [ ] UserSteamProfile / UserTradeUrl / UserSteamBackupCode 实体
- [ ] UserCoin / UserCoinLog 实体(含批次过期字段)
- [ ] UserCoupon / Coupon / RedeemCode 实体
- [ ] UserLoginLog / UserVisit / InviteRewardLog / SignRecord 实体

### 商品域
- [ ] Product 实体(类型、classId、多级价格字段)
- [ ] CasePlan / CasePlanOption 实体(加价率、返现、数量档位)
- [ ] PromotionPlan / QuantityOption 实体
- [ ] CaseContent 实体(七档品质)
- [ ] Recommend / ExchangeRate 实体

### 订单域
- [ ] UserOrder 实体(入口、价格四件套、抵扣字段、状态、serviceMode)
- [ ] PaymentRecord / RefundRecord 实体

### 开箱域
- [ ] GroupBuying / GroupBuyingSeat 实体(乐观锁版本)
- [ ] UserPrize / UserPrizeLog 实体(饰品完整字段)
- [ ] RollRoom / RollRoomConfig / RollRoomParticipant 实体

### 资讯域
- [ ] NewsSource / NewsItem 实体(指纹去重键)
- [ ] Article / Notice / Banner / Partner 实体

### 行情域与系统域
- [ ] MarketItem / MarketPriceSnapshot / SyncCursor 实体
- [ ] AdminAccount / Role / Permission / RolePermission 实体
- [ ] SiteSetting / SupportQrcode / ShortUrl / NotificationChannel 实体
- [ ] 通用约定落地:应用侧主键生成器(ULID/雪花)
- [ ] 通用约定落地:软删除过滤器、UTC 时间处理
- [ ] 首个 EF 迁移生成并在本地库验证建表

## M2 资讯聚合(自建爬虫,第一个垂直切片)

### 爬虫管道
- [ ] Doggo.Scraper 通用抓取框架:调度(PeriodicTimer/Quartz)+ 站点注册表
- [ ] HttpClient 抓取 + AngleSharp 解析的基础 SourceAdapter 抽象
- [ ] 动态页 Playwright for .NET 的 SourceAdapter 实现
- [ ] 条目清洗:HTML 白名单过滤、图片代理或转存策略
- [ ] 指纹去重(标题+正文哈希)落库前判重
- [ ] 失败重试与站点级熔断(连续失败暂停)
- [ ] SyncCursor 游标记录(增量拉取)

### 数据源接入
- [ ] CS2 官方公告源(Steam News RSS / 官方博客)
- [ ] CS2 更新日志源
- [ ] Dota 2 官方公告源
- [ ] Dota 2 更新与版本改动源
- [ ] 赛事资讯源(至少一个)

### 资讯 API
- [ ] `GET /api/news`(分页、游戏/分类/标签过滤)
- [ ] `GET /api/news/{id}`(详情,阅读计数)
- [ ] 运营内容接口:`GET /api/notices`、`GET /api/banners`、`GET /api/articles/{id}`
- [ ] 集成测试:抓取→清洗→去重→落库→API 输出

## M3 行情同步(vapor 集成)

- [ ] vapor 客户端封装:`Doggo.Infrastructure/Vapor`(REST 调用 + API Key 配置)
- [ ] Webhook 接收端点(验签、幂等)
- [ ] MarketItem 资料同步任务(classId、市场名、图标)
- [ ] MarketPriceSnapshot 价格快照同步任务
- [ ] 按日聚合任务(快照 → 日线,供趋势图)
- [ ] `GET /api/market/items`(饰品/箱子列表、搜索)
- [ ] `GET /api/market/items/{id}/prices`(历史价格序列)
- [ ] 汇率接口:`GET /api/exchange-rates`
- [ ] vapor 不可用时的降级策略(读最近快照、任务退避)

## M4 商品与订单

### 商品 API
- [ ] `GET /api/products`(箱子/钥匙列表、排序)
- [ ] `GET /api/products/{id}`(详情 + 套餐选项 + 内容物展示)
- [ ] `GET /api/products/{id}/skus`(按履约方式的库存与价格)
- [ ] `GET /api/promotions`(限量促销)
- [ ] `GET /api/recommends`(推荐位)
- [ ] 价格计算服务:采购价/参考价/修正价/加价率/数量档位阶梯

### 订单状态机
- [ ] 下单服务(装配价格、占库存/座位、支付期限)
- [ ] 订单状态机实现(待支付→待结算→待取回→取回中→已完成;取消/退款)
- [ ] 超时未支付取消任务
- [ ] 支付抽象接口 `IPaymentProvider`(创建支付、回调验证、退款)
- [ ] 回调幂等处理(状态条件更新)
- [ ] 对账补偿任务(取回检查、状态回退)

### 交易链接与饰品取回
- [ ] 用户交易链接 CRUD + 有效性校验接口
- [ ] vapor trading action 发起取回
- [ ] 取回结果回调处理(订单完成/回退)
- [ ] 取回重试策略与错误记录

## M5 开箱系统

### 批次与座位
- [ ] 批次创建(账号批次、座位数、乐观锁)
- [ ] 占座服务(条件更新,售罄判定)
- [ ] 开箱请求 → vapor CaseOpening action 提交
- [ ] 开箱结果事件处理 → 生成 UserPrize + 推进座位状态
- [ ] 座位状态机全链路测试(售卖中→…→完成)

### 奖品
- [ ] 奖品记录查询 API(我的奖品列表/详情)
- [ ] 奖品估价关联(行情价快照)
- [ ] 开箱日志(UserPrizeLog)查询

### roll 房
- [ ] AliasMethod 抽样算法实现 + 单元测试(权重分布验证)
- [ ] 房间模板与规则配置(登录领取/每单参与/邀请码等)
- [ ] 房间创建 → 进行中 → 开奖状态机
- [ ] 开奖任务(到期检查 + 权重抽取)
- [ ] 奖品发放(库存饰品走交易 / 积分入账)
- [ ] 房间自动滚动下一期
- [ ] `GET /api/roll/rooms`、`GET /api/roll/rooms/{id}` C 端接口

## M6 用户体系与营销

### 认证
- [ ] 注册/登录(账号密码 + JWT 签发)
- [ ] 刷新 token / 登出
- [ ] Steam OpenID 登录绑定
- [ ] 登录日志与访问日志中间件

### 积分与优惠
- [ ] 积分账本服务(余额、FIFO 消费、退款回补)
- [ ] 积分批次过期任务
- [ ] 兑换码核销(积分/优惠券两种)
- [ ] 优惠券:领取、可用商品判定、下单抵扣、退款回补
- [ ] 邀请奖励:绑定邀请码、奖励入账
- [ ] 签到:自然周周期、补签规则

## M7 客户端

### web/(React + TypeScript + Vite)
- [ ] 脚手架初始化(Vite + TS + 路由 + 请求封装)
- [ ] 设计系统基座:主题、布局、公共组件(资讯卡片/行情表/饰品卡)
- [ ] 资讯列表/详情页
- [ ] 行情列表/详情页(价格趋势图)
- [ ] 商品/箱子列表与详情页
- [ ] 订单与支付流程页
- [ ] 开箱结果展示与我的奖品页
- [ ] roll 房页面
- [ ] 用户中心(资料/交易链接/积分/优惠券/签到)
- [ ] Steam 登录接入

### admin/(Ant Design Pro)
- [ ] 脚手架初始化(UmiJS + Pro)
- [ ] 登录与 RBAC 权限接入
- [ ] 资讯管理:源管理、条目管理、文章/公告/Banner
- [ ] 商品管理:商品/套餐/选项/促销/汇率
- [ ] 订单管理:查询、退款、奖品查看
- [ ] 用户管理:账号、流水、登录日志
- [ ] roll 房管理:模板、房间、奖品配置
- [ ] 数据统计:面板、走势、排行
- [ ] 系统设置:站点配置、权限、IP 白名单

### miniapp/(Taro + React + TypeScript)
- [ ] Taro 脚手架初始化(微信小程序)
- [ ] 登录(wx.login 换 JWT)
- [ ] 资讯/行情核心页
- [ ] 商品与开箱结果页
- [ ] 用户中心页
- [ ] 提审合规自查(类目、隐私协议)

### mobile/(Taro + React + TypeScript)
- [ ] Taro RN 端初始化,与 miniapp 共享层抽包
- [ ] 核心页面适配(RN 差异处理)
- [ ] 打包流水线(Android APK 优先)

## M8 部署与运维

- [ ] `server/src/Doggo.Api` Dockerfile
- [ ] Scraper / Worker Dockerfile
- [ ] `web/` 构建产物 nginx 容器或静态部署
- [ ] `admin/` 构建产物部署配置
- [ ] `deploy/docker-compose.prod.yml`(全栈编排)
- [ ] nginx 配置模板(域名、HTTPS、反代 Api)
- [ ] GitHub Actions 发布流水线(镜像构建 + 部署)
- [ ] 部署文档(docs/guide/deployment.md)
- [ ] 备份策略文档(MySQL 备份脚本 + 说明)

## 持续事项

- [ ] 每个里程碑完成后同步更新 docs(vitepress)与 README
- [ ] 核心业务逻辑(状态机、价格计算、AliasMethod、积分账本)保持 ≥80% 单测覆盖
- [ ] 敏感配置(数据库、vapor API Key、JWT 密钥)只走环境变量,不落仓库
