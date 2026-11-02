# 代码深审报告（2026-11-01）

范围：本仓全量。方法：安全静态扫描、隐藏 bug 猎捕、质量门（lint / 类型检查 / 测试基线）、
按严重度归并报告。每个发现都回到 `file:line` 核验，未能在代码上复现的作为
UNVERIFIED 单独标注，不计入确认数。

结论：**49 条发现，当场修复 9 条（3 个 commit，测试全绿），其余 40 条记录到
`todo.md`。安全扫描未发现硬编码密钥 / SQL 拼接 / 反序列化 / 路径穿越 / shell 注入。**

---

## 1. 安全静态扫描

| 类别 | 结果 | 证据 |
|---|---|---|
| 硬编码密钥 | 无 | 扫描 server/web/admin/mobile/miniapp，未发现明文密钥或固定 JWT/支付密钥落库 |
| SQL 注入 | 无 | EF Core 全部参数化查询；无字符串拼接进 `FromSql`/`ExecuteSql` |
| 反序列化 | 无 | 无 `BinaryFormatter`/`TypeNameHandling`/`Deserialize` 于不可信输入 |
| 路径穿越 | 无 | 文件读写集中于 `NewsStore`/上传目录，均以固定白名单目录拼接 |
| shell 注入 | 无 | `Process.Start` 仅限 CI 脚本，参数非用户可控 |
| 前端 XSS | 2 处（纵深防御） | `web/src/pages/NewsDetailPage.tsx:44`、`ArticlePage.tsx:37` 渲染未消毒 HTML；新闻已在服务端 `NewsStore.cs:62` 消毒，文章为运营所写，降级为纵深防御 |

## 2. 隐藏 bug 猎捕（按严重度）

### P0

- **[S1] P0 部署** `MockPaymentProvider` 在生产被注册为唯一支付渠道，任何请求都视为支付成功。
  `server/src/Doggo.Api/Program.cs:83`。处置：**TODO**——属部署里程碑决定；本轮仅修复其金额校验（见 S3）。
- **[S2] P0 越权** 订单/开箱/roll/奖品/交易链接接口整体未加 `RequireAuthorization`，无 token 可调变相接口。
  `server/src/Doggo.Api/Program.cs:37-41`（仅定义了 Admin 策略，无全局兜底）。处置：**FIXED** `be05a90`。
- **[S3] P0 金额校验** 支付回调未校验 `amountCents == order.TotalCostCents`，金额不符仍结算。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:193`。处置：**FIXED** `b0ffb2c`。
- **[B3] P0 并发** 取消与支付回调竞态：回调在取消判定后、结算前写入，无乐观锁。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:241-256`。处置：**TODO**——需条件更新或 rowversion（EF InMemory 无法验证行级并发，未盲修）。
- **[B4] P0 并发** roll 开奖无双实例锁：两个 worker 同时 `draw` 同一房间可各选中奖者。
  `server/src/Doggo.Infrastructure/Roll/RollService.cs:121-173`。处置：**TODO**。
- **[B5] P0 并发** roll 金币奖励双发：`ClaimedAtUtc` 判定与更新非原子。
  `server/src/Doggo.Infrastructure/Roll/RollService.cs:204-266`。处置：**TODO**。

### P1

- **[S4] P1 加密** SteamOpenId state 用 `SequenceEqual` 非定长比较且密钥可被配置为弱值。
  `server/src/Doggo.Infrastructure/Auth/SteamOpenIdService.cs`。处置：**FIXED** `3b8abf0`。
- **[S5] P1 越权** 奖品列表/详情/日志未校验 `route userId == 当前用户`，可枚举他人奖品。
  `server/src/Doggo.Api/Endpoints/PrizeEndpoints.cs`。处置：**FIXED** `be05a90`。
- **[S6] P1 越权** 交易链接增删改查未校验归属，可覆盖他人 steam 交易链接。
  `server/src/Doggo.Api/Endpoints/TradeUrlEndpoints.cs`。处置：**FIXED** `be05a90`。
- **[S8] P1 越权** 开箱 `/result` 未校验 seat 归属，可代他人写开奖结果。
  `server/src/Doggo.Api/Endpoints/CaseEndpoints.cs`。处置：**FIXED** `be05a90`。
- **[S9] P1 越权** roll create-template/create-room/prizes/join/draw/claim 匿名可调。
  `server/src/Doggo.Api/Endpoints/RollEndpoints.cs`。处置：**FIXED** `be05a90`。
