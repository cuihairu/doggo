# 部署

本文描述用 Docker Compose 部署 Doggo 全栈的流程:MySQL、Redis、API、迁移、Scraper、Worker、主站与后台。镜像由每日构建流水线发布到 GHCR(`nightly` 滚动 tag);正式版本发布是手动步骤,不在这条链路上。

## 前置条件

- 一台 Linux 主机,Docker Engine 与 docker compose 插件可用
- 一个域名,指向主机;TLS 证书(推荐 certbot 签发)
- 目录约定:`/opt/doggo` 放仓库(clone 或 rsync)

## 配置

```bash
cd /opt/doggo
cp deploy/.env.example deploy/.env
```

必填三项:

| 变量 | 说明 |
| --- | --- |
| `MYSQL_ROOT_PASSWORD` | MySQL root 密码,备份脚本也用它 |
| `MYSQL_PASSWORD` | 应用账号 `doggo` 的密码 |
| `AUTH_SECRET_KEY` | JWT 签名密钥,`openssl rand -base64 48` 生成 |

可选项:端口(`WEB_PORT`/`ADMIN_PORT`/`API_PORT`)、Vapor 对接(`VAPOR_*`,留空即关闭)、微信登录(`WECHAT_APP_ID`/`WECHAT_APP_SECRET`,留空时 `/api/auth/wechat` 返回 503)。

所有敏感配置只走环境变量,不落仓库。

## 启动

```bash
cd /opt/doggo
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.prod.yml ps
```

服务职责:

| 服务 | 说明 |
| --- | --- |
| `mysql` / `redis` | 数据层,带健康检查;数据在 `mysql-data` / `redis-data` 卷里 |
| `migrator` | 一次性跑 `dotnet ef database update`,失败即非零退出 |
| `api` | 等待 migrator 成功后才启动,监听容器 8080(宿主默认 8082) |
| `scraper` / `worker` | 资讯抓取与后台任务,共用同一个 `Dockerfile.worker`,以 `PROJECT` 参数区分 |
| `web` | 主站静态文件 + `/api` 反代,宿主默认 8080 |
| `admin` | 管理后台静态文件 + `/api` 反代,宿主默认 8081 |

本机源码构建(不走 GHCR)时,compose 会直接 `build`,无需额外操作。

## nginx 网关(域名 + HTTPS)

`deploy/nginx/doggo.conf.template` 是宿主 nginx 的模板:HTTP 跳 HTTPS、主域名(web + `/api`)、admin 独立域名、`/api/admin/` 的 IP 白名单占位。替换 `DOGGO_DOMAIN`、`ADMIN_DOMAIN`、`ADMIN_ALLOW_IP`、`CERT_PATH` 后:

```bash
cp deploy/nginx/doggo.conf.template /etc/nginx/sites-available/doggo.conf
# 编辑上面的占位符
nginx -t && systemctl reload nginx
```

certbot 签发(示例):

```bash
certbot --nginx -d DOGGO_DOMAIN -d ADMIN_DOMAIN
```

admin 域名建议先填好 `allow` 列表再放开 `deny all`,后台入口与 `/api/admin/` 都只对白名单开放。

## 备份

`deploy/backup/backup-mysql.sh` 每晚做一次 `--single-transaction` 全量 dump(gzip),保留 7 天日备 + 8 个周备,周日额外归档到 weekly 池:

```bash
cp deploy/backup/backup-mysql.sh /usr/local/bin/doggo-backup.sh
chmod 700 /usr/local/bin/doggo-backup.sh
crontab -e   # 17 3 * * * /usr/local/bin/doggo-backup.sh >> /var/log/doggo-backup.log 2>&1
```

恢复:

```bash
docker exec -i doggo-mysql sh -c \
  'exec mysql -uroot -p"$MYSQL_ROOT_PASSWORD" doggo' < /var/backups/doggo/daily/doggo-XXXX.sql.gz
```

Redis 数据是缓存/队列性质,不做持久化备份;唯一状态数据在 MySQL。备份目录建议再做一次异地同步(rclone/rsync)。

## 升级

```bash
cd /opt/doggo
git pull
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build
```

`up -d --build` 会重建变更的服务;migrator 在 api 之前执行,新列/新索引先落库,老进程短暂混跑是安全的(迁移遵循只增不改的惯例)。

## 持续交付

`.github/workflows/daily.yml` 的 `images` job 每日构建五个镜像推到 `ghcr.io/<owner>/doggo-{api,scraper,worker,web,admin}:nightly`(48 小时无提交自动跳过)。要自动滚动到服务器:设仓库变量 `DEPLOY_ENABLED=true` 与 `DEPLOY_SSH_HOST`/`DEPLOY_SSH_USER`/`DEPLOY_SSH_KEY` 三个 secret,`deploy` job 会 ssh 到目标机执行 `compose pull && up -d`。

`nightly` 是滚动 tag,不做版本号 tag、不发 GitHub Release;发正式版由用户手动给出版本号后另行操作。
