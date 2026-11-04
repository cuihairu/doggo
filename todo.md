# TODO

> 只列需求,每条一句。已完成项的证据(提交号与该批测试数)写在所属节标题行。


## M0 项目骨架与基础设施

证据:422cfbc / 268cd82 / 29c7d69 / fbc5470 / ffa6347 / e899982 / 65217de / f3e6b46

### 仓库工程化
- [x] README 中英双语
- [x] VitePress 文档站点
- [x] 功能分析与设计文档
- [x] .editorconfig 统一 C# 与 TypeScript 代码风格
- [x] Directory.Build.props 统一 .NET 版本、nullable 与 analyzers
- [x] gitignore 覆盖 bin 与 obj
- [x] CI 跑 dotnet build、dotnet test、Codecov 上传与文档构建
- [x] README 加 CI 与 Codecov 徽章
- [x] PR 模板
- [x] main 分支禁强推禁删除、线性历史

### server 解决方案骨架
- [x] Doggo.slnx 解决方案
- [x] Doggo.Domain 类库
- [x] Doggo.Infrastructure 类库,EF Core + MySQL + Redis
- [x] Doggo.Api Web 项目,Minimal API
- [x] Doggo.Scraper Worker Service
- [x] Doggo.Worker Worker Service
- [x] Doggo.Domain.Tests 测试项目
- [x] 项目引用关系为 Api/Scraper/Worker 到 Infrastructure 到 Domain
- [x] 解决方案构建全绿
- [x] Api 暴露 /healthz 健康检查,MySQL 与 Redis 探活
- [x] 各项目 appsettings.json 与 Development 变体
- [x] MySQL provider 用 MySql.EntityFrameworkCore 10.0.9

### 本地开发环境
- [x] deploy/docker-compose.yml 起 mysql 与 redis,带健康检查
- [x] deploy/.env.example
- [x] EF Core 连接配置与启动迁移验证
- [x] Redis 连接配置验证


## M1 领域模型与数据层

证据:29c7d69 / d51dd16

### 用户域
- [x] UserAccount 实体
- [x] UserSteamProfile、UserTradeUrl、UserSteamBackupCode 实体
- [x] UserCoin、UserCoinLog、UserCoinBatch 实体,按 FIFO 过期
- [x] UserCoupon、Coupon、RedeemCode 实体
- [x] UserLoginLog、UserVisit、InviteRewardLog、SignRecord 实体

### 商品域
- [x] Product 实体,含 classId 与多级价格字段
- [x] CasePlan、CasePlanOption 实体,含加价率与数量档位
- [x] PromotionPlan、QuantityOption 实体
- [x] CaseContent 实体,七档品质
- [x] Recommend、ExchangeRate 实体

### 订单域
- [x] UserOrder 实体,含价格四件套、抵扣字段、状态、serviceMode
- [x] PaymentRecord、RefundRecord 实体

### 开箱域
- [x] GroupBuying、GroupBuyingSeat 实体,含乐观锁版本
- [x] UserPrize、UserPrizeLog 实体,含饰品完整字段
- [x] RollRoom、RollRoomTemplate、RollRoomPrize、RollRoomParticipant 实体

### 资讯域
- [x] NewsSource、NewsItem 实体,含指纹去重键
- [x] Article、Notice、Banner、Partner 实体

### 行情域与系统域
- [x] MarketItem、MarketPriceSnapshot、MarketPriceDaily、SyncCursor 实体
- [x] AdminAccount、Role、Permission、RolePermission 实体
- [x] SiteSetting、SupportQrcode、ShortUrl、NotificationChannel 实体
- [x] 通用约定落地:应用侧 Guid v7 主键
- [x] 通用约定落地:软删除过滤、UTC 时间、SaveChanges 审计
- [x] 首个 EF 迁移建表并在本地库验证
- [x] 数据层行为测试:ID 生成、审计时间戳、软删除过滤


## M2 资讯聚合

证据:f1e3930 / 7b2ec76

### 爬虫管道
- [x] Doggo.Scraper 抓取框架:调度与站点注册表
- [x] HttpClient + AngleSharp 的 SourceAdapter 抽象
- [x] Playwright for .NET 的动态页 SourceAdapter
- [x] 条目清洗:HTML 白名单过滤与图片代理策略
- [x] 指纹去重,落库前判重
- [x] 失败重试与站点级熔断
- [x] SyncCursor 游标增量拉取

### 数据源接入
- [x] CS2 官方公告源
- [x] CS2 更新日志源
- [x] Dota 2 官方公告源
- [x] Dota 2 更新与版本改动源
- [x] 赛事资讯源,至少一个

### 资讯 API
- [x] GET /api/news 分页与游戏、分类、标签过滤
- [x] GET /api/news/{id} 详情与阅读计数
- [x] 运营内容接口:GET /api/notices、GET /api/banners、GET /api/articles/{id}
- [x] 集成测试:抓取到清洗到去重到落库到 API 输出


## M3 行情同步

证据:38e67d0 / b088bb5 / bd05f01 / 6560fda

- [x] vapor 客户端封装,Doggo.Infrastructure/Vapor
- [x] Webhook 接收端点,验签与幂等
- [x] MarketItem 资料同步任务
- [x] MarketPriceSnapshot 价格快照同步任务
- [x] 按日聚合任务,快照到日线
- [x] GET /api/market/items 列表与搜索
- [x] GET /api/market/items/{id}/prices 历史价格序列
- [x] GET /api/exchange-rates 汇率接口
- [x] vapor 不可用时的降级策略,读最近快照并任务退避


## M4 商品与订单

证据:6e1f337 / ea3725a

### 商品 API
- [x] GET /api/products 列表与排序
- [x] GET /api/products/{id} 详情、套餐选项与内容物
- [x] GET /api/products/{id}/skus 按履约方式的库存与价格
- [x] GET /api/promotions 限量促销
- [x] GET /api/recommends 推荐位
- [x] 价格计算服务:采购价、参考价、修正价、加价率、数量档位阶梯

### 订单状态机
- [x] 下单服务:装配价格、占库存或座位、支付期限
- [x] 订单状态机:待支付到待结算到待取回到取回中到已完成,取消与退款
- [x] 超时未支付取消任务
- [x] 支付抽象接口 IPaymentProvider:创建支付、回调验证、退款
- [x] 回调幂等处理,状态条件更新
- [x] 对账补偿任务:取回检查、状态回退

### 交易链接与饰品取回
- [x] 用户交易链接 CRUD 与有效性校验接口
- [x] vapor trading action 发起取回
- [x] 取回结果回调处理,订单完成或回退
- [x] 取回重试策略与错误记录


## M5 开箱系统

证据:8a29637 / 56ad25d / b4b9e77 / c2b64c7

### 批次与座位
- [x] 批次创建,含账号批次、座位数、乐观锁
- [x] 占座服务,条件更新与售罄判定
- [x] 开箱请求提交 vapor CaseOpening action
- [x] 开箱结果事件处理,生成 UserPrize 并推进座位状态
- [x] 座位状态机全链路测试

### 奖品
- [x] 奖品记录查询 API:我的奖品列表与详情
- [x] 奖品估价关联行情价快照
- [x] 开箱日志查询

### roll 房
- [x] AliasMethod 抽样算法与单元测试
- [x] 房间模板与规则配置
- [x] 房间状态机:创建到进行中到开奖
- [x] 开奖任务:到期检查与权重抽取
- [x] 奖品发放:库存饰品走交易或积分入账
- [x] 房间自动滚动下一期
- [x] C 端接口 GET /api/roll/rooms 与 GET /api/roll/rooms/{id}


## M6 用户体系与营销

证据:24e4fb2 / 94572a1 / 5a05b39 / 8a75fe5 / d75da6b

### 认证
- [x] 注册登录,账号密码 + JWT 签发
- [x] 刷新 token 与登出
- [x] Steam OpenID 登录绑定
- [x] 登录日志与访问日志中间件

