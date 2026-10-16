# doggo UI system: tokens, themes and four-client reuse

[English](/design/ui-system.en) | [简体中文](/design/ui-system)

Companion prototypes: `docs/design/prototypes/` (static, openable in a browser, verifiable by screenshots). This page records the token table, the theme list, the component reuse relationships and "which client uses which set"; the implementation phase uses it as the acceptance baseline, grading each screen "matched / has differences / not implemented".

## 1. Principles

1. **A theme = colour tokens + pattern/decorations**, carrying no layout information.
2. **Switching themes never changes layout**: structural tokens (spacing, font sizes, grid, radius scale, durations) live in `:root` and no theme may override them; a theme block may only write visual tokens.
3. **The component layer knows nothing about themes**: `base.css` consumes tokens only and must not contain theme-colour literals; page structure contains no theme branches.
4. No third-party design assets; icons are self-drawn SVGs (CSS mask); the brand paw print reuses `docs/public/paw.svg`.

## 2. Token system (two layers)

### 2.1 Structural tokens (`:root`, fixed across all themes)

| Token | Meaning | Value |
|---|---|---|
| `--sp-1..5` | spacing scale 8/16/24/36/56px | stepped |
| `--fs-min/body/lead/title/h1` | font-size scale 12/14/16/20/28px | stepped |
| `--topbar-h` / `--tabbar-h` | top bar / bottom navigation height | 56px |
| `--radius-s/m/l` | radius scale | 6/8/14px |
| `--container` | web content width | 1200px |
| `--sidebar-w` | admin sidebar width | 220px |
| `--phone-w/h` | phone real-device frame | 375×780px |
| `--dur` / `--ease` | motion duration / easing | 160ms / ease-out |

The **only** structural item a theme may override is `--radius` (the visual radius, see the theme blocks); every other structural token stays untouched.

### 2.2 Visual tokens (one `[data-theme="..."]` block per theme)

| Token | Meaning |
|---|---|
| `--bg` | page background |
| `--surface` / `--surface-2` | card surface / secondary surface (placeholder, skeleton base) |
| `--text` / `--muted` | body text / de-emphasised text |
| `--border` | border |
| `--primary` / `--primary-text` | primary colour / text on the primary colour |
| `--primary-weak` | weak primary background (selected state, weakened buttons, skeleton shimmer) |
| `--danger` / `--success` | error / success |
| `--gold` | gold semantics such as coins and rarity |
| `--accent-2` | chart second-series colour |
| `--radius` | component radius (visual radius, themes may fine-tune it) |
| `--shadow` | card shadow |
| `--brand-mark` | brand paw-print colour |
| `--pattern` / `--pattern-opacity` | decorative pattern (consumed by `body::before`, z-index below content) |
| `color-scheme` | native control light/dark |

## 3. Theme list (11 sets)

| # | id | Name | Light/dark | Primary | Pattern | Use case |
|---|---|---|---|---|---|---|
| ① | `brand` | default (brand blue) | light | `#2f66d0` | none | whole site normally, admin normally |
| ② | `dark` | dark | dark | `#6a96f0` | fine diagonal lines | night browsing, mobile follows system, admin long shifts |
| ③ | `cny` | festival · Chinese New Year | light | `#c62828` | red diagonal grid + golden glow | Chinese New Year slots, spring festival campaigns |
| ④ | `lantern` | festival · Lantern Festival | light | `#e06a3c` | two-sided lantern glow | limited Lantern Festival evening atmosphere |
| ⑤ | `dragonboat` | festival · Dragon Boat | light | `#0f7a5c` | wave diagonal texture | Dragon Boat campaigns, green-family slots |
| ⑥ | `midautumn` | festival · Mid-Autumn | dark | `#e3b548` (moonlight gold) | moon halo + star dots | Mid-Autumn period; the dark festival sample |
| ⑦ | `national` | festival · National Day | light | `#d8322a` | horizontal stripe band + golden glow | National Day slots |
| ⑧ | `spring` | season · spring | light | `#e0688f` | petal dot matrix | March–May (optional) |
| ⑨ | `summer` | season · summer | light | `#0f9bb0` | diagonal texture + sunlight dots | June–August (optional) |
| ⑩ | `autumn` | season · autumn | light | `#b4622a` | falling-leaf diagonal texture | September–November (optional) |
| ⑪ | `winter` | season · winter | light | `#3d7ea6` | snow dots | December–February (optional) |

