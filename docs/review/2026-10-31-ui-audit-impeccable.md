# Doggo UI 审计报告（impeccable audit）

- 日期：2026-10-31
- 目标：`doggo` 仓库（`web/` Vite + React 19 + react-router 7；`mobile/` Taro 4.3 RN）
- 工具：impeccable skill v4.8.0 `audit`；Playwright 1.63（chromium，桌面 1440×900 / 窄屏 390×844）；后端 `.NET API` 未运行，用 `/api/**` 路由拦截注入数据态渲染。
- 健康评分：**78 / 100**（详见「总体评判」；此值为审计时、即本轮修复前的基线评分，修复后复评见文末「复核矩阵」与「修复后复评」）

> 审计截图存放于本文件同目录的 `2026-10-31-ui-audit/`（下文截图引用均相对此目录）。

## 总体评判（不留情面）

先说实话：**这套 UI 有一个很扎实的骨架——令牌分层干净、组件只消费令牌、空/载/错/成四态齐全——但「能用」和「好看」之间还隔着几道没补的墙。** 最刺眼的是两件事：

1. **窄屏（≤720px）几乎每页都横向溢出。** 顶部导航 `flex-wrap: nowrap`，商品/开箱四列、三列卡片是写死的 `repeat(4|3, 1fr)` 网格，表单输入框没有 `width:100%`，商品详情的价格区被 `min-width:auto` 撑爆。390px 视口下首页横向滚动宽度达 423px——一个以「开箱交易」为主业的站点，手机上第一屏就要左右拖，这是 P0。
2. **季节/节日主题的次要文字对比度不达标（WCAG AA 4.5:1）。** 灯笼、端午、国庆、夏、秋、冬六个主题的 `--muted` 在白底上只有 3.0–4.4:1，导航、表格表头、说明文字大面积"灰得看不清"。这不是审美偏好，是合规问题。

另外两项硬伤：**全站没有任何键盘焦点指示**（`:focus-visible` 未定义，Tab 到按钮/输入框/链接时视觉上"消失"），以及**动效不尊重 `prefers-reduced-motion`**（骨架屏闪烁、开箱发光脉冲无条件播放）。这四类问题本次已全部修复（见下）。

还剩一个**没修、也明确不该由我擅自修**的问题：**品牌主色在五个主题里作按钮/链接前景时低于 AA**（灯笼 3.34、春 3.21、夏 3.31、秋 4.45、冬 4.44）。主色是品牌资产，改动它属于改视觉方向，超出本次"修复坏损"的边界——我把它作为最高优先级的**遗留问题**记录，并给出不碰品牌主色的加法式解法（新增 `--primary-strong` 令牌）。

说句公道话：桌面端（1440px）的排版、价格层级、吸底购买栏、资讯表格都相当干净；令牌架构（结构令牌在 `:root`、视觉令牌按 `data-theme` 收敛、组件零硬编码颜色）是这个项目最值钱的设计资产，值得保留。本次 18 个「路由 × 视口」组合在修复后全部 `overflow=false`、0 控制台错误。

## 修复的问题（4 个问题 → 4 个 commit）

### P0 · 窄屏全站横向溢出

- **现象**：390px 视口下首页 `scrollWidth=423px`；产品详情、商品列表、用户中心均横向滚动。
- **根因**：
  - `web/src/theme.css` `.app-nav` `flex-wrap: nowrap`（导航五项 + 主题选择器 + 用户区撑爆一行）；
  - `web/src/theme.css` `.grid.cols-4` / `.grid.cols-3` 写死 `repeat(4|3, 1fr)`，无断点收敛；
  - `web/src/theme.css` `.field input` 无 `width:100%; min-width:0`；
  - `web/src/pages/ProductDetailPage.tsx` 详情卡片右列 `flex:1` 但 `min-width:auto`，价格行 `display:flex` 不换行。
- **修复**：
  - `theme.css`：新增 `@media (max-width:720px)` 块——导航 `flex-wrap:wrap; height:auto`、隐藏 `.spacer`、`.theme-switch{margin-left:auto}`、`app-main` 收窄内边距；`.grid.cols-4`/`.grid.cols-3` 收敛为 `repeat(2,1fr)`；`.card{overflow-wrap:break-word}`；`.field input{width:100%;min-width:0}`。
  - `ProductDetailPage.tsx`：右列 `minWidth:0`；价格行 `flexWrap:'wrap'`。
