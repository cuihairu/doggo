# doggo UI 体系：令牌、主题与四端复用


配套原型：`docs/design/prototypes/`（静态可打开、截图可验）。本文记录令牌表、主题清单、组件复用关系与"哪端用哪套"；实现阶段以此为验收基准，逐屏给「已对上 / 有差异 / 未实现」。

## 1. 原则

1. **主题 = 颜色令牌 + 图案/装饰件**，不含布局信息。
2. **换主题不改布局**：结构令牌（间距、字号、栅格、圆角框架、时长）写在 `:root`，任何主题不得覆盖；主题块只允许写视觉令牌。
3. **组件层不认识主题**：`base.css` 只消费令牌，禁止出现主题色字面量；页面结构里不出现主题判断分支。
4. 不引第三方设计素材；图标自绘 SVG（CSS mask），品牌爪印复用 `docs/public/paw.svg`。

## 2. 令牌体系（两层）

### 2.1 结构令牌（`:root`，全主题固定）

| 令牌 | 含义 | 值 |
|---|---|---|
| `--sp-1..5` | 间距阶 8/16/24/36/56px | 阶梯式 |
| `--fs-min/body/lead/title/h1` | 字号阶 12/14/16/20/28px | 阶梯式 |
| `--topbar-h` / `--tabbar-h` | 顶栏/底部导航高度 | 56px |
| `--radius-s/m/l` | 圆角阶 | 6/8/14px |
| `--container` | web 内容宽 | 1200px |
| `--sidebar-w` | admin 侧栏宽 | 220px |
| `--phone-w/h` | 手机真机框 | 375×780px |
| `--dur` / `--ease` | 动效时长/缓动 | 160ms / ease-out |

主题允许覆盖的**唯一**结构项是 `--radius`（视觉圆角，见主题块）；其余结构令牌一概不动。

### 2.2 视觉令牌（每主题一个 `[data-theme="..."]` 块）

| 令牌 | 含义 |
|---|---|
| `--bg` | 页面背景 |
| `--surface` / `--surface-2` | 卡片面 / 次级面（占位、骨架底） |
| `--text` / `--muted` | 正文 / 弱化文字 |
| `--border` | 描边 |
| `--primary` / `--primary-text` | 主色 / 主色上的文字 |
| `--primary-weak` | 主色弱底（选中态、按钮弱化、骨架 shimmer） |
| `--danger` / `--success` | 错误 / 成功 |
| `--gold` | 金币、稀有度等金色语义 |
| `--accent-2` | 图表第二序列色 |
| `--radius` | 组件圆角（视觉圆角，允许主题微调） |
| `--shadow` | 卡片阴影 |
| `--brand-mark` | 品牌爪印颜色 |
| `--pattern` / `--pattern-opacity` | 装饰图案（`body::before` 消费，z-index 低于内容） |
| `color-scheme` | 原生控件明暗 |

## 3. 主题清单（11 套）

| # | id | 名称 | 明暗 | 主色 | 图案 | 适用场景 |
|---|---|---|---|---|---|---|
| ① | `brand` | 默认（品牌蓝） | 浅 | `#2f66d0` | 无 | 全站常态、admin 常态 |
| ② | `dark` | 暗色 | 暗 | `#6a96f0` | 细斜线 | 夜间浏览、mobile 跟随系统、admin 长时值守 |
| ③ | `cny` | 节日·春节 | 浅 | `#c62828` | 红斜格 + 金光 | 春节区间运营位、新春活动 |
| ④ | `lantern` | 节日·元宵 | 浅 | `#e06a3c` | 双侧灯彩辉光 | 元宵当晚限时氛围 |
| ⑤ | `dragonboat` | 节日·端午 | 浅 | `#0f7a5c` | 波浪斜纹 | 端午活动、绿系运营位 |
| ⑥ | `midautumn` | 节日·中秋 | 暗 | `#e3b548`（月色金） | 月晕 + 星点 | 中秋区间；暗色节日样本 |
| ⑦ | `national` | 节日·国庆 | 浅 | `#d8322a` | 横纹带 + 金光 | 国庆区间运营位 |
| ⑧ | `spring` | 季节·春 | 浅 | `#e0688f` | 花瓣点阵 | 3–5 月（可选启用） |
| ⑨ | `summer` | 季节·夏 | 浅 | `#0f9bb0` | 斜纹 + 阳光点 | 6–8 月（可选启用） |
| ⑩ | `autumn` | 季节·秋 | 浅 | `#b4622a` | 落叶斜纹 | 9–11 月（可选启用） |
| ⑪ | `winter` | 季节·冬 | 浅 | `#3d7ea6` | 雪点 | 12–2 月（可选启用） |

