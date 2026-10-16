# 原有平台功能分析

[English](/en/design/legacy-analysis) | [简体中文](/design/legacy-analysis)

本文整理本项目所源自的生产平台的完整功能点,作为开源版重写的功能基线。按模块记录平台实际做了什么、怎么做的,不涉及原品牌信息。

## 1. 平台形态

原平台是一个 **CS2 武器箱代购 + 开箱服务平台**,核心模式:

- 用户购买"装载了真实武器箱 + 钥匙的 Steam 账号";
- 开箱有两种履约方式:平台托管的 Steam 机器人**代开**,或交付账号凭据由用户**自开**;
- 开箱掉落由 Valve 服务器决定,**平台不控制饰品概率**;
- 开出的饰品通过 Steam 交易报价交还用户;
- 平台负责账号托管、自动化执行、快照记录、库存与订单状态机、饰品取回。

配套能力:饰品价格爬虫(8 个国内交易站)、营销体系(roll 房/优惠券/积分/活动)、多渠道广告追踪、管理后台。

技术形态:Java 21 + Spring Boot 3.4 单体多模块 monorepo(MySQL 8 + Redis),约 744 个 Java 文件;管理后台为 React 18 + Ant Design Pro(UmiJS);Steam 机器人为仓库外的独立进程,与平台通过 Redis Stream 消息队列通信。

## 2. 模块划分

| 模块 | 职责 |
| --- | --- |
| common | 37 个业务枚举、工具类(别名抽样算法、加解密、SteamGuard 工具)、延迟任务队列 |
| dao | MyBatis-Plus 实体约 70 张表,注解驱动自动建表加索引 |
| service | 全部业务逻辑,约 130 个 Service(订单/库存/开箱/roll 房/活动/支付/短信/积分/优惠券/通知) |
| bot | 机器人任务桥接层:指令写入 Redis Stream,消费结果流并落库 |
| tracking | 广告转化回传(百度营销 API、360 点睛 API) |
| server | C 端 REST API(JWT 认证)+ 全部定时任务 + 支付网关对接 |
| admin | 后台管理 API(RBAC、IP 白名单、OSS、留存统计),约 70 个 Controller |
| scraper | 饰品价格爬虫,独立进程(Selenium/Chrome + HTTP API) |

## 3. Steam 机器人接入

### 3.1 通信边界

原平台的 Java 侧不解析 Steam 协议,真正的机器人是外部进程。接口边界是两条 Redis Stream 和一张落库表:

- 指令流 `bot_task_<command>`,结果流 `bot_task_ret_<主机名>`;
- `bot_task` 表记录任务(生产者/消费者/action/加密参数/加密结果/状态/截止时间);
- 同账号同 action 2 秒限流;任务 30 分钟超时;结果由消费组 `server` 认领,handler 分发处理;每日清理 30 天前任务;
- 消费者按主机名注册在线状态。

### 3.2 指令集

| 指令 | 作用 |
| --- | --- |
| password_auth | 校验 Steam 账号密码可用 |
| enable_guard | 为账号绑定 Steam Guard 令牌(批量导入用) |
| inventory | 拉取机器人账号背包快照 |
| inventory_history | 拉取背包增量变更(仅 CS2,按 historyId 游标) |
| trading | 向用户交易链接发起饰品报价 |
| trade_url_info | 校验用户交易链接有效性 |
| open_crate | 团购批次开箱(一串箱子) |
| open_all_crate | 一键开账号下全部箱子 |
| open_crate_trading / open_crate_inventory / open_crate_inventory_history | 团购订单的专用变体 |

任务参数统一携带账号凭据与 SteamGuard 密钥(密文存库)、items、交易链接、订单号。

### 3.3 结果处理

| 处理器 | 行为 |
| --- | --- |
| 密码验证 | 成功后把凭据写入用户绑定账号 |
| 背包快照 | 写快照表;按来源区分管理员仓库账号 |
| 背包变更 | 解析增减物品与交易对手,事务写入库存流水,推进游标 |
| 交易 | 成功→订单置"取回中";失败回退并记录错误 |
| 开箱结果 | 按订单号逐条写奖品记录(补市价与图片),推进团购库存状态,聚合整单状态 |
| 交易链接校验 | 回写校验状态/昵称/错误 |

### 3.4 机器人账号状态机

`创建(备货) → 待盘点 → 盘点中 → 盘点完成(人工审核) → 在线上架 → 订单锁定 → 已售 → 已取回`,旁路状态:可复用 / 有余额 / 售卖中 / 异常 / 逾期。

运营操作:Excel 批量导入账号、批量导入 Guard 令牌、批量上下线、库存校对、转仓库账号、背包快照、SKU 库存汇总(现货/冷却/售出/备货/错误)、冷却原因与可售时间。