- **验证**：修复后 18 个「路由 × 视口」组合全部 `overflow=false`，0 控制台错误。
- **截图**：`2026-10-31-ui-audit/web-home-narrow.png`、`web-products-narrow.png`、`web-product-detail-narrow.png`（修复后）。
- **Commit**：`62ee688`（导航换行）、`8485bbd`（网格/表单/详情溢出）。

### P1 · 次要文字对比度低于 WCAG AA（6 个主题）

- **现象**：`--muted` 在 `--bg`/`--surface` 上对比度 3.0–4.4:1，低于 4.5:1。受影响：导航链接、表格表头（`.table th`）、`.muted` 说明文字、字段标签。
- **根因**：六个主题的 `--muted` 取值过浅。
- **修复**（web + mobile 同步）：灯笼 `#a97b62→#8d6752`、端午 `#5c8577→#517669`、国庆 `#a06a5f→#98645a`、夏 `#5b8890→#50777e`、秋 `#997748→#886940`、冬 `#6b7f8e→#5f717e`。修复后 `muted/bg` 全部 ≥4.61:1。
- **文件**：`web/src/tokens.css`（各 `[data-theme]` 块）、`mobile/src/theme.ts`（同值）。
- **截图**：`2026-10-31-ui-audit/web-user-desktop.png`、`web-dark-home.png`（修复后）。
- **Commit**：`d13150c`。

### P1 · 无键盘焦点指示

- **现象**：Tab 键遍历到按钮/输入框/链接时无可见焦点环，键盘用户"迷路"。
- **根因**：`theme.css` 未定义 `:focus-visible`，浏览器默认 outline 被部分控件样式覆盖。
- **修复**：`theme.css` 增加 `:focus-visible{outline:2px solid var(--primary);outline-offset:2px}`，跨主题统一、跟随品牌色。
- **Commit**：`d84843d`。

### P2 · 动效不尊重减弱动效偏好

- **现象**：`.skeleton::after` 闪烁、`.open-glow` 脉冲无条件播放。
- **修复**：`theme.css` 增加 `@media (prefers-reduced-motion:reduce){.skeleton::after,.open-glow{animation:none}}`。
- **Commit**：`d84843d`。

## 遗留问题（记录，已随后续修复解决）

### P1 · 品牌主色作前景时低于 AA（灯笼/春/夏/秋/冬）

- **对比度**（`--primary` vs `--primary-text`，即 `.btn` 白字）：灯笼 3.34、春 3.21、夏 3.31、秋 4.45、冬 4.44；链接/价格（`--primary` vs `--surface`）同水平。均低于 4.5:1（`brand`/`dark`/`cny`/`端午`/`中秋` 达标）。
- **定位**：`web/src/tokens.css` 各主题 `--primary`/`--primary-text`；消费方 `web/src/theme.css` `.btn`、`.btn.secondary`、`.rarity-tag`、`.app-nav a.active`，以及各页价格/标题元素。
- **为什么当时未修**：`--primary` 是品牌色，改它属于"改视觉方向"，超出本次边界。
- **解决**：随 `c287e55` 采纳**加法式** `--primary-strong`（每个主题取比 `--primary` 深一档、对白字 ≥4.5:1 的色值），仅用于按钮填充/链接前景等**文字承载**场景；品牌标记（logo、色板、选中态描边）继续用 `--primary` 不变。11/11 主题 `--primary-strong` 对文本对比度均 ≥4.5:1。
- **截图**：`2026-10-31-ui-audit/web-products-desktop.png`（蓝色价格 5.34:1 达标，供对照）、`web-lantern-narrow.png`（灯笼主题，按钮为问题项）。

### P3 · 春主题 `--muted` 在页面背景上 4.33:1

- `spring` 的 `--muted #8a6d7c` 对 `--bg #fdf6f9` 为 4.33:1（略低于 AA），但对 `--surface` 为 4.61:1（达标）。受影响面为直接落在背景上的瞬态文字（"加载中…"、空态）。
- **解决**：随 `fa5a8aa` 将 spring `--muted` 微调至 `#876a79`，对 `--bg` 提升至 4.52:1，双场景达标。
- **截图**：`2026-10-31-ui-audit/web-home-desktop.png`。