每套的完整色板见 `prototypes/index.html` 第二节（卡片用自身主题渲染）。

## 4. 主题切换机制

- 优先级：URL 片段 `#theme=xxx` > `localStorage` > 页面默认。
- 切换动作：写 `<html data-theme>`，无重渲染、无布局跳变；原型脚本 `prototypes/theme.js`，实现期各端等价实现（web 直接用同思路，小程序/手机端用设置存储 + 样式变量源）。
- 装饰层：`body::before` 固定铺 `--pattern`，`--pattern-opacity` 控制强弱，`pointer-events:none`，z-index 低于内容层。
- admin 侧通过运营日历（设置页「主题运营位」）按区间自动切节日/季节主题；**用户手动选择优先于运营位**。

## 5. 组件层与四端复用

`prototypes/base.css` 是组件词表（只消费令牌）。四端复用关系：

| 组件 | web | 小程序 | mobile | admin |
|---|---|---|---|---|
| 顶栏/导航（topbar/nav） | ✔ | —（appbar+tabbar） | —（同小程序） | 侧栏布局 |
| 卡片/栅格（card, grid cols-2/3/4） | ✔ | ✔ | ✔ | ✔ |
| 按钮（btn + ghost/danger/plain/sm/block） | ✔ | ✔ | ✔ | antd Button 对齐语义 |
| 徽章/标签（badge, chip） | ✔ | ✔ | ✔ | antd Tag 对齐语义 |
| 表格（table） | ✔ | 紧凑行（tick-row） | 同左 | antd Table |
| 骨架屏（skeleton + shimmer） | ✔ | ✔ | ✔ | ✔ |
| 空态/错误/成功（empty/error-box/success-box） | ✔ | ✔ | ✔ | ✔ |
| 轻提示（toast） | ✔ | ✔ | ✔ | antd message |
| KPI 卡（kpi） | — | — | — | ✔ |
| 漏斗条（funnel-step/funnel-bar） | — | — | — | ✔ |
| 手机真机框（phone） | 仅原型 | ✔ | ✔ | — |
| 弹层（modal/drawer） | — | 半屏 sheet（待细化） | 同左 | ✔ |
| 主题选择器（theme-switch/theme-pick） | ✔（右上 + 个人中心） | ✔（我的→主题外观） | ✔（同左） | ✔（顶栏） |

## 6. 状态组件矩阵（空态/加载/错误/成功）

| 流程 | 加载 | 空态 | 错误 | 成功 | 原型 |
|---|---|---|---|---|---|
| 商品/行情列表 | 骨架卡 | 说明原因 + 清筛选动作 | 重试按钮 | 正常列表（首屏+加载更多） | web/states、两端 states |
| 下单支付 | 骨架 + 按钮置灰 | 无可用优惠时给空态卡 | 渠道超时 + 重试/换方式 + 保留订单倒计时 | 支付成功 + 进开箱下一步 | web/states 一、web/checkout |
| 开箱 | 开箱动画占位 | —（必有结果） | 支付未完成，订单保留 | 结果卡 + 存奖品/取回 | miniapp/order、两端 states |
| 个人中心 | 骨架行 | 未登录引导登录 | 拉取失败重试 | 数据就绪 | web/account、两端 profile |

规范：空态必须说明原因并给下一步动作；错误必须可重试或给替代路径；成功必须给明确下一步；轻提示只做瞬时反馈，状态变化落页面元素。

## 10. 实现状态（2026-10-13）

四端令牌化均已落地，逐屏对照如下。状态口径：**已对上** = 结构与组件语义一致；**有差异** = 存在但缺项/形态不同；**未实现** = 无对应实现。

### 10.1 令牌化落地

