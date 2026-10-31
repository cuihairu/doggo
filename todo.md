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
  - 复核 2026-10-16(PATROL1016):仍无用户裁决记录,维持已裁决搁置,不重开;解锁=用户明确拍板口径
  - 代决 2026-10-16(FW1016D):前台只暴露聚合中位数,不暴露逐站点明细;记录见 docs/design/price-crawler-design.md §12
  - 证据:GET /api/market/items/{id}/reference + MarketApiTests 4 例(Api.Tests 96 例全绿);web 行情趋势页对照块(pnpm build 绿)
  - 已代决:按 docs/review/2026-10-30-decisions.md 执行(2026-10-30)(维持原裁决,解锁=用户拍板口径)

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
- [x] 文档首页：按功能配截图预览（来源：用户 2026-10-14）
  - 要求：把界面**按功能**截图，放在**文档首页做预览**，**按功能逐项介绍**（不是一个大图了事）
  - 验收：每个主要功能一张截图 + 一句说明；截图取自**真实运行的界面**，不是示意图；首页能直接看到
  - 证据：2180556（web 10 张）/ 956d9b0（admin 9 张）/ 9668bf1（mobile 7 张）；pnpm docs:build ×3 全绿
- [x] 统一 TODO 文件名为小写 todo.md并修齐全仓引用与中英审计文档（2026-10-28，本次提交）
  - 证据：README 中英、docs/guide 中英、PR 模板、两审计文档 D-11 的 TODO.md→todo.md 引用已同步；`git grep 'TODO\.md'` 无残留；仓库根仅存 todo.md

## 未完成项

- [ ] vapor 联调:需可用的 vapor 实例与 Steam 账号 — BLOCKED 缺可用的 vapor 实例与 Steam 账号（Vapor:BaseUrl/ApiKey 仍为空，server/src/Doggo.Worker/appsettings.json:13-14）
  - 复核 2026-10-16(PATROL1016):appsettings 中 Vapor:BaseUrl/ApiKey 仍为空,凭据未到位;解锁=用户提供可用的 vapor 实例与 Steam 账号
  - 已代决:按 docs/review/2026-10-30-decisions.md 执行(2026-10-30)(需用户输入,暂缓)
- [ ] 小程序提审:需主体资质、正式 AppID 与合法域名 — BLOCKED 缺主体资质、正式 AppID 与合法域名（miniapp/project.config.json:2 仍为 touristappid 占位）
  - 复核 2026-10-16(PATROL1016):miniapp/project.config.json 的 appid 仍为 touristappid 占位;解锁=用户提供主体资质、正式 AppID 与合法域名
  - 已代决:按 docs/review/2026-10-30-decisions.md 执行(2026-10-30)(需用户输入,暂缓)
- [x] docs 双语补齐(2026-10-16 执行单;约定:中文保基名,英文置于 `docs/en/` 并镜像同路径,两份首 3 行内放语言切换器;排除 CHANGELOG/CONTRIBUTING/LICENSE/CODE_OF_CONDUCT/SECURITY)
  - [x] docs/ 根目录:审计-文档一致性.md 已配 docs/en/审计-文档一致性.md(真实英译)
  - [x] docs/index.md:用户已代决=站点级 i18n(config.mts 加 `locales.{root,en}`,英文首页挂 /en/;home 页不塞逐页切换器,语言切换走站点级导航 `VPNavBarTranslations`,两个 locale 各声明 label 即出现)。复核注:home 布局正文实际会渲染(dist/index.html 的 `.vp-doc` 内含「界面预览」与截图 `<img>`),原「不渲染正文、切换器无处放」的判断有误;按代决执行,不影响验收
  - [x] docs/guide/ 6 篇(introduction/features/architecture/getting-started/deployment/miniapp-compliance)配英文版(真实英译,已移至 docs/en/guide/,切换器指向 /en/ 路径)
  - [x] docs/design/ 7 篇(overview/data-model/case-opening/legacy-analysis/ui-system/price-crawler-design/analytics-funnel-design) + prototypes/README.md 配英文版(真实英译,均低于执行单 1000 行门槛;已移至 docs/en/design/);全部中文原文补切换器(首 3 行内)
  - [x] README.md L93-101「本地测试口径」节为中文正文,违反「README 不混中文」红线:README.md 改英文节(Local testing by change type),README.zh.md 补对应中文节;README.md L87 残留中文专名(vapor 联调、小程序提审)改英文并刷新计数
  - [x] 收尾门禁:pnpm docs:build 绿,/en/ 下 15 页与 /en/ 首页全部产出,nav 切换器在内容页渲染并指向镜像路径;顺手修 nav logo 路径(原 `${base}logo.svg` 经 withBase 拼成 /doggo/doggo/logo.svg 404,改回 /logo.svg,与其他仓一致)
  - [x] 2026-10-24 按舰队约定翻转(e4b0b9a):英文上根、中文移 `docs/zh/`、删 `docs/en/`,仅用 VitePress 官方 `locales`(zh 带 `link: /zh/`),无逐页切换器、无 .zh.md 镜像;根与 /zh/ 各 16 页镜像产出,站内 md 与 nav 链接死链 0