## 4. 价格爬虫

- 范围:仅 CS2(appId 730)的武器箱与钥匙两类商品;无 Dota2、无资讯抓取。
- 数据源:8 个国内第三方饰品交易站(HTTP API 与 Selenium 混合,如悠悠有品、C5Game、ZBT、IGXE、CSQAQ 等),另有站点兼作汇率源。
- 调度:每 10 分钟一轮,按站点配置的抓取间隔/请求延迟用延迟队列错峰执行;随机 sleep 模拟人工;非无头 Chrome。
- 落库:`fetcher_price`(站点/名称/价格/数量/类型/币种/图片)+ 站点管理表(间隔/Headers/最近与下次拉取时间)。
- 消费:同步为商品定价参考价(最终价 = max(爬虫参考价, 采购价) + 修正);开箱奖励按饰品名补当前市价;后台提供"同步爬虫价格"与"清理缓存"操作。

## 5. 商品体系

- 商品三类:钥匙代购、箱子套餐、组件套餐;类型上区分箱子/钥匙/组合。
- 套餐模型:套餐(case_plan)+ 选项(case_plan_option)。选项含起批数量、加价率 ratio(**是钥匙加价率,不是爆率**)、可否用券/积分、返现及倍率、数量档位(min/max)。
- 限量促销:折扣价、库存、每人限购、每日重置。
- 数量档位:购买数量阈值 + 库存告警线。
- 推荐套餐、进货汇率(多币种 + Steam 汇率,双数据源)。
- 箱子内容物展示配置(case_content:标题/品质/颜色/图片/参考价),品质为 CS2 七档稀有度(白/浅蓝/蓝/紫/粉/红/金)。

## 6. 订单与支付

- 下单入口五种:活动页 / 开箱 / 钥匙 / 淘宝 / 团购,订单号按入口前缀 + base36。
- 下单时装配价格并锁定一个机器人账号作为库存;支付期限 10 分钟,过期自动取消。
- 支付渠道:支付宝 SDK、聚合支付(主通道)、DSA 证书网关、微信公众号支付、积分全额抵扣、优惠券叠加抵扣;回调统一入口更新订单。
- 订单状态机:待支付 → 待结算 → 待取回 → 取回中 → 已完成;旁路:取消 / 退款。
- 定时补偿:交易检查(20 分钟 + 整点/4 小时补偿)、取回状态对账、促销限购每日重置。
- 淘宝卡密:订单号 + Base64 卡密兑换账号凭据,填交易链接领取。

## 7. 开箱业务流

三种模式:

1. **自开**:购买后系统交付一个机器人账号的凭据 + Steam Guard 令牌;用户在真实游戏内自行开箱;平台拉背包快照与订单关联;领取时校验(箱子/钥匙是否开完、交易冷却)后发起交易指令交付饰品。
2. **团购代开("一发入魂")**:一批 = 一个机器人账号上的一串箱子,按座位(lot/seat)售卖;用户付款占座后提交开箱指令,机器人会把同账号内排在前面的未开箱子一并执行(排队开箱 + 保持登录);结果回调生成奖品记录。
3. **一键开箱(云开)**:开掉账号下全部箱子。

库存座位状态链:售卖中 → 锁定 → 已付款 → 开箱中 → 已开箱 → 获取库存 → 完成库存 → 交易中 → 完成。

奖品记录字段:assetId/classId/instanceId、市场名、品质、外观(磨损档)、floatValue、稀有度、检查链接、可交易时间、估价、图片。

## 8. 用户体系

- 注册(短信验证码)、密码/验证码登录、JWT;头像/昵称/手机/密码管理;实名认证;登录日志与访问日志(日 UV/PV)。
- **平台积分**:余额、流水、兑换码充值、FIFO 消费、按小时过期、退款回补。
- **优惠券**:券类型(满减/折扣)、可用商品、兑换码兑换。
- **邀请返利**:邀请码、奖励流水。
- **签到**:自然周周期、送积分、凭当日订单补签。

## 9. 营销与活动

- **roll 房**:房间状态机(创建中→进行中→开奖中→开奖结束→领奖→结束);参与规则八种(登录领取/每次购买/免单套餐/固定或随机邀请码/全蓝/出红/出金免单);开奖用**别名抽样法(AliasMethod)按权重**抽取,奖品为仓库饰品(机器人交易发货)或积分;支持内定与人工分配;自动滚动下一期。
- **活动中心**:抽签、轮盘(奖励 value/weight/库存/限值,支持模拟批量抽奖)、好友祝福、许愿;活动配置与领取。
- **内容**:Banner、公告、合作伙伴、客服二维码(时段生效)、文章管理(分类:综合/赛事/新闻/事件,富文本,阅读数)——这是原平台唯一的"资讯"能力。
- **展示增强(运营手段,重写时可裁剪)**:首页统计放大系数、欧气排行榜/欧气时刻、滚动晒单的假订单、模拟数据生成任务(假用户/假奖品/假 roll 房参与者/假销量,定时任务驱动)。