### 积分与优惠
- [x] 积分账本服务:余额、FIFO 消费、退款回补
- [x] 积分批次过期任务
- [x] 兑换码核销,积分与优惠券两种
- [x] 优惠券领取、可用商品判定、下单抵扣、退款回补
- [x] 邀请奖励:绑定邀请码与奖励入账
- [x] 签到:自然周周期与补签规则


## M7 客户端

证据:b9d8ecf / d7ece79 / 2d020e7 / eb92dca / cb17c25 / 2214566 / f873504 / bc44720 / f514105 / f9d838d / 91ba0de / 8849e96(488 绿)

### web
- [x] Vite + React + TS 脚手架,含路由与请求封装
- [x] 设计系统基座:主题、布局、公共组件
- [x] 资讯列表与详情页
- [x] 行情列表与详情页,SVG 折线趋势图
- [x] 商品与箱子列表、详情页、SKU 选择下单
- [x] 订单与支付流程页:模拟支付、取消、状态轮询
- [x] 开箱结果展示与我的奖品页
- [x] roll 房页面:列表、详情、参与、开奖
- [x] 用户中心:资料、交易链接、积分、优惠券、签到
- [x] Steam 登录接入

### admin
- [x] UmiJS + Pro 脚手架,pnpm build 全绿
- [x] 登录与 RBAC 权限:role claim + Admin policy,非管理员 403
- [x] 资讯管理:源、条目、文章、公告、Banner
- [x] 商品管理:上下架、套餐选项、促销、汇率
- [x] 商品资料写入:新建与编辑商品、套餐选项、数量档、内容物全量替换
- [x] 订单管理:查询、退款、奖品查看
- [x] 用户管理:账号、流水、登录日志
- [x] roll 房管理:模板、房间、奖品配置
- [x] 数据统计:面板、近 14 天走势、商品排行
- [x] 系统设置:站点配置、权限、IP 白名单

### miniapp
- [x] Taro 微信小程序脚手架,taro build --type weapp 通过
- [x] wx.login 换 JWT 登录
- [x] 资讯与行情核心页
- [x] 商品与开箱结果页
- [x] 用户中心页:资料、金币、优惠券、签到
- [x] 提审合规自查

### mobile
- [x] Taro RN 端,与 miniapp 共享层抽包
- [x] 核心页面适配:账密登录、RN 组件、暗色主题、订单本地查询
- [x] Android APK 打包流水线与模拟器验收


## M8 部署与运维

证据:86be665 / 8aa1fb7 / b00c44e / 56c5143 / 7d0e385 / 88043bf / 086ed86

- [x] Doggo.Api Dockerfile,多阶段含 migrator target
- [x] Scraper 与 Worker 共用 Dockerfile.worker,PROJECT 参数区分
- [x] web 构建产物 nginx 容器,SPA fallback 与 /api 反代
- [x] admin 构建产物部署配置
- [x] deploy/docker-compose.prod.yml 八服务全栈,migrator 先行
- [x] nginx 配置模板:域名、HTTPS、反代、admin IP 白名单
- [x] GitHub Actions 发布流水线:GHCR nightly 镜像与 DEPLOY_ENABLED 门控部署
- [x] 部署文档
- [x] 备份策略:7 日备与 8 周备


## M9 手机版本

证据:3d59054 / a64e882 / 6bb6b58 / c45beee / a9cab04 / 27efb0a / 6df1172 / 9755539 / 0a6c225 / 3ea774f

- [x] 手机端交付:Taro RN 复用 miniapp 共享层
- [x] 核心页面移动端齐套:首页开箱、行情、资讯、个人中心、订单
- [x] 行情 30 日收盘 SVG 趋势折线图
- [x] 订单支付轮询与未支付取消
- [x] 下单优惠券抵扣选择器
- [x] 无套餐商品数量档按档计价下单
- [x] 真机或模拟器安装验收,核心流程实走:登录、浏览、开箱、订单可查


## M10 运营内容与 C 端接口接通

证据:9f14726 / b297dc7 / 02bd86d / b542f9f / 3cb4865 / 1f8ef44 / 565bb37 / 613f3a8 / ff36b03

- [x] web 用户中心兑换码
- [x] web 首页公告条与 Banner 横滑
- [x] web 商店推荐位
- [x] shared 层扩展:redeemCode、fetchNotices、fetchTradeUrls、addTradeUrl
- [x] 小程序与手机端用户中心兑换码
- [x] 小程序优惠券过滤口径修正
- [x] 手机端 Steam 交易链接管理
- [x] 小程序与手机端首页公告条
- [x] web 行情约合人民币展示
- [x] web 文章详情页 /articles/:id
- [x] 用户端金币流水明细 GET /api/coins/logs,三端展示
- [x] 领券中心:GET /api/coupons 与 POST /api/coupons/{id}/claim,三端接通


## M11 邀请返利可见化与前端搜索分页

证据:8be678b / 1b6111e / 2f04b68 / 03e7526 / 0676920 / a997fb9

- [x] 邀请返利明细 GET /api/invites,跨三端展示
- [x] web 行情搜索与分页:关键词、游戏、类型、加载更多
- [x] web 商店搜索与分页:关键词、类型、加载更多
- [x] 小程序与手机端商品搜索与加载更多
- [x] 翻页计数改用服务端返回行数
- [x] web 行情类型筛选
- [x] 该批测试基线 355 例全绿


## M12 界面重设计与两个模块

证据:原型 5914329 / 4011390 / d1064be / 89e1c05 / 29fb730 / 67b29b9;价格爬虫 1ca6dd5 + 0b08b2c(441 绿);漏斗 152a7de + 69da251 + 1b47726 + 6af514a + a5437c9(480 绿);主题令牌化 695d1e7 + 0b43096 + 6521306 + 5061c04 + 7f453a6(四端 build 绿);状态组件矩阵 62dbf2e + ffa0bb2 + 5a73d0c + 1b9387c;下单开箱闭环 5cc56ed + 3ec06a9 + c84c4c7;商品详情补齐 08570b9 + 297ccb5 + b353ce4;admin 设置页与行情涨跌 5934a0b + a402de8;admin 价格爬虫页 549ad12;支付渠道与 Banner 日历 d389f96 + 2ac84f0;个人中心补齐 b6bfdfd(全仓 504 绿)

### 四端 UI 原型重做
- [x] web、小程序、mobile、admin 各一套静态原型,多主题令牌化
- [x] 11 套主题:默认、暗色、五个节日、四个季节
- [x] 每端覆盖首页、详情、关键动作、设置或个人页,含空态、加载、错误、成功状态图
- [x] ui-system.md 记录令牌表、主题清单、组件复用关系

### 四端令牌化落地
- [x] web:tokens.css + theme.ts + 顶栏/个人中心主题卡
- [x] mobile:11 套 Palette + 跟随系统默认与手动覆盖
- [x] miniapp:app.scss 令牌 + 原生 chrome 同步 + 主题外观卡
- [x] admin:antd token 映射 + rootContainer ConfigProvider + 顶栏下拉
- [x] 按原型逐屏对照并给差异清单(ui-system.md §10)

### 状态组件矩阵落地(ui-system.md §10.3 结构性差异 1)
- [x] web:components/States.tsx(Skeleton/EmptyState/ErrorBox/SuccessBox),九页接入骨架/空态/错误重试/支付成功下一步,差异清单同步
- [x] mobile:ui.tsx 增加 StateIco/Skeleton/EmptyState/SuccessBox,Msg 升级为错误框;商品/行情/开箱/商品详情/资讯详情/订单六页接入,订单页支付成功给下一步
- [x] miniapp:states.tsx(Skeleton/EmptyState/ErrorBox/SuccessBox),商品/行情/商品详情/资讯详情/开箱五页接入,加载/错误/空态替换文本,开箱页带登录引导
- [x] admin:components/States.tsx(PageLoading/ErrorAlert/EmptyAction),Dashboard/Funnel/商品资料抽屉接入,Orders/Users/Rolls/Products/Articles 13 个 ProTable 错误 Alert+重试,静默 catch 消除