The complete palette for each set is in the second section of `prototypes/index.html` (the card renders in its own theme).

## 4. Theme switching mechanism

- Priority: URL fragment `#theme=xxx` > `localStorage` > page default.
- Switching action: write `<html data-theme>`, no re-render, no layout jump; prototype script is `prototypes/theme.js`; each client implements the equivalent at implementation time (web uses the same approach directly; miniapp/mobile use settings storage + style-variable sources).
- Decoration layer: `body::before` always paints `--pattern`, `--pattern-opacity` controls strength, `pointer-events:none`, z-index below the content layer.
- On the admin side, an operations calendar (the "theme slots" page in settings) switches festival/season themes automatically by date range; **the user's manual choice wins over the operations slots**.

## 5. Component layer and four-client reuse

`prototypes/base.css` is the component vocabulary (consuming tokens only). Reuse across the four clients:

| Component | web | miniapp | mobile | admin |
|---|---|---|---|---|
| top bar / navigation (topbar/nav) | ✔ | — (appbar+tabbar) | — (same as miniapp) | sidebar layout |
| card / grid (card, grid cols-2/3/4) | ✔ | ✔ | ✔ | ✔ |
| button (btn + ghost/danger/plain/sm/block) | ✔ | ✔ | ✔ | antd Button, semantics aligned |
| badge / tag (badge, chip) | ✔ | ✔ | ✔ | antd Tag, semantics aligned |
| table (table) | ✔ | compact rows (tick-row) | same as miniapp | antd Table |
| skeleton (skeleton + shimmer) | ✔ | ✔ | ✔ | ✔ |
| empty/error/success (empty/error-box/success-box) | ✔ | ✔ | ✔ | ✔ |
| toast | ✔ | ✔ | ✔ | antd message |
| KPI card (kpi) | — | — | — | ✔ |
| funnel bar (funnel-step/funnel-bar) | — | — | — | ✔ |
| phone real-device frame (phone) | prototypes only | ✔ | ✔ | — |
| overlay (modal/drawer) | — | half-screen sheet (to be refined) | same as miniapp | ✔ |
| theme picker (theme-switch/theme-pick) | ✔ (top right + profile) | ✔ (Me → theme appearance) | ✔ (same as miniapp) | ✔ (top bar) |

## 6. State component matrix (empty / loading / error / success)

| Flow | Loading | Empty | Error | Success | Prototype |
|---|---|---|---|---|---|
| product / market lists | skeleton card | reason + clear-filters action | retry button | normal list (first page + load more) | web/states, both state pages |
| checkout / payment | skeleton + disabled button | empty card when no discount available | channel timeout + retry / switch method + keep the order countdown | payment success + go to the next case-opening step | web/states 1, web/checkout |
| case opening | opening animation placeholder | — (there is always a result) | payment incomplete, order retained | result card + deposit prize / claim | miniapp/order, both state pages |
| profile | skeleton rows | not-logged-in login prompt | fetch failure retry | data ready | web/account, both profile pages |

Rules: the empty state must explain the reason and offer a next action; errors must be retryable or offer an alternative path; success must give a clear next step; toasts are transient feedback only — state changes land on page elements.

## 10. Implementation status (2026-10-13)

All four clients have tokenisation in place; screen-by-screen comparison follows. Status terms: **matched** = structure and component semantics agree; **has differences** = present but items missing or shaped differently; **not implemented** = no corresponding implementation.

### 10.1 Tokenisation in place

