# Doggo web 用户站

C 端 Web 用户站（React 19 + Vite + TypeScript）：资讯、行情、商品与开箱下单、订单支付、roll 房、用户中心。

## 命令

```bash
pnpm install
pnpm dev      # 本地开发，/api 走 Vite 代理
pnpm build    # tsc -b && vite build，产物到 dist/
pnpm lint     # oxlint
```

## 说明

- 请求封装在 `src/api/client.ts`，事件埋点在 `src/api/events.ts`（漏斗上报）。
- 主题与设计令牌见 `docs/design/ui-system.md`。
- 生产部署：`deploy/docker-compose.prod.yml` 的 web 服务（宿主默认 8080），镜像由每日构建发布到 GHCR（`.github/workflows/daily.yml`）。
