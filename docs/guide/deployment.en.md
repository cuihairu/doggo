# Deployment

[English](/guide/deployment.en) | [简体中文](/guide/deployment)

This page describes how to deploy the full Doggo stack with Docker Compose: MySQL, Redis, API, migrations, Scraper, Worker, the web site and the admin console. Images are published to GHCR by the daily build pipeline (rolling `nightly` tag); releasing a formal version is a manual step and is not part of this chain.

## Prerequisites

- One Linux host with Docker Engine and the docker compose plugin available
- A domain pointed at the host, plus a TLS certificate (certbot recommended)
- Directory convention: `/opt/doggo` holds the repository (clone or rsync)

## Configuration

```bash
cd /opt/doggo
cp deploy/.env.example deploy/.env
```

Three required values:

| Variable | Meaning |
| --- | --- |
| `MYSQL_ROOT_PASSWORD` | MySQL root password; the backup script uses it too |
| `MYSQL_PASSWORD` | Password for the `doggo` application account |
| `AUTH_SECRET_KEY` | JWT signing key, generate with `openssl rand -base64 48` |

Optional: ports (`WEB_PORT` / `ADMIN_PORT` / `API_PORT`), vapor integration (`VAPOR_*`, leave empty to disable), WeChat login (`WECHAT_APP_ID` / `WECHAT_APP_SECRET`, when empty `/api/auth/wechat` returns 503).

All sensitive configuration goes through environment variables only and is never committed to the repository.

## Starting

```bash
cd /opt/doggo
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.prod.yml ps
```

What each service does:

| Service | Description |
| --- | --- |
| `mysql` / `redis` | Data layer with health checks; data lives in the `mysql-data` / `redis-data` volumes |
| `migrator` | Runs `dotnet ef database update` once and exits non-zero on failure |
| `api` | Starts only after the migrator succeeds; listens on container port 8080 (host default 8082) |
| `scraper` / `worker` | News scraping and background jobs, sharing one `Dockerfile.worker` and differing by the `PROJECT` argument |
| `web` | Static files for the main site plus an `/api` reverse proxy, host default 8080 |
| `admin` | Static files for the admin console plus an `/api` reverse proxy, host default 8081 |

When building from local source (not from GHCR), compose simply `build`s and needs no extra step.

## nginx gateway (domain + HTTPS)

`deploy/nginx/doggo.conf.template` is the host nginx template: HTTP redirects to HTTPS, the main domain (web + `/api`), a separate admin domain, and the IP allowlist for `/api/admin/` is a commented placeholder (uncomment the allow/deny lines and fill in real addresses yourself). After replacing `DOGGO_DOMAIN`, `ADMIN_DOMAIN` and `CERT_PATH`:

```bash
cp deploy/nginx/doggo.conf.template /etc/nginx/sites-available/doggo.conf
# edit the placeholders above
nginx -t && systemctl reload nginx
```

certbot issuance (example):

```bash
certbot --nginx -d DOGGO_DOMAIN -d ADMIN_DOMAIN
```

For the admin domain, fill in the `allow` list before opening up `deny all` — both the admin entry point and `/api/admin/` should stay allowlist-only.

## Backups

`deploy/backup/backup-mysql.sh` runs one `--single-transaction` full dump (gzip) every night, keeping 7 daily backups plus 8 weekly ones, with Sunday also archived into the weekly pool:

```bash
cp deploy/backup/backup-mysql.sh /usr/local/bin/doggo-backup.sh
chmod 700 /usr/local/bin/doggo-backup.sh
crontab -e   # 17 3 * * * /usr/local/bin/doggo-backup.sh >> /var/log/doggo-backup.log 2>&1
```

Restore:

```bash
docker exec -i doggo-mysql sh -c \
  'exec mysql -uroot -p"$MYSQL_ROOT_PASSWORD" doggo' < /var/backups/doggo/daily/doggo-XXXX.sql.gz
```

Redis data is cache/queue in nature and is not backed up durably; the only stateful data is in MySQL. Consider syncing the backup directory off-host as well (rclone/rsync).

## Upgrades

```bash
cd /opt/doggo
git pull
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build
```

`up -d --build` rebuilds only changed services; the migrator runs before the api, so new columns and indexes land first and a brief mix of old and new processes is safe (migrations follow an additive-only convention).

## Continuous delivery

The `images` job in `.github/workflows/daily.yml` builds five images every day and pushes them to `ghcr.io/<owner>/doggo-{api,scraper,worker,web,admin}:nightly` (auto-skipped when there are no commits for 48 hours). To roll to the server automatically, set the repository variable `DEPLOY_ENABLED=true` and the `DEPLOY_SSH_HOST` / `DEPLOY_SSH_USER` / `DEPLOY_SSH_KEY` secrets; the `deploy` job then sshes to the target machine and runs `compose pull && up -d`.

`nightly` is a rolling tag: no version-number tags and no GitHub Release. A formal release happens only after the user supplies a version number.
