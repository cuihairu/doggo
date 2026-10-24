# doggo 四端 UI 原型（静态 HTML/CSS）


UI/UX 重做的第一交付物：先原型、用户过目、再动代码。全部为静态页面，浏览器直接打开即可，无构建、无第三方设计素材；图标为仓内自绘 SVG（CSS mask 渲染），品牌爪印复用 `docs/public/paw.svg` 的路径。

## 打开方式

任意浏览器打开 `index.html`（总索引），或直接打开某一端页面。切换主题：

- 每页右上角下拉（admin 在侧栏顶部）；
- 或 URL 追加 `#theme=midautumn` 这类片段直接定位（截图可复现）；选择会写入 localStorage。

11 套主题：`brand` 默认（品牌蓝）/ `dark` 暗色 / `cny` 春节 / `lantern` 元宵 / `dragonboat` 端午 / `midautumn` 中秋 / `national` 国庆 / `spring` 春 / `summer` 夏 / `autumn` 秋 / `winter` 冬。

## 文件结构

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

## 截图验收清单

每个客户端按「首页/列表 → 详情 → 下单或关键动作 → 设置或个人页」走一遍，每屏至少截：默认主题 + 1 个节日主题 + 暗色；状态页（states）额外截 空态 / 加载 / 错误 / 成功 四态。验收标准：

1. 布局不随主题变化（切换主题逐屏对比）；
2. 每屏的 `proto-note` 标注与真实实现文件对应得上；
3. 状态四件套齐备，空态有下一步动作、错误可重试。

## 与实现的关系

- `docs/design/ui-system.md`：令牌表、主题清单、组件复用关系、"哪端用哪套"。
- 实现阶段按原型逐屏做，每屏给「已对上 / 有差异 / 未实现」三档 + 截图证据；不允许只改颜色就声称完成主题。