### 下单/开箱闭环(ui-system.md §10.3 结构性差异 2)
- [x] web:确认订单页(订单内容/支付方式/支付倒计时/协议勾选/金额明细)与开箱结果卡(open-glow/稀有 badge/参考价/检视链接)落地;OrderView 补商品名、图标与方案标题,SeatView 补奖品快照
- [x] mobile:订单卡补商品图标/名称/套餐、支付倒计时徽章(mm:ss,服务端 payDeadlineUtc)、支付方式「支付宝(模拟)」与优惠券抵扣行;开箱页新增结果卡(最新奖品置顶,金色 glow、稀有/品质 badge、参考价、存奖品与取回待 vapor 提示);shared 类型补 productName/productIconUrl/planTitle 与座位奖品快照
- [x] miniapp:无订单路由,商品页下单前用确认弹窗替代确认订单页(商品/套餐/支付方式「支付宝(模拟)」/应付金额/15 分钟时限,取消则不埋点);开箱页新增结果卡(金色 glow、稀有/品质 badge、参考价、存奖品与取回待 vapor 提示),样式入 app.scss

### 商品详情补齐(ui-system.md §10.3 结构性差异 3)
- [x] web:「包含内容」徽章网格(首 8 项 + 其余 N 项)、划线原价、促销库存 badge、金币抵扣与优惠券选择(/api/coupons/mine,SKU 切换重置)、底部 buybar(应付券后 + 立即开箱);行情参考价对照表需行情日涨跌数据,未做
- [x] mobile:金币抵扣 chip(余额上限 50/200/500/2000,1 金币抵 1 分,随 createOrder 的 coinDeduction 提交,余额不足以服务端校验为准)、「包含内容」徽章网格(品质色边框 + ★品质 + 参考价,全部项)、底部 buybar(最低单开价 + 立即开箱);划线原价仍缺(缺原价字段展示位)
- [x] miniapp:划线原价(regularPriceCents > finalPriceCents 时显示)、套餐语义改单开/三开/十开、「包含内容」徽章网格(品质色边框 + ★品质 + 参考价)、底部 buybar(fixed + safe-area,最低单开 + 立即开箱);金币抵扣与优惠券选择已补(2026-10-17,FIRE1017H:mobile 同款口径,filter-chip 选择随 createOrder 的 userCouponId/coinDeduction 提交,确认弹窗列抵扣行,useDidShow 回登录态刷新;miniapp typecheck + weapp build 绿)

### admin 设置页(ui-system.md §10.3 结构性差异 4)
- [x] 服务端:GET /api/admin/settings 与 PUT /api/admin/settings/{key},走既有 SiteSetting 表,key 形状校验 + 值 5000 字上限 + valueType 白名单,upsert 覆盖
- [x] admin:设置页站点参数行内编辑/保存/新增覆盖 + 骨架/错误重试/空态;爬虫站点总览读 price-crawl/maps 聚合站点映射与待确认数
- [x] 支付渠道配置:服务端 PaymentChannel 实体(商户号/AppId/费率基点/启用)+ GET 列表与 PUT upsert(Provider 唯一索引,一行一渠道);密钥不落库,SecretRef 仅存环境变量名(字母/数字/下划线,≤64 字),费率 0-10000 基点;admin 控制台挂载由设置页方跟进
- [x] 主题运营位日历:Banner 增 StartAtUtc/EndAtUtc 投放区间(半开区间,null 不限),同 position 启用横幅窗口重叠返回 409、end<=start 返回 400,新增 PUT 编辑窗口;前台 /api/banners 只返回窗口内(或未设窗口)的横幅
- [x] admin 控制台挂载(2026-10-17,PATROL1017G,收掉 L322 的「由设置页方跟进」与本条的「控制台入口」):设置页新增支付渠道卡(列表 + 编辑 upsert `PUT /api/admin/payment-channels/{provider}`,密钥只登记环境变量名);内容中心 Banner 页补投放窗口列、创建窗口字段与行内编辑(PUT 全量,400/409 服务端文案透出);admin pnpm build 绿

### 个人中心补齐(ui-system.md §10.2 profile 行)
- [x] mobile:头像头(昵称首字 + 手机/邮箱掩码 · 邀请码)、stat-row 三格(金币/可用优惠券/邀请人数)、本周签到进度(已签 n/7 · 今天可 +N,数据取 /api/sign 周期行);金币卡并入 stat-row
- [x] miniapp:同款头像头与 stat-row,签到卡改为本周进度;样式入 app.scss

### 行情页对照与涨跌(ui-system.md §10.3 结构性差异 5)
- [x] 服务端:GET /api/market/items 增 changeCents/changeBps,取最近两个日线收盘,单日线或前收为 0 时为 null;2 例新测试
- [x] mobile/miniapp:行情列表日涨跌红绿(服务端 changeBps)、搜索框与游戏/类型筛选,shared fetchMarket 收 filters
- [x] 站内 vs 外部参考价对照表:2026-10-16 代决按聚合口径落地(用户授权代决,可推翻)

### 价格爬虫独立模块
- [x] 独立 classlib Doggo.PriceCrawler,只依赖 Domain,可独立成项目
- [x] 调度:per-site 串行循环、夜间窗口、IntervalMinutes 频控、robots 门禁、发现任务、熔断、指数退避、原始行清理
- [x] 站点适配器:ISiteAdapter + 配置驱动的 HTML 适配器
- [x] 清洗去重:币种归一、价格上界、60 分钟窗口、滚动中位数疑点标记
- [x] 落库:ItemMap、Raw、Series、SiteState 四表加迁移,映射三态门禁
- [x] 产出:PriceTrend 日收盘、日环比、周同比、区间涨跌
- [x] admin 接口 /api/admin/price-crawl:maps 列表与审阅、prices 序列、spread-report 比价
- [x] 模块 81 例加 Api 5 例测试先写后绿
- [x] admin 页面:「数据分析 → 价格爬虫」映射审阅(状态分段 + 确认/拒绝)与比价表(7/30 天),ProTable 错误重试走 actionRef.reload

### 漏斗数据分析
- [x] 埋点模型:AnalyticsEvent 与 FunnelDailyStat 实体、事件字典四事件白名单
- [x] 上报接口 POST /api/events,匿名可用,批量、体积、限频、幂等、query 剥除
- [x] 服务端权威打点:order_created 与 order_paid 同一 SaveChanges 批次
- [x] 聚合任务:FunnelAggregator 按身份、会话回填、7 天窗口、按第一步归日、幂等 upsert
- [x] 每日 00:30 UTC 跑 T-1 并滚动重算 T-7 到 T-1
- [x] 后台接口 GET /api/admin/funnel 与 admin 漏斗页
- [x] 客户端封装:shared/src/events.ts 与 web/src/api/events.ts,商品详情页两处埋点,下单请求带 X-Doggo-Session
- [x] 该批新增 28 例测试,全仓 469 绿


## 持续事项

证据:c88d823 / 2f41ab5 / d611fc6

- [x] 每个里程碑完成后同步更新 docs 与 README
- [x] 核心业务逻辑保持 80% 以上单测覆盖,daily.yml 持续采集
- [x] 敏感配置只走环境变量,不落仓库
- [x] 文档首页：按功能配截图预览（证据：2180556/956d9b0/9668bf1；docs:build ×3 绿）
- [x] 统一 TODO 文件名为小写 todo.md 并修齐全仓引用与中英审计文档（2026-10-28）


## 未完成项