| 端 | 令牌源 | 切换机制 | 入口 |
|---|---|---|---|
| web | `web/src/tokens.css`（结构 `:root` + 11 主题块） | `theme.ts`：`#theme=xxx` > localStorage > brand，写 `<html data-theme>` | 顶栏 select + 个人中心「外观与主题」卡 |
| mobile | `mobile/src/theme.ts`（11 套 Palette） | 跟随系统为默认，手动选择写 AsyncStorage 覆盖；「跟随系统」为第 12 项 | 个人中心「外观与主题」卡 |
| miniapp | `miniapp/src/app.scss`（`page` 结构令牌 + 11 主题块） | `theme.ts`：Taro 存储 + `setBackgroundColor`/`setNavigationBarColor` 同步原生 chrome；页根由 `ThemeRoot` 写 `data-theme` | 我的 → 主题外观 |
| admin | `admin/src/theme.ts`（11 套 → antd token 映射） | `ThemeGate`（umi `rootContainer`）包 `ConfigProvider`；dark/midautumn 走 `darkAlgorithm` | 顶栏下拉（`actionsRender`） |

### 10.2 逐屏对照

**web**（原型 5 屏）

| 屏 | 状态 | 差异 |
|---|---|---|
| home | 有差异 | 原型双拼大 Banner → 实现横向滚动图；行情速览无涨跌列；分类 chip → select；资讯无双栏迷你列表 |
| product | 有差异 | 无面包屑、缩略图、独立行情参考价对照表（需行情日涨跌数据）；「包含内容」徽章网格、划线原价、促销库存 badge、金币抵扣与优惠券选择、底部 buybar 已落地 |
| checkout | 有差异 | 独立确认订单页已落地：订单内容卡（图标/名称/套餐）、支付方式卡（支付宝模拟已选 + 微信待接入）、支付倒计时徽章（服务端 payDeadlineUtc）、协议勾选、金额明细与三步说明；无首单优惠行（服务端无该字段） |
| account | 有差异 | 主题卡每项 1 个色点（原型 6 色 swatch 网格）；无「自动切换/同步四端」chip 与当前主题名提示；领券中心为实现新增 |
| states | 有差异 | 骨架屏 shimmer、空态（图标+说明+行动按钮）、错误框（重试）、成功框（支付后下一步）已按原型落地；轻提示 badge 体系与表单内联错误仍为纯文本 |

**miniapp**（原型 6 屏）

| 屏 | 状态 | 差异 |
|---|---|---|
| home | 有差异 | 无 appbar、banner 活动位、四宫格入口、运营推荐行；行情速览无涨跌列且不可点进 market；多出公告卡与资讯流 |
| market | 有差异 | 搜索框 + 游戏/类型筛选 chip 与日涨跌红绿（`changeBps`，服务端按最近两个日线收盘算）已落地；仍无站内 vs 外部参考价对照表；列表渲染 classId 而非磨损度；无入口跳 market（页面成孤儿） |
| product | 有差异 | 划线原价、套餐语义（单开/三开/十开）、「包含内容」徽章网格（品质色边框 + ★品质 + 参考价）与底部 buybar（最低单开 + 立即开箱，fixed + 安全区）已落地；金币抵扣与优惠券选择已补（2026-10-17，filter-chip 选择 + 确认弹窗列抵扣行）；仍无 CS2 badge 与库存文案；下单前用确认弹窗替代独立确认订单页（商品/套餐/支付方式/应付金额/15 分钟时限） |
| order | 部分已落地 | 小程序无订单路由：确认订单走商品页弹窗，无支付倒计时（mock 渠道下单即支付成功）；开箱结果卡已落地（金色 glow/稀有 badge/参考价/存奖品与取回提示）；取回到 Steam 依赖 vapor 联调，暂不可操作 |
| profile | 有差异 | 头像头（昵称首字 + 掩码账号 · 邀请码）、stat-row 三格（金币/优惠券/邀请）与本周签到进度已落地；主题外观为内联 11 色板卡（原型为入口行，功能等价）；菜单组缺订单记录/设置 |
| states | 有差异 | 骨架屏（脉冲卡）、空态（徽章+说明+行动按钮）、错误框（警示徽章+重试/替代路径）已落地；成功框仅订单支付流程接入，其余反馈仍为 toast |

**mobile**（原型 6 屏）