| Client | Token source | Switching mechanism | Entry |
|---|---|---|---|
| web | `web/src/tokens.css` (structural `:root` + 11 theme blocks) | `theme.ts`: `#theme=xxx` > localStorage > brand, writes `<html data-theme>` | top bar select + profile "appearance and theme" card |
| mobile | `mobile/src/theme.ts` (11 Palettes) | follows the system by default; manual choice writes an AsyncStorage override; "follow system" is the 12th item | profile "appearance and theme" card |
| miniapp | `miniapp/src/app.scss` (`page` structural tokens + 11 theme blocks) | `theme.ts`: Taro storage + `setBackgroundColor`/`setNavigationBarColour` to sync native chrome; `ThemeRoot` writes `data-theme` at the page root | Me → theme appearance |
| admin | `admin/src/theme.ts` (11 sets → antd token mapping) | `ThemeGate` (umi `rootContainer`) wraps `ConfigProvider`; dark/midautumn use `darkAlgorithm` | top bar dropdown (`actionsRender`) |

### 10.2 Screen-by-screen comparison

**web** (5 prototype screens)

| Screen | Status | Differences |
|---|---|---|
| home | has differences | prototype double-banner → implementation horizontal-scrolling image; market quick view has no up/down column; category chips → select; news has no dual-column mini list |
| product | has differences | no breadcrumb, thumbnails or separate market reference-price comparison table (needs market day change data); "included contents" badge grid, strikethrough original price, promotion stock badge, coin deduction and coupon selection, and the bottom buybar are in place |
| checkout | has differences | a standalone confirm-order page is in place: order-content card (icon/name/plan), payment-method card (Alipay simulated selected + WeChat to be wired), payment countdown badge (server-side payDeadlineUtc), agreement checkbox, amount breakdown and three-step explainer; no first-order discount row (the server has no such field) |
| account | has differences | theme card has 1 colour dot per item (prototype is a 6-colour swatch grid); no "auto-switch/sync across clients" chip or current-theme-name hint; coupon centre is new in the implementation |
| states | has differences | skeleton shimmer, empty state (icon + explanation + action button), error box (retry), and success box (next step after payment) follow the prototype; the toast badge system and form inline errors are still plain text |

**miniapp** (6 prototype screens)

| Screen | Status | Differences |
|---|---|---|
| home | has differences | no appbar, banner slot, four-square entry or operations recommendation row; market quick view has no up/down column and cannot jump into market; adds an announcement card and news feed |
| market | has differences | search box + game/type filter chips and red/green day changes (`changeBps`, computed server-side from the two most recent daily closes) are in place; still no in-site vs external reference-price comparison table; the list renders classId rather than wear tier; no entry jumps into market (the page is orphaned) |
| product | has differences | strikethrough original price, plan semantics (single/three/ten opens), the "included contents" badge grid (quality-colour border + ★quality + reference price) and the bottom buybar (minimum single open + open now, fixed + safe area) are in place; still no CS2 badge, stock text, or coin deduction/coupon selection; a confirm dialog before ordering replaces the standalone confirm-order page (product/plan/payment method/amount payable/15-minute deadline) |
| order | partially in place | the miniapp has no order route: confirmation runs through a product-page dialog, with no payment countdown (the mock channel pays successfully on submit); the opening result card is in place (gold glow / rarity badge / reference price / deposit-prize and claim hints); claiming back to Steam depends on the vapor integration and is not yet operable |
| profile | has differences | the avatar head (first letter of nickname + masked account · invite code), the stat-row trio (coins/coupons/invites) and this week's check-in progress are in place; theme appearance is an inline 11-colour palette card (the prototype has an entry row, functionally equivalent); the menu group lacks order history/settings |
| states | has differences | skeleton (pulsing card), empty state (badge + explanation + action button), error box (warning badge + retry / alternative path) are in place; the success box is wired into the order payment flow only, other feedback is still toast |

**mobile** (6 prototype screens)