- [ ] vapor 联调:需可用的 vapor 实例与 Steam 账号 — BLOCKED 缺可用的 vapor 实例与 Steam 账号　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 小程序提审:需主体资质、正式 AppID 与合法域名 — BLOCKED 缺主体资质、正式 AppID 与合法域名　复核注记：2026-11-04 复核仍缺外部条件
- [x] docs 双语补齐
- [x] docs/ 根目录:审计-文档一致性.md 已配 docs/en/审计-文档一致性.md(真实英译)
- [x] docs/index.md:用户已代决=站点级 i18n(config.mts 加 `locales.{root,en}`,英文首页挂 /en/;home 页不塞逐页切换器,语言切换走站点级导航 `VPNavBarTranslations`,两个 locale 各声明 label 即出现)。复核注:home 布局正文实际会渲染(dist/index.html 的 `.vp-doc` 内含「界面预览」与截图 `<img>`),原「不渲染正文、切换器无处放」的判断有误;按代决执行,不影响验收
- [x] docs/guide/ 6 篇(introduction/features/architecture/getting-started/deployment/miniapp-compliance)配英文版(真实英译,已移至 docs/en/guide/,切换器指向 /en/ 路径)
- [x] docs/design/ 7 篇(overview/data-model/case-opening/legacy-analysis/ui-system/price-crawler-design/analytics-funnel-design) + prototypes/README.md 配英文版(真实英译,均低于执行单 1000 行门槛;已移至 docs/en/design/);全部中文原文补切换器(首 3 行内)
- [x] README.md L93-101「本地测试口径」节为中文正文,违反「README 不混中文」红线:README.md 改英文节(Local testing by change type),README.zh.md 补对应中文节;README.md L87 残留中文专名(vapor 联调、小程序提审)改英文并刷新计数
- [x] 收尾门禁:pnpm docs:build 绿,/en/ 下 15 页与 /en/ 首页全部产出,nav 切换器在内容页渲染并指向镜像路径;顺手修 nav logo 路径
- [x] 2026-10-24 按舰队约定翻转(e4b0b9a):英文上根、中文移 `docs/zh/`、删 `docs/en/`,仅用 VitePress 官方 `locales`(zh 带 `link: /zh/`),无逐页切换器、无 .zh.md 镜像;根与 /zh/ 各 16 页镜像产出,站内 md 与 nav 链接死链 0


## 审核发现

- [x] 审核发现：中英文项目状态仍写“469 server tests green”(（docs/guide/introduction.md:27、docs/zh/guide/introduction.md:27、todo.md:288）)
- [x] 审核发现：提交标题仅称完成小写迁移(（README.md:78、todo.md:3）)
- [x] 审核发现：a285f53..HEAD 未发现问题(证据:a285f53)
- [x] 审核发现：本范围新增第 3 条审核记录后(（README.md:78、todo.md:392）)
- [x] 审核发现：决策文档引用的 todo.md 行号过期(（条目 3 的 todo.md:333-336 引用已核验仍有效，未改）)
- [x] 审核发现：决策文档 appsettings 行号错位(（docs/review/2026-10-30-decisions.md:11）)
- [x] 审核发现：决策文档条目 2 与条目 3 之间出现连续两个「---」分隔线(（docs/review/2026-10-30-decisions.md:62、docs/review/2026-10-30-decisions.md:64）)
- [x] 审核发现：提交标题仅称修复决策文档待办引用(（todo.md:397-399）;证据:bf9a532)
- [x] 审核发现：提交 074d27e 标题仅称「record audit finding verification」(（README.md:78、todo.md:400）;证据:074d27e)
- [x] 审核发现：本轮 diff 未见问题（074d27e1..HEAD）(证据:074d27e1/074d27e/2f58fe3)
- [x] 审核发现：本轮 diff 未见问题（2f58fe3a..HEAD）(证据:2f58fe3a/edfeaad)
- [x] 审核发现：审计报告声称修复后 18 个「路由 × 视口」组合全部 overflow=false
- [x] 审核发现：审计报告「健康评分 78/100」未明确是修复前基线还是修复后复评
- [x] 审核发现：签到日期 chip 仍用 var(--primary) 小字置 var(--primary-weak)(（春 2.59、夏 2.78、灯笼 2.71、秋 3.60、冬 3.75、国庆 3.89，均 <4.5:1）)
- [x] 审核发现：本提交标题「pass wcag aa on primary text and buttons」只修 web(（:42、:55）)
- [x] 审核发现：tokens.css 头注称值与 docs/design/prototypes/tokens.css 一致(（web/src/tokens.css:1、docs/design/prototypes/tokens.css:225）;证据:d13150c)
- [x] 审核发现：同为价格主色文本(（这些为 20-22px 粗体大字号，≥3:1 大文本 AA 尚达标，但转换不彻底）)


## 审核发现 2026-10-31

- [x] 审核发现：tokens.css 头注「--primary-strong：比 --primary 更深/更稳」与取值不符
- [x] 审核发现：todo.md:408 新记审核发现称「d13150c 未同步的其余 5 主题 muted 一并同步」(证据:d13150c/c287e55)
- [x] 审核发现：审计报告新增「修复后复评 92/100」仍列「品牌主色前景 AA」为遗留项（且结论节称其为唯一拦在达标门前的待拍板项）
- [x] 审核发现：README 计数在中转提交 a73c0c0 写「251 of 253 checklist items」(（README.md:78、todo.md:411-415）;证据:a73c0c0)
- [x] 审核发现：docs/review/2026-10-31-ui-audit-impeccable.md 第 62-76 行「遗留问题（记录(（11/11 主题 --primary-strong 对文本 ≥4.5:1）;证据:c287e55/fa5a8aa)
- [x] 审核发现：docs/review/2026-10-31-ui-audit-impeccable.md:69 新增 P1「解决」行称「每个主题取比 --primary 深一档」(（与 docs/design/prototypes/tokens.css:7 头注及 11 组取值一致）;证据:7db78ab/c0984c8)
- [x] 审核发现：todo.md:419 声称「本轮 diff 未见问题（8e455d09..HEAD）」并称「已删」(（todo.md:419）;证据:8e455d09/870a6e6/c0984c8/f14c4f5)
- [x] 审核发现：提交 c0984c8 标题仅称「docs: fix primary-strong wording and verify audit finding」(（README.md:78）;证据:c0984c8)
- [x] 审核发现：todo.md:418 称「与本 diff 同文件 tokens.css 头注已改为『按主题明暗方向调整 --primary』相矛盾」(（todo.md:418、docs/design/prototypes/tokens.css:7）;证据:c0984c8/7db78ab)
- [x] 审核发现：README.md 顶部出现重复 docs 徽章(（README.md:1）;证据:80ba820/5b1e3d4/0320db9/80b6859)
- [x] 审核发现：5b1e3d4 删除 README 项目标题 `# Doggo`(（README.md:1、README.zh.md:1）;证据:5b1e3d4/f32804d/80b6859)
- [x] 审核发现：README 顶部 HTML 块吞掉 Markdown 标题与语言链接（README.md:1-6、README.zh.md:1-6；CommonMark 实测 H1=0）(证据:bc459c7)
- [x] 审核发现：README.zh.md 的 TODO.md 大写死链（README.zh.md:69）(证据:bc459c7)

## 代码深审 2026-11-02

### 已修（本轮）

- [x] 深审 P0 修复:订单/开箱/roll/奖品/交易链接匿名面加 JWT(证据:be05a90)
- [x] 深审 P0 修复:支付回调金额不符拒绝结算(证据:b0ffb2c)
- [x] 深审 P1 修复:SteamOpenId 状态 HMAC 改为随机启动密钥+常数时间比较(证据:3b8abf0)

### 待处理