| 屏 | 状态 | 差异 |
|---|---|---|
| home | 有差异 | 无 banner、四宫格（实现为 6 个文字 Btn 网格）、运营推荐；行情速览无涨跌%；多出资讯卡；原型 tabbar 未实现（§9-1） |
| market | 有差异 | 搜索框 + 游戏/类型筛选按钮与日涨跌红绿已落地；仍无站内 vs 外部参考价对照；多出 30 日收盘 SVG 趋势线（原型无） |
| product | 有差异 | 无 180px hero 大图、套餐为文字 Btn 非 plan-row；金币抵扣 chip（余额上限、1 金币抵 1 分）、优惠券列表、「包含内容」徽章网格（品质色边框 + ★品质 + 参考价）与底部 buybar（最低单开价 + 立即开箱）已落地 |
| order | 有差异 | 无订单列表端点（本地记忆最近 10 个 id + 手动输入）；待支付倒计时徽章（mm:ss，服务端 payDeadlineUtc）、支付方式「支付宝（模拟）」、商品图标/名称/套餐与优惠券抵扣行已落地；仍无独立确认订单页与协议勾选；多出 4s 轮询；状态 7 种（原型 4 种） |
| profile | 有差异 | me-head 头像头（昵称首字 + 手机/邮箱掩码 · 邀请码）、stat-row 三格（金币/优惠券/邀请）、本周签到进度（已签 n/7 · 今天可 +N + 签到按钮）已落地；「外观与主题」卡已实现且更完整（11 色板 + 跟随系统）；仍无设置页 |
| states | 有差异 | 骨架屏（脉冲卡）、空态（图标+说明+行动）、错误框（警示图标+重试）、支付成功框已落地；开箱结果卡（最新奖品置顶：金色圆形 glow、稀有/品质 badge、参考价、存奖品与取回提示）属下单/开箱闭环（§10.3-2） |

**admin**（原型 6 屏）

| 屏 | 状态 | 差异 |
|---|---|---|
| dashboard | 有差异 | KPI 口径不同（用户/订单 vs 原型 PV/UV）；趋势为折线非柱状；排行 Top10 营收非 Top5 热销；缺漏斗速览卡、系统状态卡 |
| funnel | 有差异 | 原型 3 张分组表（端/渠道/按日柱图）→ 实现单行表 + 分日/周筛选；无「整体转化/最大流失」结论行；步骤文案「加购(下单意图)」 |
| orders | 有差异 | 无顶部 4 KPI、筛选（订单号/状态/日期+刷新）；列缺用户/内容、多金币/券抵扣+奖品；状态枚举 0-6（原型 5 态）；退款 modal 无预置原因与退回券/金币 chips |
| products | 有差异 | 无筛选与「新建商品」；列缺游戏/类型/库存；状态为纯文本非 badge；抽屉无掉落表/价格历史/外部参考价；汇率修正用 window.prompt |
| settings | 有差异 | 站点参数改为可编辑（`GET/PUT /api/admin/settings` 走既有 SiteSetting 表，行内保存 + 新增/覆盖、加载骨架/错误重试/空态）；爬虫站点总览读 `GET /api/admin/price-crawl/maps` 聚合站点映射数与待确认数；支付渠道与主题运营位日历已挂载控制台（2026-10-17）：设置页渠道卡（列表 + 编辑 upsert，密钥只登记环境变量名），内容中心 Banner 页补投放窗口列、创建窗口字段与行内编辑（400/409 服务端文案透出） |
| states | 有差异 | 已落地：Dashboard 骨架屏+错误重试（替代 '-' 占位与静默 catch）、Funnel 骨架/错误/空态「清空筛选」、商品资料抽屉骨架+错误；Orders/Users/Rolls/Products/Articles 共 13 个 ProTable 改为错误 Alert+重试；登录页与表单内错误保持纯文本 |
| Users/Articles/Rolls/Login | 无原型对照 | 实现多出页面（原型侧栏仅占位链接） |
| PriceCrawl | 无原型对照 | 「数据分析 → 价格爬虫」页：映射审阅（按状态分段 + 确认/拒绝，只读对照）与 7/30 天比价表（ProTable + 错误重试 + 空态） |

### 10.3 差异清单（按类）

**结构性差异（原型有、实现无，按优先级）**

