# 快速开始

项目处于早期建设阶段,本页描述目标使用方式;对应模块落地后会同步更新。

## 环境要求

| 依赖 | 版本 | 用途 |
| --- | --- | --- |
| .NET SDK | 10.0 | 服务端、爬虫、机器人 |
| Docker + Docker Compose | 稳定版 | 部署(MySQL、Redis 等) |
| Node.js | 20+ | 文档站点(VitePress) |

## 本地开发

```bash
# 克隆仓库
git clone https://github.com/cuihairu/doggo.git
cd doggo

# 构建解决方案(代码落地后可用)
dotnet build

# 启动基础设施
docker compose up -d mysql redis
```

## 文档站点

文档使用 VitePress 构建,位于 `docs/` 目录:

```bash
pnpm install

# 本地预览,默认 http://localhost:5173
pnpm docs:dev

# 构建静态页面到 docs/.vitepress/dist
pnpm docs:build
```

## 路线图

- [x] 项目文档与架构设计
- [ ] 领域模型与数据层
- [ ] 资讯爬虫与聚合管道
- [ ] 饰品行情同步(基于 vapor)
- [ ] 开箱模拟玩法
- [ ] REST API 与用户体系
- [ ] 管理后台