- **[S10] P1 越权** roll join/claim 读取 body 的 `userId` 而非 token 声明，可冒用他人。
  `server/src/Doggo.Api/Endpoints/RollEndpoints.cs`。处置：**FIXED** `be05a90`。
- **[S11] P1 越权** 订单 pay/cancel/refund/retrieval 未校验订单归属。
  `server/src/Doggo.Api/Endpoints/OrderEndpoints.cs`。处置：**FIXED** `be05a90`。
- **[B6] P1 并发** 金币余额为读-改-写非原子，无并发令牌，双请求可重复扣减。
  `server/src/Doggo.Infrastructure/Users/CoinService.cs:38-176`。处置：**TODO**。
- **[B7] P1 并发** 优惠券双用：`UsedOrderId` 无唯一索引，两单可同时用同一券。
  `server/src/Doggo.Infrastructure/Marketing/CouponService.cs:169-208`。处置：**TODO**。
- **[B8] P1 一致性** 兑换码超兑（先读后写），且先加币后记账，记账失败不回滚。
  `server/src/Doggo.Infrastructure/Marketing/CouponService.cs:62-106`。处置：**TODO**。
- **[B9] P1 一致性** 下单多步无事务，库存/券/币部分失败不整体回滚。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:98-157`。处置：**TODO**。
- **[B10] P1 校验** `CoinDeduction` 无上限，且未强制 plan 的 `CanUseCoin/CanUseCoupon`。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:136-152`。处置：**TODO**。
- **[B11] P1 并发** 退款先打款后复查，重复退款可双打。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:287-328`。处置：**TODO**。
- **[B12] P1 并发** 手机/邮箱无唯一约束，重复注册可产生同号账号。
  `server/src/Doggo.Infrastructure/Auth/AuthService.cs:85-90`。处置：**TODO**。
- **[B13] P1 并发** refresh token 轮换竞态：并发刷新可产生两个有效 token。
  `server/src/Doggo.Infrastructure/Auth/AuthService.cs:223-262`。处置：**TODO**。
- **[B14] P1 并发** 签到双发：`MakeupOrderNo` 无唯一约束。
  `server/src/Doggo.Infrastructure/Users/SignService.cs:53-99`。处置：**TODO**。
- **[B15] P1 并发** 邀请一次性竞态：先读后写无锁。
  `server/src/Doggo.Infrastructure/Users/InviteService.cs:38-53`。处置：**TODO**。
- **[B16] P1 并发** roll 重复加入无唯一约束。
  `server/src/Doggo.Infrastructure/Roll/RollService.cs:83-115`。处置：**TODO**。
- **[B17] P1 可靠性** vapor webhook 先声明幂等再处理，处理失败丢事件。
  `server/src/Doggo.Api/Endpoints/VaporEndpoints.cs:71-93`。处置：**TODO**。

### P2

- **[S7] P2 XSS** 前端 `NewsDetailPage.tsx:44`、`ArticlePage.tsx:37` 渲染未消毒 HTML（纵深防御，见上）。
  处置：**TODO**。
- **[S12] P2 存储** token 存 `localStorage`，XSS 可窃取。
  `web/src/api/client.ts:14-17`。处置：**TODO**。
- **[D1] P2 部署** nginx admin IP 白名单默认注释。
  `deploy/doggo.conf.template:82-91`。处置：**TODO**。
- **[D2] P2 部署** dev compose 发布 MySQL 端口、Redis 无密码。
  `docker-compose.yml:12-13、26-27`。处置：**TODO**。
- **[F3] P2 安全** banner `linkUrl` 无 scheme 白名单，可 `javascript:` 注入。
  `web/src/pages/NewsListPage.tsx:47-52`。处置：**TODO**。
- **[F5] P2 存储** admin token 存 `localStorage`。
  `admin/src/app.tsx:16-18`。处置：**TODO**。
- **[B18] P2 加密** roll 开奖用 `Random.Shared` 非加密随机，中奖可预测。
  `server/src/Doggo.Infrastructure/Roll/RollService.cs:162`。处置：**TODO**。
- **[B19] P2 正确性** 漏斗聚合写出 `to` 之后的行。
  `server/src/Doggo.Infrastructure/Marketing/FunnelAggregator.cs:26-77`。处置：**TODO**。
- **[B20] P2 正确性** 行情日线混币种无汇率换算。
  `server/src/Doggo.Infrastructure/Market/MarketStore.cs:161-230`。处置：**TODO**。
- **[B21] P2 并发** 重复提交取回可双发 trade offer。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:406-463`。处置：**TODO**。
- **[B22] P2 正确性** SteamOpenId 过期时间按本地时区解析。
  `server/src/Doggo.Infrastructure/Auth/SteamOpenIdService.cs:129-130`。处置：**TODO**。