## 不可审计项

- **`mobile/`（Taro RN）**：仓库无 H5 目标，需 RN/模拟器，本环境无法渲染。已按源级审计并同步 `mobile/src/theme.ts` 的 `--muted` 修复；建议后续在真机/模拟器上补一次渲染级复检。

## 缺失上下文

- 仓库无 `PRODUCT.md` / `DESIGN.md`。事实设计源：`docs/design/ui-system.md`（令牌/状态规范）+ `docs/design/prototypes/tokens.css`（原型令牌）。本次按此执行，未臆造缺失上下文。

## 正面发现

- **令牌架构**：结构令牌在 `:root` 且不被主题覆盖（"换主题不改布局"）；视觉令牌按 `[data-theme]` 单点收敛；`theme.css` 只消费令牌、无硬编码主题色（ui-system.md §1.3 纪律落实到位）。
- **四态组件**：`.state-box`（空/错/成）+ `.skeleton` 骨架屏统一，符合 ui-system.md §6。
- **主题切换**：`data-theme` 单点切换、即时生效、选择持久化，无需刷新。
- **桌面排版**：商品四列网格、价格层级（现价/划线原价/促销标签）、吸底购买栏、资讯表格渲染干净。
- **修复后回归**：18 个「路由 × 视口」组合 `overflow=false`、0 控制台错误、0 页面异常。

## 复核矩阵（逐组合实测）

> 审计修复后，按 9 条路由 × 2 个视口（1440×900 桌面 / 390×844 窄屏）共 18 组合，以 Playwright 1.63（chromium）对 `web/` dev server 实测；后端 API 用路由拦截注入数据态渲染（与「工具」节同法）。`overflow = scrollWidth > clientWidth`。

| 路由 | 视口 | clientWidth | scrollWidth | overflow |
|---|---|---|---|---|
| `/` | 1440×900 | 1440 | 1440 | false |
| `/` | 390×844 | 390 | 390 | false |
| `/products` | 1440×900 | 1440 | 1440 | false |
| `/products` | 390×844 | 390 | 390 | false |
| `/products/demo` | 1440×900 | 1440 | 1440 | false |
| `/products/demo` | 390×844 | 390 | 390 | false |
| `/user` | 1440×900 | 1440 | 1440 | false |
| `/user` | 390×844 | 390 | 390 | false |
| `/login` | 1440×900 | 1440 | 1440 | false |
| `/login` | 390×844 | 390 | 390 | false |
| `/market` | 1440×900 | 1440 | 1440 | false |
| `/market` | 390×844 | 390 | 390 | false |
| `/prizes` | 1440×900 | 1440 | 1440 | false |
| `/prizes` | 390×844 | 390 | 390 | false |
| `/roll` | 1440×900 | 1440 | 1440 | false |
| `/roll` | 390×844 | 390 | 390 | false |
| `/orders/demo` | 1440×900 | 1440 | 1440 | false |
| `/orders/demo` | 390×844 | 390 | 390 | false |

**修复后复评**：18/18 组合 `overflow=false`、0 控制台错误、0 页面异常；健康评分由审计基线 78/100 提升至 92/100（修复 P0 窄屏溢出、P1 次要文字对比度、P1 键盘焦点、P2 减弱动效后）。遗留清单已随 `c287e55` 收敛：采纳「加法式 `--primary-strong`」并把 web 价格/按钮/chip 与 mobile 按钮/Price 前景改用它后，11/11 主题 `--primary-strong` 对文本对比度均 ≥4.5:1（品牌主色前景 AA 已解决）；春主题 `--muted` 边缘对比度亦经 `#876a79` 提升至 4.52:1（达标）。

## 结论

本次修复 4 类问题（窄屏溢出、次要文字对比度、键盘焦点、减弱动效），全部通过回归验证；遗留 2 项（品牌主色前景 AA 与春主题边缘对比度）经 `c287e55`/`fa5a8aa` 采纳加法式 `--primary-strong` 与 `--muted` 微调后均已解决（11/11 主题 `--primary-strong` 对文本 ≥4.5:1，春主题 `--muted` 4.52:1）。**核心短板已清，达标门前不再有待拍板项。**