## 审核发现

- [x] 审核发现：中英文项目状态仍写“469 server tests green”，但新增清单的 M12 最新证据已记录“全仓 500 绿”，当前状态数字互相矛盾（docs/guide/introduction.md:27、docs/zh/guide/introduction.md:27、todo.md:288）— 属实，docs 双语更新为 504 全仓测试绿（dotnet test 实测 504=81+103+45+179+96，证据 docs/guide/introduction.md:27、docs/zh/guide/introduction.md:27）
- [x] 审核发现：提交标题仅称完成小写迁移，实际同时新增 380 行里程碑清单并把 README 计数改为 239/241，提交说明未覆盖这些实质变更（README.md:78、todo.md:3）— 属实，README 计数修正为实际 todo.md 条目（234 完成 + 2 外部依赖 = 236 总，证据 README.md:78、todo.md:390）
- [x] 审核发现：a285f53..HEAD 未发现问题
- [x] 审核发现：本范围新增第 3 条审核记录后，todo.md 实际为 235 完成 + 2 未完成 = 237 总，但 README.md:78 仍写「234 of 236 checklist items」，计数过期不一致（README.md:78、todo.md:392）— 属实，README 计数修正为勾选本条后的实际值 236 完成 + 2 外部依赖 = 238 总（todo.md 顶层级实测 236 完成 + 2 未完成），证据 README.md:78、todo.md:393
- [x] 审核发现：决策文档引用的 todo.md 行号过期——同批在条目上方插入 3 行「已代决」后，vapor/小程序条目已移至 todo.md:376/379，文档仍写 todo.md:375/377（分别指向空白行/复核行）（docs/review/2026-10-30-decisions.md:9、docs/review/2026-10-30-decisions.md:37、docs/review/2026-10-30-decisions.md:97）— 属实，文档 todo.md 引用更新为 376/379，证据 docs/review/2026-10-30-decisions.md:9、docs/review/2026-10-30-decisions.md:37、docs/review/2026-10-30-decisions.md:96（条目 3 的 todo.md:333-336 引用已核验仍有效，未改）
- [x] 审核发现：决策文档 appsettings 行号错位——BaseUrl 实际在 server/src/Doggo.Worker/appsettings.json:13、ApiKey 在 :14，文档却写 BaseUrl:14/ApiKey:15（docs/review/2026-10-30-decisions.md:11）— 属实，文档改为 BaseUrl:13/ApiKey:14，证据 docs/review/2026-10-30-decisions.md:11
- [x] 审核发现：决策文档条目 2 与条目 3 之间出现连续两个「---」分隔线（docs/review/2026-10-30-decisions.md:62、docs/review/2026-10-30-decisions.md:64）— 属实，删除多余分隔线，仅留一条（docs/review/2026-10-30-decisions.md:62）
- [x] 审核发现：提交标题仅称修复决策文档待办引用，实际同时把 README 计数从 236/238 改为 239/241 并在 todo.md 末尾追加 3 条审核发现记录（todo.md:397-399），提交说明未覆盖这些实质变更（README.md:78、todo.md:397）— 属实，历史提交 bf9a532 的 diff 同时修改 README.md 与追加 todo.md:397-399，不能安全改写已推送提交；本条补记并同步 README 计数为勾选后实际值 240/242，证据 bf9a532、README.md:78、todo.md:397-400
- [x] 审核发现：提交 074d27e 标题仅称「record audit finding verification」，实际同时把 README 计数从 239/241 改为 240/242，提交说明未覆盖 README 计数变更（README.md:78、todo.md:400）— 属实，历史提交 074d27e 的 diff 同时修改 README.md 与 todo.md，不能安全改写已推送提交；本条补记并同步 README 计数为勾选后实际值 241/243，证据 074d27e、README.md:78、todo.md:401
- [x] 审核发现：本轮 diff 未见问题（074d27e1..HEAD）— 属实，074d27e1 唯一解析为 074d27e，该范围仅含 2f58fe3；git diff 未见实质问题且 README/todo 计数一致（241/243），证据 git diff --check 074d27e..HEAD、git show 2f58fe3 --stat、todo.md:241 checked/242 unchecked、dotnet test --no-build 实测 504 绿（81+103+45+179+96）
- [x] 审核发现：本轮 diff 未见问题（2f58fe3a..HEAD）— 属实，2f58fe3a..HEAD 仅含 edfeaad；git diff --check 2f58fe3a..HEAD 干净且 README/todo 计数一致（242/244），证据 git log --oneline 2f58fe3a..HEAD、git diff --check 2f58fe3a..HEAD、README.md:78、todo.md:242 checked/2 unchecked、DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --no-build 实测 504 绿（81+103+45+179+96）
- [x] 审核发现：审计报告声称修复后 18 个「路由 × 视口」组合全部 overflow=false，但同目录截图仅 17 张且未给出逐组合的路由×视口清单与实测数值，证据与结论无法一一复核（docs/review/2026-10-31-ui-audit-impeccable.md:21、docs/review/2026-10-31-ui-audit-impeccable.md:36、docs/review/2026-10-31-ui-audit-impeccable.md:92）— 属实，审计报告已补「复核矩阵」节：18 个「路由 × 视口」逐组合清单与实测数值（clientWidth/scrollWidth/overflow 均 false），18/18 全 false，证据 docs/review/2026-10-31-ui-audit-impeccable.md:94-117、docs/review/2026-10-31-ui-audit-impeccable.md:119、Playwright 1.63 实测输出
- [x] 审核发现：审计报告「健康评分 78/100」未明确是修复前基线还是修复后复评，与正文「核心短板已清、仅剩品牌主色与春主题边缘项」的表述口径不清晰（docs/review/2026-10-31-ui-audit-impeccable.md:6、docs/review/2026-10-31-ui-audit-impeccable.md:96）— 属实，审计报告已补口径：78/100 为审计时（修复前）基线，并新增「修复后复评」92/100 与遗留项说明，证据 docs/review/2026-10-31-ui-audit-impeccable.md:6、docs/review/2026-10-31-ui-audit-impeccable.md:119
- [x] 审核发现：签到日期 chip 仍用 var(--primary) 小字置 var(--primary-weak)，同 diff 已把 .btn.secondary/.rarity-tag/.theme-opt 换成 --primary-strong，此处漏改致 AA 不达标（春 2.59、夏 2.78、灯笼 2.71、秋 3.60、冬 3.75、国庆 3.89，均 <4.5:1）（web/src/pages/UserCenterPage.tsx:228）— 属实，chip 已改 var(--primary-strong)；修复后对比度 spring 4.53/summer 4.52/lantern 4.51/autumn 4.52/winter 4.55/national 4.56 均 ≥4.5:1，证据 web/src/pages/UserCenterPage.tsx:228、web/src/tokens.css:43 等、WCAG 对比度实测
- [x] 审核发现：本提交标题「pass wcag aa on primary text and buttons」只修 web，移动端同源问题未修——mobile/src/ui.tsx 按钮填充/文字（:42、:55）与 Price（:122）仍用 t.primary/t.primaryText，春主题 primary #e0688f 对白字 3.21:1 <4.5:1（mobile/src/ui.tsx:42、mobile/src/ui.tsx:55、mobile/src/ui.tsx:122）— 属实，mobile/src/theme.ts 新增 primaryStrong 令牌（各主题取与 web tokens.css 同值的 AA 深色档，spring #c2285a），ui.tsx 按钮填充/文字与 Price 改用 t.primaryStrong；修复后 spring primaryStrong #c2285a 对白字 5.61:1 ≥4.5:1，证据 mobile/src/theme.ts:12、mobile/src/ui.tsx:42、mobile/src/ui.tsx:55、mobile/src/ui.tsx:122、WCAG 对比度实测
- [x] 审核发现：tokens.css 头注称值与 docs/design/prototypes/tokens.css 一致，但本次只改 web/src/tokens.css 与 mobile/src/theme.ts，未同步设计源——docs/design/prototypes/tokens.css:225 仍为 spring --muted #8a6d7c 且无 --primary-strong，造成源/实现漂移（web/src/tokens.css:1、docs/design/prototypes/tokens.css:225）— 属实，设计源 docs/design/prototypes/tokens.css 已同步：各主题补 --primary-strong（与 web/src/tokens.css 同值），spring --muted 改 #876a79，且 d13150c 未同步的其余 6 主题 muted 一并同步，证据 docs/design/prototypes/tokens.css:7、docs/design/prototypes/tokens.css:233、docs/design/prototypes/tokens.css:236
- [x] 审核发现：同为价格主色文本，本次把 ProductDetailPage:345/ProductListPage:163/UserCenterPage:187 换成 --primary-strong，却漏改 ProductDetailPage:231 与 OrderPayPage:80/164（仍 var(--primary)），同类语义混用两枚令牌（这些为 20-22px 粗体大字号，≥3:1 大文本 AA 尚达标，但转换不彻底）（web/src/pages/ProductDetailPage.tsx:231、web/src/pages/OrderPayPage.tsx:80、web/src/pages/OrderPayPage.tsx:164）— 属实，三处已改用 var(--primary-strong)，价格主色文本全站统一为 --primary-strong；证据 web/src/pages/ProductDetailPage.tsx:231、web/src/pages/OrderPayPage.tsx:80、web/src/pages/OrderPayPage.tsx:164