- [ ] 深审 P0 部署:MockPaymentProvider 在生产被注册为唯一支付渠道(Program.cs:83;需 fail-closed 或接入真实渠道,本轮仅修金额校验) — BLOCKED 缺真实支付渠道接入决策与凭据(Alipay/WeChat 密钥),Program.cs:83 注释明示属部署里程碑　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P0 并发:取消与支付回调竞态,UserOrder 无乐观锁(OrderService.cs:241-256;需条件更新或 rowversion) — BLOCKED 缺真实 MySQL 验证行级并发/乐观锁(InMemory 不强制),需 EF migration+条件更新　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P0 并发:roll 开奖无双实例锁,两 worker 同开双中奖(RollService.cs:121-173) — BLOCKED 缺真实 MySQL 验证原子状态迁移(InMemory 不强制),需条件更新或分布式锁　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P0 并发:roll 金币奖励双发,ClaimedAtUtc 竞态(RollService.cs:204-266) — BLOCKED 缺真实 MySQL 验证条件更新(InMemory 不强制),需并发令牌+幂等约束　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:金币余额非原子读改写,无并发令牌(CoinService.cs:38-176) — BLOCKED 缺真实 MySQL 验证余额 RMW(InMemory 不强制),需 rowversion/原子 SQL　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:优惠券双用,UsedOrderId 无唯一索引(CouponService.cs:169-208) — BLOCKED 缺真实 MySQL 验证唯一索引(InMemory 不强制),需 EF migration 唯一约束　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:兑换码超兑+先发币后记账失败(CouponService.cs:62-106) — BLOCKED 缺真实 MySQL 验证事务+原子自增(InMemory 不支持事务),需事务重构　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 一致性:下单部分失败不回滚库存/券/币(OrderService.cs:98-157) — BLOCKED 缺真实 MySQL 验证事务(InMemory 不支持事务),需事务包裹多步写入　复核注记：2026-11-04 复核仍缺外部条件
- [x] 深审 P1 校验:CoinDeduction 不封顶且 plan 的 CanUseCoin/CanUseCoupon 未强制(OrderService.cs:136-152)(证据:d24107c/b9bf2f6,OrderService.cs:87、OrderService.cs:154)
- [ ] 深审 P1 并发:退款先打款后复查致双退(OrderService.cs:287-328) — BLOCKED 缺真实 MySQL 验证原子退款声明(InMemory 不强制),需先声明后打款的事务/条件更新　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:手机/邮箱无唯一约束致重复注册(AuthService.cs:85-90) — BLOCKED 缺真实 MySQL 验证唯一索引对既有数据无冲突(InMemory 不强制),需 EF migration 唯一约束　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:refresh token 轮换竞态,双 token 有效(AuthService.cs:223-262) — BLOCKED 缺真实 MySQL 验证原子撤销(WHERE RevokedAtUtc IS NULL)(InMemory 不强制),需条件更新　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:签到双发,MakeupOrderNo 无唯一约束(SignService.cs:53-99) — BLOCKED 缺真实 MySQL 验证唯一索引(InMemory 不强制),需 EF migration 唯一约束　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:邀请一次性竞态(InviteService.cs:38-53) — BLOCKED 缺真实 MySQL 验证原子认领(InMemory 不强制),需条件更新/唯一约束　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 并发:roll 重复加入无唯一约束(RollService.cs:83-115) — BLOCKED 缺真实 MySQL 验证唯一索引(InMemory 不强制),需 EF migration 唯一约束　复核注记：2026-11-04 复核仍缺外部条件
- [ ] 深审 P1 可靠性:webhook 幂等先声明,处理失败丢事件(VaporEndpoints.cs:71-93) — BLOCKED 缺真实 MySQL 验证幂等声明与处理事务一致性(InMemory 不强制),需 outbox/事务　复核注记：2026-11-04 复核仍缺外部条件
- [x] 深审 P2 安全:NewsDetailPage/ArticlePage 未消毒 HTML 渲染(NewsDetailPage.tsx:44、ArticlePage.tsx:37;新闻已在服务端消毒 NewsStore.cs:62,文章为运营所写,降为纵深防御)(证据:a69c6a7,web/src/pages/NewsDetailPage.tsx:44、ArticlePage.tsx:37)
- [x] 深审 P2 安全:token 存 localStorage(client.ts:14-17)(证据:87dba79,web/src/api/client.ts:5、server/src/Doggo.Api/Endpoints/AuthCookies.cs:20;AuthApiTests.cs:96)
- [x] 深审 P2 部署:nginx admin IP 白名单默认注释(doggo.conf.template:82-91)(证据:doggo.conf.template:83、doggo.conf.template:88;docs/guide/deployment.md:54)
- [x] 深审 P2 部署:dev compose 发布 MySQL/无密 Redis(证据:docker-compose.yml:12-13、docker-compose.yml:26-27;docker compose config host_ip=127.0.0.1,ps 仅 127.0.0.1:13306/16379,dotnet test 107/107 绿)
- [x] 深审 P2 安全:banner linkUrl 无 scheme 白名单(NewsListPage.tsx:47-52)(证据:a69c6a7,web/src/pages/NewsListPage.tsx:47、web/src/lib/url.ts:3)
- [x] 深审 P2 安全:admin token 存 localStorage(app.tsx:16-18)(证据:87dba79,admin/src/app.tsx:11、admin/src/pages/Login/index.tsx:26)
- [x] 深审 P2 安全:roll 开奖用 Random.Shared 非加密(RollService.cs:162)(证据:bfb6fe4,AliasMethod.cs:103、RollService.cs:163)
- [x] 深审 P2 正确性:漏斗聚合写出 to 之后的行(FunnelAggregator.cs:26-77)(证据:a69c6a7,FunnelAggregator.cs:104)
- [x] 深审 P2 正确性:行情日线混币种无换算(MarketStore.cs:161-230)(证据:a69c6a7,MarketStore.cs:199、MarketEntities.cs:36;MarketStoreTests.cs:149)
- [ ] 深审 P2 并发:重复提交取回双发 trade offer(OrderService.cs:406-463) — BLOCKED 缺真实 MySQL 验证原子状态迁移(InMemory 不强制),需条件更新进入 Retrieving　复核注记：2026-11-04 复核仍缺外部条件
- [x] 深审 P2 正确性:SteamOpenId 状态过期时间按本地时区解析(SteamOpenIdService.cs:129-130)(证据:a69c6a7,SteamOpenIdService.cs:130)
- [x] 深审 P2 正确性:GroupBuyingView 对重复 OrderId 的 ToDictionary 抛异常(CaseEndpoints.cs:61、331)(证据:a69c6a7,CaseEndpoints.cs:87)
- [ ] 深审 P2 并发:同模板双开房(RollService.cs:273-309) — BLOCKED 缺真实 MySQL 验证唯一约束(InMemory 不强制),需 EF migration 唯一索引　复核注记：2026-11-04 复核仍缺外部条件
- [x] 深审 P3 卫生:axios 0.27.2 传递依赖(证据:admin/pnpm-workspace.yaml:6 override axios=1.20.0、admin/package.json:23 pnpm.overrides 同步、admin/pnpm-lock.yaml:2147 锁定 axios@1.20.0;pnpm build 编译成功,Umi v4.7.23 webpack 无冲突)
- [x] 深审 P3 卫生:mysqladmin 密码作 CLI 参数(证据:docker-compose.yml:17、docker-compose.prod.yml:19 改 `--defaults-extra-file=<(printf '[client]...')`;docker compose config 通过,容器 healthy,dotnet test 184+107 全绿)
<!-- 审核发现:CI 硬编码凭据(mobile.yml:86-88、104-106)不属实:该凭据仅为 CI 验收 runner 内临时 MySQL/API 的测试夹具与 seed 数据,非真实生产凭据,无泄露风险,无需改 -->
- [x] 深审 P3 卫生:小程序 RichText 注入(证据:miniapp/src/pages/newsdetail/newsdetail.tsx:58、miniapp/package.json:35 xss@^1.0.17;filterXSS stripIgnoreTagBody 剥离 script/iframe/svg 等,typecheck+build 绿)
- [ ] 深审 P3 卫生:小程序 urlCheck false(project.config.json:8) — BLOCKED 缺正式 AppID 与微信后台合法域名配置(改 true 需同批上线域名,属用户侧资产,todo.md:384 提审已 BLOCKED)　复核注记：2026-11-04 复核仍缺外部条件
- [x] 深审 P3 卫生:http 默认 API base(证据:shared/src/client.ts:8 默认空+rawRequest fail-closed、miniapp/config/index.ts:20 env 注入 TARO_APP_API_BASE;miniapp build 注入 http://127.0.0.1:8082,typecheck+build 绿;mobile 保留模拟器默认经 setApiBase)
- [x] 深审 P3 卫生:nginx 缺安全头(证据:doggo.conf.template:48-50、doggo.conf.template:82-84 add_header nosniff/referrer-policy/frame-deny;nginx -t 语法 ok,curl 实测三头均返回)
- [x] 深审 P3 卫生:inspectLink 无 scheme 白名单(CaseOpeningPage.tsx:108)(证据:a69c6a7,CaseOpeningPage.tsx:108、web/src/lib/url.ts:3)
- [x] 深审 P3 卫生:appsettings 默认 DB 密码占位(证据:DoggoInfrastructureExtensions.cs:22-38 生产 fail-closed(空或 change-me 拒绝),Development/Testing 豁免;新增 DoggoInfrastructureExtensionsTests.cs 5 例,dotnet test 189+107 全绿)
- [x] 深审 P3 卫生:订单号用 Random.Shared(证据:OrderService.cs:733-734 改 RandomNumberGenerator.GetInt32;dotnet test 45+189+107 全绿)
- [x] 深审 P3 卫生:VaporClient HttpResponseMessage 未释放(证据:VaporClient.cs:80、VaporClient.cs:109 改 using var + SendAsync 统一异常翻译;dotnet test 45+189+107 全绿)

- [x] 审核发现：SteamOpenIdService 经 AddHttpClient 注册为 transient（Program.cs:34），无 Auth:SecretKey 时 _signingKey 按实例随机生成（SteamOpenIdService.cs:25-28），登录与回调为两次请求故密钥不同，state 校验必败；注释「镜像单例 TokenService」不成立（Program.cs:32、SteamOpenIdService.cs:22-26）(证据:9c54dd4, SteamOpenIdService.cs:20)
- [x] 审核发现：Case 座位 claim/open/advance/paid/release 仅校验「已登录」未校验 seat/order 归属，任意登录用户可对他人的 seat 执行 paid/release/open/advance 或用他人 orderId claim（CaseEndpoints.cs:87、118-121、204；仅 result 于 :171 用 SeatOwnedByAsync 校验）(证据:75abde8, CaseEndpoints.cs:108、CaseEndpoints.cs:149、CaseEndpoints.cs:190、CaseEndpoints.cs:240、CaseEndpoints.cs:281)
- [x] 审核发现：CreateOrderRequest.UserId、JoinRoomRequest.UserId、ClaimRoomRequest.UserId 已改由 token 提供但仍留在请求契约，易误导客户端传他人 id（OrderEndpoints.cs:293、RollEndpoints.cs:269、271）(证据:896cfa2, OrderEndpoints.cs:292、RollEndpoints.cs:269、RollEndpoints.cs:271)
- [x] 审核发现：支付回调金额不符拒绝分支无对应测试（OrderService.cs:197），现有用例仅覆盖 success:false 与等额成功（OrderApiTests.cs:192、142）(证据:438e7f7, OrderApiTests.cs:204)
- [x] 审核发现：代码深审报告「当场修复 9 条」分解自相矛盾，P1×6 列 S4/S5/S6/S8/S9/S10/S11 共 7 个 id，且「归属面」与 P1 项重复计数（docs/review/2026-11-01-code-review.md:150）(证据:c7a46ab, 2026-11-01-code-review.md:150)

- [ ] 审核发现：CaseEndpoints 新增归属校验（SeatOwnedByAsync/OrderOwnedByAsync）为 check-then-act，与 GroupBuyingService 变更非原子：paid/release/open/advance 中 seat 在「校验通过」与「变更执行」之间被 release 并由他人 re-claim，过期请求可变更新 owner 的 seat（如免支付 MarkPaid、把他人已付 seat 置回 OnSale）——归属强制应并入变更事务/行锁（CaseEndpoints.cs:149、240、281；GroupBuyingService.cs:196、217、233、359） — BLOCKED 缺真实 MySQL 验证行锁/事务(InMemory 不支持事务),需行锁+变更并入同一事务　复核注记：仍阻塞——CaseEndpoints.cs:154/195/245/286 归属校验仍为独立读，GroupBuyingService 各变更方法（ReleaseSeatAsync/MarkSeatPaidAsync/SubmitOpeningAsync/AdvanceSeatAsync/ApplyOpeningResultAsync）仍直接 SaveChanges 无事务/行锁包裹；测试仍用 EF InMemory（ApiFactory.cs:49），不支持事务且不强制行锁，无真实 MySQL 并发验证
- [x] 审核发现：claim 新归属门仅校验 order 归属，ClaimSeatAsync 从不校验 order 状态（PendingPayment）或 order 产品与 batch 匹配，可用已取消/未支付或他人产品的 order 锁定 seat；新测试仅 seed PendingSettlement order（GroupBuyingApiTests.cs:78）掩盖该缺口（CaseEndpoints.cs:108；GroupBuyingService.cs:153-193）(证据:b9bf2f6,GroupBuyingService.cs:164、GroupBuyingService.cs:170;GroupBuyingApiTests.cs:243)
- [x] 审核发现：/open（SubmitOpening）归属校验（CaseEndpoints.cs:149）无对应测试——Mutations_reject_other_users_orders_and_seats 仅断言 paid/release/advance 外来拒绝，未覆盖 /open；该外来 404 无需 vapor 即可断言（归属校验先于 vapor 检查 GroupBuyingService.cs:235），应补（GroupBuyingApiTests.cs:233-239）(证据:b9bf2f6,GroupBuyingApiTests.cs:240)
- [x] 审核发现：code-review 报告「当场修复 9 条」修正后仍不收敛：P0 6−2=4≠3、P1 19−7=12≠13（待处理 P0 3/P1 13 与发现 P0 6/P1 19 对不上），归属面重新分类后「发现」计数行未同步（docs/review/2026-11-01-code-review.md:149-151）(证据:529963b,docs/review/2026-11-01-code-review.md:151)

## 审核记录：d9d679f6..HEAD（7 commits：a5688da/e932608/0fa62f8/6d58d2b/b722e22/1e24a92/d24107c）

- [x] 审核发现：新旗标门未校验 plan 归属——CanUseCoupon/CanUseCoin 只在 `plan is { CanUseCoupon:false }` 时生效，而 plan 仅按 PlanId 加载（OrderService.cs:83-85），未校验 Enabled 或 ProductId==ProductId（仅 OptionId 非空时 LoadPlanOptionAsync 才校验）。客户端传一个他产品/禁用/不存在的 PlanId 且不带 OptionId，即可绕过新旗标门，同时仍按该 plan.CashbackBps 计返现（OrderService.cs:116）——旗标强制对「无 OptionId 的下单」形同虚设（OrderService.cs:89-96）(证据:b9bf2f6,OrderService.cs:87、OrderService.cs:672;OrderServiceTests.cs:251)
- [x] 审核发现：新 cap 校验触发路径会持久化半成品订单行——OrderService.cs:156 抛异常后 catch（164-168）调 RestoreCouponAsync，其内部 SaveChangesAsync 会顺带 flush 掉 line 138 `db.UserOrders.Add(order)` 尚未保存的 order 实体，残留一条 PendingPayment/TotalCostCents=0 的幽灵订单（券已归还但订单落库）。该副作用对「余额不足」旧路径同样存在，本 diff 新增 line 154/156 触发点并明确依赖该 catch（OrderService.cs:154-168）(证据:b9bf2f6,OrderService.cs:171;OrderServiceTests.cs:301)
- [x] 审核发现：Coin_deduction_above_the_order_total_is_rejected 未 seed 币余额——若移除 cap 校验，SpendAsync 仍抛 CoinValidationException（余额不足）且同样映射 400（OrderEndpoints.cs:75-77），该用例无法区分「超出总额」与「余额不足」两种拒绝，守护不了新逻辑（OrderApiTests.cs:400）(证据:b9bf2f6,OrderApiTests.cs:400)
- [x] 审核发现：未覆盖「券已消耗后币扣超剩余」的拒付分支，且未断言拒付后券被归还（RestoreCouponAsync catch 路径）；Coin_deduction_within_the_total_is_applied 仅断言订单字段，未断言币账余额实际减少（OrderApiTests.cs:419）(证据:b9bf2f6,OrderApiTests.cs:433、OrderApiTests.cs:446)
- [x] 审核发现：负值 CoinDeduction 被静默忽略（OrderService.cs:148 `>0` 门），请求携带负数既不拒付也不扣减，客户端期望与结果不符（OrderService.cs:148）(证据:b9bf2f6,OrderService.cs:87;OrderServiceTests.cs:286)
- [x] 审核发现：d24107c 已实现 cap+旗标强制，但对应待办「深审 P1 校验:CoinDeduction 不封顶且 plan 的 CanUseCoin/CanUseCoupon 未强制(OrderService.cs:136-152)」仍保持未勾选且未标注已修，且行号引用已过期（实际 cap 校验在 OrderService.cs:154）（todo.md:450）(证据:529963b,todo.md:450)
- [x] 审核发现：docs/审计-文档一致性.md:76-78 注记「（未 commit）」「todo.md 未触碰」与本区间提交状态矛盾——a5688da 已 commit 该文档并修改 todo.md，注记过时（docs/审计-文档一致性.md:76-78）(证据:529963b,docs/审计-文档一致性.md:76)
- [x] 审核发现：本轮 diff 未见问题（d9d679f6..HEAD）——README.md / README.zh.md 为措辞去味与状态计数更新（277/323 与当前 todo.md 计数一致，无误）；其余发现已逐条列于上方。(证据:529963b,d9d679f6..HEAD)

- [x] 审核发现：行情列表 DTO 当前价与日涨跌币种不一致——list 端点 latest 快照查询（MarketEndpoints.cs:95）未按币种过滤，可取到 USD 快照作 current 价，而日涨跌徽标已改为仅 CNY（MarketEndpoints.cs:104），货币分离修复不彻底，应同样过滤 Currency=="CNY"（MarketEndpoints.cs:88-104）(证据:MarketEndpoints.cs:95;MarketApiTests.cs:79)
- [x] 审核发现：GroupBuyingView 奖项按 OrderId 分组仅取最新一条（CaseEndpoints.cs:90-91），一单多奖（同一 order 跨多 seat，见既有多 claim 缺口）时会把不同奖项折叠为一条并赋给同单全部 seat（CaseEndpoints.cs:426），应按 (OrderId, AssetId) 去重而非仅按 OrderId（CaseEndpoints.cs:89）(证据:CaseEndpoints.cs:88;GroupBuyingApiTests.cs:149)
- [x] 审核发现：带 OptionId 下单时 plan 被加载并校验两次——LoadPlanOptionAsync（OrderService.cs:651-686）与 LoadPlanAsync（OrderService.cs:695-710）均查同一 plan，多一次 DB 往返且校验逻辑重复（OrderService.cs:87-88）(证据:OrderService.cs:87;OrderService.cs:656)
- [x] 审核发现：审计-文档一致性.md:96 D-29R 记录「已修：两版 README 同步为 290 of 331」与提交后 README.md:82 实际「297 of 331」不符——a69c6a7 将计数刷新为 297 未同步该记录，复发记录再度过期（docs/审计-文档一致性.md:96）(证据:docs/审计-文档一致性.md:96;README.md:82)
- [x] 审核发现：SeedOrderWithStatusAsync（GroupBuyingApiTests.cs:96）与 SeedPaidOrderAsync（:75）订单号均为「DO{秒级时间戳}0001」固定后缀，同秒创建多单即撞唯一 OrderNo（IX_UserOrders_OrderNo 唯一）；InMemory 不强制唯一故测试未暴露，MySQL 下会失败（GroupBuyingApiTests.cs:88-96）(证据:GroupBuyingApiTests.cs:73;GroupBuyingApiTests.cs:96)
- [x] 审核发现：web tryRefresh 以空 body POST /api/auth/refresh（仅带 Content-Type: application/json，web/src/api/client.ts:53），服务端 refresh 绑定非空 [FromBody] RefreshRequest（AuthEndpoints.cs:119），net10.0 实测空 body 返回 400，导致 web 端 access token 过期后透明刷新必失败、被登出，即使 refresh cookie 仍有效；新测试 Browser_session_uses_httpOnly_cookies_instead_of_a_bearer_header 用 `{}` body（AuthApiTests.cs:101）未覆盖真实空 body 路径，掩盖该缺陷（web/src/api/client.ts:53；AuthEndpoints.cs:119；AuthApiTests.cs:101）(证据:AuthEndpoints.cs:140;AuthApiTests.cs:101;dotnet test 526 绿)
- [x] 审核发现：httpOnly cookie 迁移不彻底——login/register/refresh 对浏览器客户端仍在 JSON 响应体返回 accessToken+refreshToken（AuthResult 含两 token，AuthService.cs:29），XSS 可读登录响应或 POST {} 到 /api/auth/refresh 直接读取两 token；httpOnly 仅防直读 cookie、未防 XSS 经响应体窃取，httpOnly 抗 XSS 目标未达成（AuthEndpoints.cs:69、AuthEndpoints.cs:139；AuthService.cs:29）(证据:AuthEndpoints.cs:28;AuthEndpoints.cs:56;AuthApiTests.cs:104;dotnet test 526 绿)
- [x] 审核发现：admin 白名单 fail-closed 不完整——主域 location /api/（doggo.conf.template:59-66）仍把 /api/admin/* 代理到 API 且无 IP 限制，攻击者可经 https://DOGGO_DOMAIN/api/admin/ 绕过 admin 域的 deny all（doggo.conf.template:93、:98）直达管理端点（仅 role=admin 声明策略兜底，AdminEndpoints.cs:30）；fail-closed 应同时阻断主域 /api/admin/（deploy/nginx/doggo.conf.template:59）(证据:doggo.conf.template:59;doggo.conf.template:63)
- [x] 审核发现：web 端「退出」不再登出——handleSignOut 只调用现为 no-op 的 clearTokens()（web/src/api/client.ts:3）并 setProfile(null)，从不调用服务端 logout()（web/src/api/auth.ts:37，已无任何调用方、成死代码），httpOnly cookie 未清除、refresh token 未吊销；刷新页面后 App.tsx fetchProfile 凭仍有效 cookie 重新登录，「退出」失效（web/src/App.tsx:26）(证据:web/src/App.tsx:25;web/src/api/auth.ts:35;pnpm build 绿)
- [x] 审核发现：ProductDetailPage signedIn 由同步 localStorage 判定改为异步 profile 判定（profile!==null，web/src/pages/ProductDetailPage.tsx:149），profile 尚未加载或 /me 网络失败时已登录用户被误判为未登录，短暂闪现「登录」CTA 且 join 等前置判断失真（web/src/pages/ProductDetailPage.tsx:149）(证据:web/src/ProfileContext.ts:4;web/src/pages/ProductDetailPage.tsx:148;pnpm build 绿)

- [x] 审核发现：ProductDetailPage 登录误判修复不完整——signedIn=profileStatus==='ready'&&profile!==null（ProductDetailPage.tsx:149）仍把 'loading' 态与 /me 网络失败（落 'anonymous'）判为未登录：加载窗口内已登录用户点「购买」被 navigate('/login')（:178-181），且 fetchProfile 网络失败时整会话被锁死为未登录（无法购买/加载优惠券）。App.tsx 导航已用 'loading' 显示「检查登录…」（App.tsx:61-62），ProductDetailPage 未同步区分，todo 该项标记 [x] 但仅部分修复（web/src/pages/ProductDetailPage.tsx:149）(证据:ProfileContext.ts:6;ProductDetailPage.tsx:149,180,184;App.tsx:61;web pnpm build 绿;dotnet test 526 绿)
- [x] 审核发现：nginx 主域 admin 网关未覆盖精确路径 /api/admin——location /api/admin/（doggo.conf.template:61）仅匹配带尾斜杠前缀，请求 /api/admin（无尾斜杠）落入 location /api/（:66）被代理到 API；当前 admin 路由均挂在 /api/admin/ 子路径（AdminEndpoints.cs:29 组根无空路由），无现成绕过，但未来若 admin.MapGet("",…) 即穿透 fail-closed 网关，宜将 location 前缀改为 /api/admin（deploy/nginx/doggo.conf.template:61）(证据:doggo.conf.template:61;AdminEndpoints.cs:29;nginx location /api/admin 前缀无尾斜杠)
- [x] 审核发现：BrowserResult 冗余 context 参数——AuthEndpoints.cs:25 签名 BrowserResult(HttpContext context,…) 的 context 在方法体内从未使用（仅用 result 与 uri），三处调用（:60、:90、:120）均传 context 属冗余，可移除（server/src/Doggo.Api/Endpoints/AuthEndpoints.cs:25）(证据:AuthEndpoints.cs:25,60,90,120;dotnet test 526 绿)
- [x] 审核发现：clearTokens 变 no-op 后仍被导出并调用——client.ts:3 clearTokens(){} 为空实现，auth.ts:36（logout 成功后）与 client.ts:63（tryRefresh 失败）仍调用之，无实际作用且暗示仍有 JS token 需清理，误导后续维护，宜删除函数与调用点（web/src/api/client.ts:3）(证据:client.ts:3,63;auth.ts:36;web pnpm build 绿)
- [x] 审核发现：handleSignOut 吞掉登出失败——App.tsx:29-37 try/finally 中 logout() 网络失败时 finally 仍 setProfile(null) 并 navigate('/')，但服务端未吊销 refresh token、未清 httpOnly cookie，刷新页面后 fetchProfile 凭仍有效 cookie 重新登录，「退出」UI 上看似成功实则未生效（仅乐观隐藏）；建议 logout 成功后再清状态，失败时保留会话并提示（web/src/App.tsx:29-37）(证据:App.tsx:29,33;auth.ts:33;web pnpm build 绿)


## 审核发现：253adaa7..HEAD（1 commit：dbaa6fa）

- [x] 审核发现：/me 失败一律落 'unknown'（App.tsx:22-25 仅 .catch），混淆「401 真未登录」与「网络/服务故障」；ProductDetailPage:184 对 'unknown' 走 fetchProfile 重试而非跳 /login，真未登录用户点「购买」先多打一次注定失败的 /me、被挡在「登录状态暂时不可用」错误而非跳转登录页，相对基线（'anonymous'→直接 navigate('/login')，base ProductDetailPage.tsx:178-181）为回归（web/src/App.tsx:24）(证据:App.tsx:25;ProductDetailPage.tsx:183;web pnpm test 10 绿)
- [x] 审核发现：'unknown' 状态在 App 导航与 ProductDetailPage 语义相反——App.tsx:64 仅判 'loading'，'unknown' 落 else 显示「登录」链接（已登录但 /me 临时失败的会话在导航栏被显示为未登录）；ProductDetailPage:184 却把 'unknown' 当「可恢复会话」发起重试。同一状态两处解读相反，已登录用户 /me 网络抖动时导航显示「登录」而详情页却尝试下单恢复（web/src/App.tsx:64）(证据:App.tsx:99;ProductDetailPage.tsx:183;web pnpm test 10 绿)
- [x] 审核发现：ProfileContext.ts:8 setProfileStatus 参数类型仍为 'loading'|'ready'|'anonymous'，未随 profileStatus 联合新增 'unknown'；当前因 App 用 useState 的 setter（兼容宽联合赋值）编译通过，但 context 接口契约与实际可设值脱节，任何消费方调 setProfileStatus('unknown') 会被 TS 拒绝，接口未同步扩展（web/src/ProfileContext.ts:8）(证据:ProfileContext.ts:8;web pnpm build 绿)
- [x] 审核发现：web 端登录态状态机改动（新增 'unknown'、'loading'/'unknown' 分支、handleSignOut 成功后才清状态）无任何自动化测试——web 无 test 脚本、无 vitest/jest/@testing-library（web/package.json scripts 仅 dev/build/lint/preview，无测试框架），登录态三分支回归只能靠人工验证；服务端 EmptyBodyBehavior/RefreshResult 修复有 AuthApiTests.cs:108 空 body 刷新覆盖（web/src/App.tsx:22,30,64;ProductDetailPage.tsx:177-198）(证据:web/package.json:12;App.test.tsx:1;ProductDetailPage.test.tsx:1;web pnpm test 10 绿)
- [x] 审核发现：新增测试依赖和升级后的插件未同步更新 web/pnpm-lock.yaml，导致 Web Docker 构建中的 frozen-lockfile 安装直接失败（web/package.json:20）(已修：同步 vitest/jsdom/@testing-library/* 与 @vitejs/plugin-react 入锁；隔离 workspace 环境复核 `pnpm install --frozen-lockfile` 通过；web pnpm test 12 绿、build 绿)
- [x] 审核发现：访问令牌过期且刷新接口返回 5xx 时，客户端仍抛出原始 /me 的 401，此处误判为已登出并隐藏会话重试入口（web/src/App.tsx:26）(已修：tryRefresh 返回状态，5xx 抛刷新失败而不是回抛 401；client.test.ts 覆盖 503；web pnpm test 12 绿、build 绿)
- [x] 审核发现：商品页重试会话收到 401 后只跳转登录页，未将共享登录状态设为 anonymous，导致导航栏仍显示会话检查失败且后续购买继续重复检查（web/src/pages/ProductDetailPage.tsx:193）(已修：401 时同步 `setProfile(null)` 与 `setProfileStatus('anonymous')`；ProductDetailPage.test.tsx 断言状态；web pnpm test 12 绿、build 绿)
- [x] 审核发现：代码块仅对 pre 设置换行，但 VitePress 默认样式在内层 code 上显式设置 white-space: pre，长代码行仍不换行，窄屏仍需横向滚动（docs/.vitepress/theme/custom.css:2）(已修：选择器扩为 `.vp-doc div[class*='language-'] pre, ... pre code`，内层 code 继承换行，docs/.vitepress/theme/custom.css:2-5；dd9b8f7；docs build 绿)
- [x] 审核发现：内容宽度覆盖的选择器优先级低于 VitePress 组件的 scoped 样式，带侧栏目录的正文仍限宽 688px、无侧栏正文仍限宽 752/784px，设置的 960px 不生效（docs/.vitepress/theme/custom.css:9）(已修：三处 max-width 补 `!important` 压过 scoped 默认限宽，docs/.vitepress/theme/custom.css:8-14；dd9b8f7；docs build 绿)

- [x] 审核发现：两版 README 的待办统计仍写已完成 328 项，但本次提交中两条 CSS 审核项已勾选，HEAD 实际为 354 项中完成 330 项（README.md:82；README.zh.md:82）(已修：两版 README 计数随本行勾选重算为 331 of 355／已完成 331 项；grep 复核 `[x]`=331、`[ ]`=24；同 commit)
- [x] 审核发现：本轮 diff 未见问题（6180452e..HEAD）(复核属实：范围仅 e8db18a 三文档计数更新，README 331/355 与当时 331 勾、24 未勾一致；无代码改动)