1. 状态组件矩阵（§6）：web、mobile、miniapp 已落地（web `components/States.tsx` 九页接入；mobile `ui.tsx` StateIco/Skeleton/EmptyState/SuccessBox，Msg 升级为错误框，商品/行情/开箱/商品详情/资讯详情/订单接入；miniapp `src/states.tsx` Skeleton/EmptyState/ErrorBox/SuccessBox，小程序无 SVG 组件，徽章用描边圆 + 字形，商品/行情/商品详情/资讯详情/开箱五页接入，开箱页无登录时错误框带「去登录」替代路径）；admin `src/components/States.tsx`（PageLoading/ErrorAlert/EmptyAction），Dashboard/Funnel/资料抽屉与 13 个 ProTable 接入，错误不再静默。
2. 下单/开箱闭环：无独立确认订单页、支付方式选择、超时倒计时、开箱结果卡（open-glow/稀有 badge/存奖品/取回）。web 已落地：确认订单页 OrderPayPage（订单内容、支付方式、支付倒计时、协议、金额明细）+ 开箱结果卡 CaseOpeningPage（open-glow、稀有 badge、参考价、检视链接，奖品由服务端自动存入）；mobile 已落地订单卡（商品图标/名称/套餐、支付倒计时徽章、支付方式、优惠券抵扣）+ 开箱页结果卡（金色 glow、稀有 badge、参考价、存奖品/取回提示）；miniapp 以商品页确认弹窗替代确认订单页（小程序无订单路由）+ 开箱页结果卡（金色 glow、稀有 badge、参考价）；shared 类型同步 productName/productIconUrl/planTitle 与座位奖品快照。四端均无取回到 Steam 入口，依赖 vapor 联调。
3. 商品详情：无「包含内容」徽章网格、行情参考价表、金币抵扣/优惠券选择、底部 buybar。mobile 已落地金币抵扣 chip（含余额上限，随 createOrder 的 coinDeduction 提交）、「包含内容」徽章网格与底部 buybar，优惠券原为列表；miniapp 已落地划线原价、套餐语义、徽章网格与 buybar，券/币选择已补（2026-10-17：金币抵扣 chip + 优惠券 chip 随 createOrder 的 userCouponId/coinDeduction 提交，确认弹窗列抵扣行）；web 已落地 ProductDetailPage 徽章网格（首 8 项 + 收起）、划线原价、促销库存 badge、金币抵扣与优惠券选择（/api/coupons/mine）、底部 buybar（应付券后估算 + 立即开箱）。行情参考价对照表已按代决口径落地（2026-10-16，价格爬虫设计 §12）：web 行情趋势页显示外部中位价、站内收盘与价差，逐站点明细仍仅 admin 侧。
4. admin 设置页：站点参数与爬虫站点总览已落地（新增 `GET /api/admin/settings`、`PUT /api/admin/settings/{key}`，key/value 形状校验 + 5000 字上限，2 例新测试）；支付渠道与主题运营位日历服务端已落地（`d389f96` 渠道配置实体与 upsert、`2ac84f0` Banner 投放区间与窗口重叠校验），控制台页面已挂载（2026-10-17）：设置页支付渠道卡（非密钥配置，密钥只登记环境变量名），Banner 页投放窗口列 + 创建窗口字段 + 行内编辑（400/409 服务端文案透出）。
5. 行情页：日涨跌与筛选已在 mobile/miniapp 落地（服务端 `MarketItemDto` 增 `changeCents`/`changeBps`，按最近两个日线收盘算，仅一条日线时为 null，2 例新测试）；站内 vs 外部参考价对照表已做（2026-10-16 代决，价格爬虫设计 §12）：`GET /api/market/items/{id}/reference` 返回近 7 天各已确认站点最新非疑点换算价的中位数、站内最新收盘与价差，web 趋势页渲染对照块，逐站点明细仍仅 admin 侧只读。

**令牌化差异**