## 审核发现 2026-10-31

- [x] 审核发现：tokens.css 头注「--primary-strong：比 --primary 更深/更稳」与取值不符——dark 主题 --primary-strong #709af1 比 --primary #6a96f0 更亮（暗底需更亮档达 AA，与「更深」相反），brand/cny/midautumn 三主题为 --primary-strong: var(--primary)（同值、未更深），仅 7/11 主题真正更深（docs/design/prototypes/tokens.css:7、docs/design/prototypes/tokens.css:56、docs/design/prototypes/tokens.css:104、docs/design/prototypes/tokens.css:182）— 属实，头注已改为「按主题明暗方向调整 --primary」，覆盖 dark 更亮与三主题同值的情况，证据 docs/design/prototypes/tokens.css:7
- [x] 审核发现：todo.md:408 新记审核发现称「d13150c 未同步的其余 5 主题 muted 一并同步」，但本 diff 实际同步 6 个非 spring 主题的 --muted（lantern/dragonboat/national/summer/autumn/winter，连同 spring 共 7 个），「其余 5 主题」应为「其余 6 主题」，计数少一（todo.md:408、docs/design/prototypes/tokens.css:127、docs/design/prototypes/tokens.css:153、docs/design/prototypes/tokens.css:207、docs/design/prototypes/tokens.css:233、docs/design/prototypes/tokens.css:261、docs/design/prototypes/tokens.css:287、docs/design/prototypes/tokens.css:313）— 属实，todo.md:408 已改为「其余 6 主题」，证据 todo.md:408、git show c287e55 -- docs/design/prototypes/tokens.css 实测 7 处 --muted 变更（spring+6）
- [x] 审核发现：审计报告新增「修复后复评 92/100」仍列「品牌主色前景 AA」为遗留项（且结论节称其为唯一拦在达标门前的待拍板项），但本提交即采纳其建议的「加法式 --primary-strong」并把 web 价格/按钮/chip 与 mobile 按钮/Price 前景改用它，8/11 主题（primaryStrong≠primary）的该项 AA 已获解决——复评遗留项清单未随本提交更新，口径前后矛盾/过时（docs/review/2026-10-31-ui-audit-impeccable.md:119、docs/review/2026-10-31-ui-audit-impeccable.md:123）— 属实，审计报告「修复后复评」与「结论」已同步：遗留清单收敛为 11/11 主题 --primary-strong 对文本 ≥4.5:1（AA 已解决）+ 春主题 --muted 4.52:1（达标），不再列待拍板项，证据 docs/review/2026-10-31-ui-audit-impeccable.md:119、docs/review/2026-10-31-ui-audit-impeccable.md:123、WCAG 对比度实测
- [x] 审核发现：README 计数在中转提交 a73c0c0 写「251 of 253 checklist items」，但该提交 todo.md 实为 251 完成 + 3 未完成 = 254 条（其中第 3 条未完成是本次新加的待验证审核发现，且该提交未在 README 反映 +2 总数增量），计数口径瞬时不一致（README.md:78、todo.md:411-415）— 属实，README 计数已对齐 todo.md 实测口径（253 完成 + 3 未完成 = 256 条，未完成含 2 条外部依赖 BLOCKED 与 1 条待验证审核发现），证据 README.md:78、todo.md:416、git show a73c0c0:todo.md 实测 251+3=254 条
- [ ] 审核发现：docs/review/2026-10-31-ui-audit-impeccable.md 第 62-76 行「遗留问题（记录，未改动）」节仍把品牌主色前景 AA 与春主题 --muted 列为未解决遗留（P1/P3），与同文件第 119/123 行「已随 c287e55/fa5a8aa 解决、达标门前不再有待拍板项」前后矛盾，该遗留节未随本 diff 收敛（docs/review/2026-10-31-ui-audit-impeccable.md:62、docs/review/2026-10-31-ui-audit-impeccable.md:64、docs/review/2026-10-31-ui-audit-impeccable.md:72）。