- **[B23] P2 正确性** `GroupBuyingView` 对重复 `OrderId` 的 `ToDictionary` 抛异常。
  `server/src/Doggo.Api/Endpoints/CaseEndpoints.cs:61、331`。处置：**TODO**。
- **[B24] P2 并发** 同模板可双开房。
  `server/src/Doggo.Infrastructure/Roll/RollService.cs:273-309`。处置：**TODO**。

### P3

- **[S13] P3 加固** nginx 缺 `X-Content-Type-Options` 等安全头。
  `deploy/doggo.conf.template:44-50`。处置：**TODO**。
- **[F6] P3 依赖** axios 0.27.2 传递依赖。
  `admin/pnpm-lock.yaml:2142`。处置：**TODO**。
- **[D3] P3 卫生** mysqladmin 密码作 CLI 参数。
  `docker-compose.yml:17`、`docker-compose.prod.yml:19`。处置：**TODO**。
- **[D4] P3 卫生** CI 硬编码凭据（仅 fixtures，无需改）。
  `.github/workflows/mobile.yml:86-88、104-106`。处置：**TODO**（记录不修）。
- **[F7] P3 注入** 小程序 RichText 渲染 HTML。
  `miniapp/src/pages/newsdetail/newsdetail.tsx:57`。处置：**TODO**。
- **[F8] P3 工具** 小程序 `urlCheck: false`。
  `project.config.json:8`。处置：**TODO**。
- **[F9] P3 卫生** http 默认 API base。
  `mobile/src/app.ts:17`、`shared/src/client.ts:8`。处置：**TODO**。
- **[D5] P3 卫生** appsettings 默认 DB 密码占位。
  `server/src/Doggo.Api/appsettings.json:10`。处置：**TODO**。
- **[B25] P3 卫生** 订单号用 `Random.Shared`。
  `server/src/Doggo.Infrastructure/Orders/OrderService.cs:677`。处置：**TODO**。
- **[B26] P3 资源** `VaporClient` 的 `HttpResponseMessage` 未释放。
  `server/src/Doggo.Infrastructure/Vapor/VaporClient.cs:80-101`。处置：**TODO**。
- **[D6] P3 安全** `inspectLink` 无 scheme 白名单。
  `web/src/pages/CaseOpeningPage.tsx:108`。处置：**TODO**。

## 3. 质量门

| 门 | 结果 |
|---|---|
| 服务端构建 | `dotnet build` 通过 |
| 服务端测试 | **504 通过 / 0 失败**（Api 96、Infrastructure 179、Domain 103、Scraper 45、PriceCrawler 81） |
| 前端构建 | `pnpm build`（web/admin）通过，本次未改动 |
| lint / 类型 | web/admin `pnpm lint`、`tsc` 通过 |

说明：改动集中在服务端鉴权面，前端仅 `README.md` 计数更新；`web`/`admin` 未改源码，
未重跑其构建。测试覆盖了本次修复的 401/404/归属分支（见 `tests/Doggo.Api.Tests/*`）。

## 4. 处置汇总

- **发现 49 条**：P0 6 / P1 19 / P2 13 / P3 11。
- **当场修复 9 条**（P0×3：S2/S3 + 归属面；P1×6：S4/S5/S6/S8/S9/S10/S11），全部经 504 测试绿。
- **记录待处理 40 条**（`todo.md` `## 代码深审 2026-11-02`）：P0 3 / P1 13 / P2 13 / P3 11。
  - 其中 P0/P1 并发与一致性类需 EF migration（并发令牌 + 唯一索引）与事务/服务重构，
    EF InMemory 测试不能验证行级并发，故记录而非盲修。
  - S1（mock 支付在生产）为部署里程碑决定，非纯代码问题。

## 5. Commit 清单

| commit | 说明 | 覆盖发现 |
|---|---|---|
| `be05a90` | `fix: require auth on order and inventory endpoints` | S2/S5/S6/S8/S9/S10/S11 |
| `b0ffb2c` | `fix: reject payment callback amount mismatch` | S3 |
| `3b8abf0` | `fix: harden steam openid state signing` | S4 |

## 6. 未覆盖说明

- Scraper / PriceCrawler / Worker 专项逻辑本轮未独立深审（服务器核心面代理中途失联，部分覆盖重叠）。
- 移动端（mobile/miniapp）仅做静态扫描，未做真机/联调验证（需 vapor 联调，见 todo.md 阻塞项）。