6. web：结构令牌未完全消费（nav 高 52px 未用 `--topbar-h`，padding/字号散落硬编码 px）；`--gold`/`--accent-2` 无消费方；涨跌色用 `--danger`/`--success`（A 股语义，无 `--up`/`--down` 令牌）。
7. mobile：`--pattern` 图案层在 RN 不渲染（无 CSS 伪元素），仅颜色/圆角令牌生效；原生导航栏 `navigationBarBackgroundColor` 静态白底（暗色主题不变）。
8. miniapp：原生 tabBar 色（color/selectedColor/backgroundColor）静态，切主题不更新（§9-2）；`backgroundTextStyle` 静态 light；`--pattern-opacity` 无消费方（alpha 烘进渐变）。
9. admin：sider `navTheme` 只在路由变化时经 runtime config 重算（生成代码限制），页面内容经 ConfigProvider 即时换肤。

**已知限制（实现期决策，非缺陷）**

10. web 切换主题不写回 URL hash（原型写 hash 为截图可复现），改用 localStorage 持久化，避免应用内导航污染 URL。
11. miniapp 图案层用 `.app-root` background-image 叠加替代 `body::before`（wxss 的 `::before` 属定位元素会压在内容之上）。
12. admin 顶栏下拉为 11 主题全量（原型仅 brand/dark 快捷切换的设想见 §9-3，待拍板）。

### 10.4 待拍板（承接 §9）

- §9-1 小程序 tabBar 3→4（+行情）、mobile 底部 tab：影响 home/market 两屏结构。
- §9-2 小程序原生 tabbar 色：保持原生默认 / 自定义 tabbar / （折中）保留原生但用 `Taro.setTabBarStyle` 同步主题色——折中方案未实施，待拍板。
- §9-3 admin 是否默认提供暗色快捷切换（原型只在顶栏放下拉）。
- §9-4 季节主题是否默认启用。
- §9-5 mobile「跟随系统」第 12 项：已按推荐实现。
- §9-6 主题运营日历区间以哪年农历为准、由谁维护（依赖 admin 设置页）。

## 7. 哪端用哪套主题

| 端 | 默认 | 用户可选 | 说明 |
|---|---|---|---|
| web 前台 | `brand` | 全部 11 套 | 右上角 + 个人中心「外观与主题」 |
| 小程序 | `brand` | 全部 11 套 | 「我的 → 主题外观」；暗色建议跟系统（待拍板） |
| mobile | 跟随系统（dark/light） | 全部 11 套（手动选择后覆盖跟随） | 现状 `theme.ts` 已有 light/dark 硬编码，扩为令牌源 |
| admin | `brand` | `brand` / `dark`（+节日可选） | 长时值守以暗色为辅；不默认节日主题，由运营日历控制前台 |

## 8. 实现映射（原型 → 各端现状）

| 端 | 现状 | 改造方式 |
|---|---|---|
| web | `web/src/theme.css` 单文件（main.tsx 引入） | 拆为「结构令牌 + 视觉令牌」两段；`data-theme` 写在 `html`；新增个人中心主题卡 |
| 小程序 | `miniapp/src/app.scss` 常量色 | SCSS 常量 → CSS 变量；tabbar 原生色随主题受限，用自定义 tabbar 或保持原生默认（待拍板） |
| mobile | `mobile/src/theme.ts` light/dark 两套 Palette，`useColorScheme` 跟随系统 | Palette 扩为 11 套令牌源；保留跟随系统为默认值，手动选择写存储 |
| admin | antd + UmiJS，页面内散落十六进制字面量（如 Dashboard `#2f66d0`） | 色值收口到 `ConfigProvider theme` tokens；暗色用 `theme.darkAlgorithm`；漏斗页/主题运营位按本文与 funnel 设计文档新增 |

四端共享的只有**令牌定义与组件语义**（一个令牌源，多端消费），不强行共享运行时代码——与现状「四端只共享契约」一致。

## 9. 待拍板项

1. 小程序 tabBar 从 3 个（首页/商品/我的）扩为 4 个（+行情）：原型按 4 tab 设计，是否采纳；mobile 同步加底部 tab（现状为首页宫格跳转）。
2. 小程序原生 tabbar 无法随主题换色：保持原生默认色，还是换自定义 tabbar。
3. admin 是否默认提供暗色快捷切换（原型只在顶栏放下拉）。
4. 季节主题是否默认启用（还是仅节日启用、季节作为备用）。
5. mobile「手动选主题后覆盖系统跟随」是否需要「跟随系统」作为第 12 个选项（推荐需要）。
6. 主题运营日历的区间以哪年农历为准、由谁维护（admin 设置页已留位）。
