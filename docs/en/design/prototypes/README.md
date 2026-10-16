# doggo four-client UI prototypes (static HTML/CSS)

[English](/en/design/prototypes/README) | [简体中文](/design/prototypes/README)

The first deliverable of the UI/UX redo: prototypes first, user review, code afterwards. Everything is a static page that a browser can open directly — no build step, no third-party design assets; icons are SVGs drawn in the repository (rendered with CSS mask) and the brand paw print reuses the path in `docs/public/paw.svg`.

## How to open them

Open `index.html` (the master index) in any browser, or open an individual client page. Switching themes:

- the dropdown in the top-right corner of each page (for admin it is at the top of the sidebar);
- or append a fragment such as `#theme=midautumn` to the URL to jump straight to a theme (reproducible for screenshots); the pick is written to localStorage.

11 themes: `brand` default (brand blue) / `dark` dark / `cny` Chinese New Year / `lantern` Lantern Festival / `dragonboat` Dragon Boat Festival / `midautumn` Mid-Autumn Festival / `national` National Day / `spring` spring / `summer` summer / `autumn` autumn / `winter` winter.

## File structure

```
prototypes/
├── tokens.css        主题令牌：结构令牌(:root，全主题共享) + 11 套视觉令牌
├── base.css          组件层：只消费令牌，不含任何主题色字面量
├── theme.js          主题切换（#theme= > localStorage > 页面默认）
├── index.html        索引 + 11 套主题色板
├── web/              web 前台：home / product / checkout / account / states
├── miniapp/          小程序（真机框 375×780）：home / product / market / order / profile / states
├── mobile/           手机端（同组件层）：home / product / market / order / profile / states
└── admin/            后台：dashboard / products / orders / funnel / settings
```

## Screenshot acceptance checklist

Every client is walked through as "home/list → detail → order or key action → settings or profile", with at least these screenshots per screen: the default theme + one festival theme + dark. The state pages get the four states (empty / loading / error / success) as well. Acceptance criteria:

1. Layout does not change with the theme (compare screens side by side after switching);
2. Every screen's `proto-note` annotation corresponds to the real implementation file;
3. The four states are present, the empty state has a next step, the error state can be retried.

## Relation to the implementation

- `docs/design/ui-system.md`: the token table, the theme list, component reuse, and "which client uses which theme".
- Implementation follows the prototypes screen by screen, each screen graded "matched / has differences / not implemented" with screenshot evidence; claiming a theme is done just because the colours changed is not allowed.
