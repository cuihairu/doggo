# 快速开始


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

# 构建后端解决方案
dotnet build server/Doggo.slnx

# 启动基础设施(MySQL 8.4 + Redis 7,带健康检查)
cp deploy/.env.example deploy/.env   # 按需修改密码与端口
docker compose -f deploy/docker-compose.yml up -d

# 安装 EF 工具并应用数据库迁移
dotnet tool install --global dotnet-ef
cd server
dotnet ef database update --project src/Doggo.Infrastructure --startup-project src/Doggo.Api
```

若主机 3306 / 6379 端口已被占用,在 `deploy/.env` 里改 `MYSQL_PORT` / `REDIS_PORT`,并在启动应用时用环境变量覆盖连接串:

```bash
ConnectionStrings__Default="Server=localhost;Port=13306;Database=doggo;User=doggo;Password=..."
ConnectionStrings__Redis="localhost:16379"
```

### 验证

```bash
curl http://localhost:<port>/healthz
# {"status":"ok","database":true,"redis":true}
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

- [x] 项目文档与架构设计(`docs/`)
- [x] 领域模型与数据层(`server/src/Doggo.Domain`,EF 迁移随部署执行)
- [x] 资讯爬虫与聚合管道(`server/src/Doggo.Scraper`)
- [x] 饰品行情同步(基于 vapor)(`server/src/Doggo.Infrastructure/Vapor`,Worker 定时同步)
- [x] 开箱模拟玩法(`server/src/Doggo.Infrastructure/Cases`)
- [x] REST API 与用户体系(`server/src/Doggo.Api`,JWT + Steam OpenID)
- [x] 管理后台(`admin/` 前端 + `/api/admin`)
- [x] 外部价格爬虫(`server/src/Doggo.PriceCrawler`,并入 Scraper 进程)
- [x] 漏斗数据分析(事件通道 + 每日聚合 + 后台漏斗页)