| Screen | Status | Differences |
|---|---|---|
| home | has differences | no banner or four-square entry (the implementation has a 6-text-button grid), no operations recommendation; market quick view has no up/down %; adds a news card; the prototype tabbar is not implemented (§9-1) |
| market | has differences | search box + game/type filter buttons and red/green day changes are in place; still no in-site vs external reference-price comparison; adds a 30-day closing SVG trend line (not in the prototype) |
| product | has differences | no 180px hero image; plans are text buttons, not plan-rows; the coin deduction chip (balance cap, 1 coin = 1 cent), coupon list, the "included contents" badge grid (quality-colour border + ★quality + reference price) and the bottom buybar (minimum single-open price + open now) are in place |
| order | has differences | no order-list endpoint (the app remembers the last 10 ids locally + manual input); the pending-payment countdown badge (mm:ss, server-side payDeadlineUtc), payment method "Alipay (simulated)", product icon/name/plan and coupon deduction row are in place; still no standalone confirm-order page or agreement checkbox; adds 4s polling; 7 statuses (the prototype has 4) |
| profile | has differences | the me-head avatar head (first letter of nickname + masked phone/email · invite code), the stat-row trio (coins/coupons/invites) and this week's check-in progress (signed n/7 · today +N + check-in button) are in place; the "appearance and theme" card is implemented and more complete (11-colour palette + follow system); still no settings page |
| states | has differences | skeleton (pulsing card), empty state (icon + explanation + action), error box (warning icon + retry) and the payment success box are in place; the opening result card (latest prize pinned: gold circular glow, rarity/quality badge, reference price, deposit-prize and claim hints) belongs to the order/opening loop (§10.3-2) |

**admin** (6 prototype screens)