## 10. 数据统计(后台)

数据面板(用户总数/今日新增/活跃/充值)、新增用户、订单走势(ARPU/ARPPU/活跃数)、渠道独立访客、用户留存、商品追踪、消费排行、推广渠道管理(备案主体/域名)。

## 11. 广告追踪

- 事件回传:注册/登录/支付/首次支付/次日留存/点击购买,异步回传百度营销 API 与 360 点睛 API,失败重试。
- 渠道归属:每个投放渠道对应独立域名/备案主体/ICP/网安号/API token;nginx 配 10 个 server 块支持多投放站点。

## 12. 数据模型概览

约 70 张表,按域分组(关键字段):

| 域 | 表 | 要点 |
| --- | --- | --- |
| 商品 | product、case_plan(+option)、key_plan(+option)、crate_group_buy_plan(+option)、case_promotion_plan、case_quantity_option、product_recommend、product_tracker、case_content | 多级价格(采购/参考/修正/最终)、planFlags、访客/销量按天统计 |
| 订单 | user_order | 入口/价格四件套/积分抵扣/券抵扣/返现/支付渠道/交易链接/状态/错误/serviceMode |
| 开箱 | group_buying、group_buying_inventory、user_prize、user_prize_log | 批次/座位/乐观锁;座位记录箱子与钥匙 assetId、奖品 assetId、状态 0~10;奖品含完整饰品字段 |
| 机器人资产 | steam_bot_account、steam_bot_backpack_snap、steam_bot_inventory_history、steam_bot_repo_account(+inventory)、steam_asset、bot_task | 加密凭据、状态机、库存快照 JSON、流水游标 |
| 用户 | user_account、user_steam_account、user_steam_trade_url、user_steam_backup_code、user_coin(+log/redeem_code)、user_coupon(+coupon/redeem_code)、user_sign(+record)、user_invite_reward_log、user_login_log、user_visit、user_retention_stats | 积分 FIFO 消费与过期、多交易链接(preferred) |
| 运营 | banner、notice、article、partner、support_qrcode、site_settings、short_url、exchange_rate、activity×7、roll_room×4、fetcher_price(+site) | 站点设置驱动功能开关 |
| 后台 | admin_account、admin_role、permission、role_permission、admin_ip_whitelist、admin_login_log、admin_notification_channel(+event) | RBAC + IP 白名单 + 通知渠道 |

## 13. 中间件与部署

- **存储**:MySQL 8(utf8mb4,自动建表、逻辑删除)、Redis(缓存/分布式计数器/限流 + 独立实例的 Redis Stream 消息队列,自定义编解码)。
- **运行时**:Undertow 容器、虚拟线程(爬虫)、定时任务内嵌于 server 进程。
- **三方**:支付宝/聚合支付/微信公众号支付、阿里云短信与 OSS、汇率 API、Steam 公开接口(交易链接解析、库存 JSON)。
- **通知**:钉钉机器人、企业微信、邮件、短信(工厂模式,事件:新订单/退款/库存不足等)。
- **安全**:Spring Security + JWT、admin IP 白名单 + 登录日志、OWASP AntiSamy XSS 清洗、凭据加密存储。
- **部署**:nginx 多域名、systemd、GitHub Actions(自托管 runner)、每日 MySQL 备份。

## 14. 开源版的映射与取舍

| 原能力 | 开源版去向 |
| --- | --- |
| 外部 Steam 机器人进程 + Redis Stream 桥接 | 由 [vapor](https://github.com/cuihairu/vapor) 承担:控制平面/代理、SteamGuard、交易、库存、开箱动作,REST API/Webhook 集成 |
| 饰品价格爬虫(8 个国内站点) | 收敛为 vapor 的 Steam 侧行情同步;第三方站点源留作扩展点 |
| Java server/service/admin API | ASP.NET Core 重写(Doggo.Api) |
| 资讯文章管理(单一模块) | 扩展为完整的 CS2 / Dota 2 资讯聚合管道(Doggo.Scraper,自建) |
| React 管理后台 | 重写(技术选型见[设计总览](./overview)) |
| 广告追踪多域名体系 | 不在开源版范围(涉及备案主体等敏感运营配置) |
| 展示增强类任务(假数据生成等) | 不在开源版范围 |
| 支付网关(支付宝/聚合/微信) | 设计为可插拔支付接口,具体渠道按需接入 |