| Screen | Status | Differences |
|---|---|---|
| dashboard | has differences | different KPI definitions (users/orders vs the prototype's PV/UV); the trend is a line rather than bars; the ranking is Top10 revenue not Top5 best-sellers; missing the funnel quick-view card and the system status card |
| funnel | has differences | the prototype's 3 grouped tables (client/channel/daily bars) → a single-row table + day/week filter; no "overall conversion / biggest drop-off" conclusion row; step wording "add to cart (order intent)" |
| orders | has differences | no top 4 KPIs, no filters (order number/status/date + refresh); columns lack user/content, multiple coin/coupon deductions + prizes; status enum 0-6 (the prototype has 5 states); the refund modal has no preset reasons or refund-coupon/coin chips |
| products | has differences | no filters or "new product"; columns lack game/type/stock; statuses are plain text not badges; the drawer has no drop table/price history/external reference price; the exchange-rate fix uses window.prompt |
| settings | has differences | site parameters became editable (`GET/PUT /api/admin/settings` over the existing SiteSetting table, inline save + add/overwrite, loading skeleton / error retry / empty state); the crawler site overview reads the aggregated site mapping count and pending count from `GET /api/admin/price-crawl/maps`; payment channels and the theme operations calendar are in place server-side (`d389f96` / `2ac84f0`: the PaymentChannel entity and channel config endpoints, Banner delivery ranges and window-overlap validation), with console pages yet to be wired and a stale notice still showing on the page |
| states | has differences | in place: Dashboard skeleton + error retry (replacing the '-' placeholder and silent catch), Funnel skeleton/empty state with error retry and "clear filters", the product drawer skeleton + error; 13 ProTables in Orders/Users/Rolls/Products/Articles changed to error Alert + retry; the login page and inline form errors stay plain text |
| Users/Articles/Rolls/Login | no prototype counterpart | extra implementation pages (the prototype sidebar has placeholder links only) |
| PriceCrawl | no prototype counterpart | the "data analysis → price crawler" page: mapping review (grouped by status + confirm/reject, read-only comparison) and a 7/30-day price comparison table (ProTable + error retry + empty state) |

### 10.3 Difference list (by class)

**Structural differences (in the prototype, missing from the implementation, by priority)**

1. The state component matrix (§6): landed for web, mobile and miniapp (web `components/States.tsx` wired into nine pages; mobile `ui.tsx` StateIco/Skeleton/EmptyState/SuccessBox with Msg upgraded to an error box, wired into product/market/opening/product-detail/news-detail/order; miniapp `src/states.tsx` Skeleton/EmptyState/ErrorBox/SuccessBox — the miniapp has no SVG components, badges use stroked circles + glyphs — wired into product/market/product-detail/news-detail/opening, and the opening page's not-logged-in error box offers a "go to login" alternative path); admin `src/components/States.tsx` (PageLoading/ErrorAlert/EmptyAction) wired into Dashboard/Funnel/the product drawer and 13 ProTables, errors no longer silent.
2. The checkout/opening loop: no standalone confirm-order page, payment-method selection, timeout countdown, or opening result card (open-glow / rarity badge / deposit prize / claim). web landed: confirm-order page OrderPayPage (order content, payment method, payment countdown, agreement, amount breakdown) + opening result card CaseOpeningPage (open-glow, rarity badge, reference price, inspection link; prizes are deposited automatically by the server); mobile landed the order card (product icon/name/plan, payment countdown badge, payment method, coupon deduction) + the opening page result card (gold glow, rarity badge, reference price, deposit-prize/claim hints); miniapp replaces the confirm-order page with a product-page dialog (the miniapp has no order route) + the opening page result card (gold glow, rarity badge, reference price); shared types synced for productName/productIconUrl/planTitle and seat prize snapshots. All four clients lack a claim-back-to-Steam entry, which depends on the vapor integration.
3. Product detail: no "included contents" badge grid, market reference-price table, coin deduction/coupon selection or bottom buybar. mobile landed the coin deduction chip (including the balance cap, submitted with createOrder's coinDeduction), the "included contents" badge grid and the bottom buybar; the coupon was originally a list; miniapp landed the strikethrough original price, plan semantics, badge grid and buybar, still without coupon/coin selection; web landed the ProductDetailPage badge grid (first 8 items + collapse), strikethrough original price, promotion stock badge, coin deduction and coupon selection (`/api/coupons/mine`), and the bottom buybar (amount payable after coupon estimate + open now). The market reference-price comparison table landed per the 代决 criteria (2026-10-16, price crawler design §12): the web trend page shows the external median, the in-site close and the spread; per-site rows remain admin-side only.
4. Admin settings page: site parameters and the crawler site overview are in place (new `GET /api/admin/settings`, `PUT /api/admin/settings/{key}`, key/value shape validation + 5000-character cap, 2 new test cases); payment channels and the theme operations calendar landed server-side (`d389f96` channel config entity and upsert, `2ac84f0` Banner delivery ranges and window-overlap validation), with console pages yet to be wired.
5. Market page: day changes and filters are in place on mobile/miniapp (the server-side `MarketItemDto` gained `changeCents`/`changeBps`, computed from the two most recent daily closes, null when there is only one daily line, 2 new test cases); the in-site vs external reference-price comparison table is done (2026-10-16 代决, price crawler design §12): `GET /api/market/items/{id}/reference` returns the median of the latest non-suspect converted prices across confirmed sites within the last 7 days, the latest in-site close and the spread; the web trend page renders the comparison block; per-site rows remain admin-side read-only.

**Tokenisation differences**

6. web: structural tokens are not fully consumed (the nav is 52px high without using `--topbar-h`, paddings/font sizes are scattered hardcoded px values); `--gold`/`--accent-2` have no consumers; up/down colours use `--danger`/`--success` (A-share semantics, no `--up`/`--down` tokens).
7. mobile: the `--pattern` layer does not render in RN (no CSS pseudo-elements); only colour/radius tokens take effect; the native navigation bar `navigationBarBackgroundColor` is a static white background (unchanged in the dark theme).
8. miniapp: the native tabBar colours (color/selectedColor/backgroundColor) are static and not updated on theme switch (§9-2); `backgroundTextStyle` is static light; `--pattern-opacity` has no consumer (alpha is baked into the gradient).
9. admin: the sider `navTheme` is only recalculated from the runtime config on route change (a codegen limitation); page content re-themes instantly through ConfigProvider.

**Known limitations (implementation-phase decisions, not defects)**

10. web switching themes does not write back to the URL hash (the prototype writes the hash so screenshots are reproducible); it uses localStorage persistence instead, to keep in-app navigation from polluting the URL.
11. The miniapp pattern layer uses a `.app-root` background-image overlay instead of `body::before` (wxss `::before` is a positioned element and would sit above the content).
12. The admin top-bar dropdown offers all 11 themes (the prototype only envisaged a brand/dark quick switch; see §9-3, pending a decision).

### 10.4 Pending decisions (carried from §9)

- §9-1 miniapp tabBar 3→4 (+market) and the mobile bottom tab: affects the structure of the home/market screens.
- §9-2 miniapp native tabbar colour: keep the native default / custom tabbar / (compromise) keep the native bar but sync the theme colour with `Taro.setTabBarStyle` — the compromise is not implemented, pending a decision.
- §9-3 whether admin should offer a dark quick switch by default (the prototype only puts a dropdown in the top bar).
- §9-4 whether season themes should be enabled by default.
- §9-5 mobile "follow system" as the 12th item: implemented per the recommendation.
- §9-6 which year's lunar calendar the theme operations calendar ranges follow, and who maintains them (depends on the admin settings page).

## 7. Which client uses which theme set

| Client | Default | User-selectable | Notes |
|---|---|---|---|
| web front-end | `brand` | all 11 sets | top right + profile "appearance and theme" |
| miniapp | `brand` | all 11 sets | "Me → theme appearance"; dark theme recommended to follow the system (pending a decision) |
| mobile | follow system (dark/light) | all 11 sets (manual selection overrides the follow) | `theme.ts` currently has hardcoded light/dark; expand to token sources |
| admin | `brand` | `brand` / `dark` (+ optional festival) | long shifts lean on dark; festival themes are not default and are driven by the operations calendar for the front-end |

## 8. Implementation mapping (prototype → current state per client)

| Client | Current state | Rework approach |
|---|---|---|
| web | `web/src/theme.css` single file (imported by main.tsx) | split into "structural tokens + visual tokens"; `data-theme` written on `html`; add the profile theme card |
| miniapp | `miniapp/src/app.scss` constant colours | SCSS constants → CSS variables; native tabbar colours cannot follow the theme, so either a custom tabbar or keeping the native default (pending a decision) |
| mobile | `mobile/src/theme.ts` light/dark Palettes, `useColorScheme` follows the system | expand Palettes to 11 token sources; keep follow-system as the default, manual choice writes storage |
| admin | antd + UmiJS, hex literals scattered in pages (e.g. Dashboard `#2f66d0`) | funnel colours into `ConfigProvider theme` tokens; use `theme.darkAlgorithm` for dark; add the funnel page/theme slots per this page and the funnel design doc |

What the four clients share is only **token definitions and component semantics** (one token source, consumed by many clients) — no forced sharing of runtime code — matching the current "clients share only the contract" stance.

## 9. Pending decisions

1. Whether to expand the miniapp tabBar from 3 (home/products/me) to 4 (+market): the prototype is designed for 4 tabs, adopt or not; mobile adds a matching bottom tab (currently the home grid navigates).
2. The miniapp native tabbar cannot change colour with the theme: keep the native default colours or switch to a custom tabbar.
3. Whether admin should offer a dark quick switch by default (the prototype only puts a dropdown in the top bar).
4. Whether season themes should be enabled by default (or festivals only, with seasons as backup).
5. Whether mobile's "manual theme selection overrides system following" needs "follow system" as the 12th option (recommended: yes).
6. Which year's lunar calendar the theme operations calendar ranges follow and who maintains them (the admin settings page has a slot for this).
