---
layout: home

hero:
  name: Doggo
  text: Open-source gaming news platform
  tagline: CS2 / Dota 2 news aggregation, item market data and case opening, built on C# / .NET, deployed on Linux and Docker
  image:
    src: /logo.svg
    alt: Doggo
  actions:
    - theme: brand
      text: Getting Started
      link: /guide/getting-started
    - theme: alt
      text: Features
      link: /guide/features
    - theme: alt
      text: GitHub
      link: https://github.com/cuihairu/doggo

features:
  - icon: 📰
    title: News Aggregation
    details: Scrapes official announcements, patch notes and esports updates for CS2 and Dota 2, then cleans and dedupes them into a single feed.
  - icon: 📈
    title: Item Market
    details: Syncs weapon case and item metadata, tracks Steam market prices, and provides market lookups and trend charts.
  - icon: 🎁
    title: Case Opening
    details: Case-opening gameplay built on weapon case drop probabilities and wear tiers, with inventory management and opening records.
  - icon: 🤖
    title: Steam Bot
    details: Steam-side market sync, inventory and trading are implemented in vapor; Doggo integrates them through its API.
  - icon: 🛠️
    title: Admin Console
    details: Operations UI for users, items, cases, news, orders and statistics.
  - icon: ⚙️
    title: Single Tech Stack
    details: Server, scraper and bot all run on C# / .NET, natively on Linux.
---

## UI Preview

The screenshots below are taken from a locally running instance (seed data) and are described feature by feature.

### Player Web

#### Home

![Player home](/screenshots/web-home.png)

Aggregates the latest news, market entry points and shop recommendations; the top navigation covers news, market, shop and roll rooms.

#### News Detail

![News detail](/screenshots/web-news-detail.png)

The article body is cleaned and deduped after scraping; the source and publish time are shown.

#### Market List

![Market list](/screenshots/web-market-list.png)

Items can be filtered by game and type, showing the latest price and update time.

#### Price Trend

![Price trend](/screenshots/web-market-trend.png)

A single item shows a daily trend chart plus an open, high, low, close price history table.

#### Shop List

![Shop list](/screenshots/web-product-list.png)

On-sale weapon cases are listed in listing order with price and category.

#### Case Detail

![Case detail](/screenshots/web-product-detail.png)

Shows the case contents, drop probabilities, wear tiers and the purchasable plans.

#### Order Payment

![Order payment](/screenshots/web-order-pay.png)

Placing an order starts a payment countdown; payment channel selection and confirmation follow step by step.

#### Team Case Opening

![Team case opening](/screenshots/web-case-opening.png)

Group case opening allocates prizes by seat; claimed prizes are moved to the inventory automatically.

#### My Prizes

![My prizes](/screenshots/web-my-prizes.png)

Opening results are kept in the inventory; wear and reference price are visible.

#### Login

![Login page](/screenshots/web-login.png)

Phone number plus password login; opening, orders and inventory become available after signing in.

### Admin Console

#### Admin Login

![Admin login](/screenshots/admin-login.png)

Admin account login; non-admin access is redirected back to the login page.

#### Dashboard

![Dashboard](/screenshots/admin-dashboard.png)

Summarises users, orders and revenue, with a 14-day trend and a product revenue ranking.

#### Orders

![Orders](/screenshots/admin-orders.png)

Filter orders by status, view prize details and handle refunds.

#### Products

![Products](/screenshots/admin-products.png)

Manages weapon case products, promotions and exchange rates, and controls listing status.

#### News

![News](/screenshots/admin-articles.png)

Manages news entries, news sources, announcements and home banners.

#### Users

![Users](/screenshots/admin-users.png)

Views user profiles, roles and status; an individual user's order history can be queried.

#### Funnel

![Funnel](/screenshots/admin-funnel.png)

Statistics conversion and drop-off rates across the view, add-to-cart, order and payment steps, aggregated daily.

#### Price Crawler

![Price crawler](/screenshots/admin-price-crawl.png)

Reviews third-party site price mappings and compares them with the in-site spread.

#### Settings

![Settings](/screenshots/admin-settings.png)

Maintains site parameters and the crawler site overview online; deployment-level configuration stays in server environment variables.

### Mobile App

#### Home

![Mobile home](/screenshots/mobile-home.png)

The bottom navigation covers case opening, market and orders entry points.

#### Case List

![Mobile case list](/screenshots/mobile-products.png)

Lists on-sale weapon cases with prices; tap through to details.

#### Case Detail and Ordering

![Mobile case detail](/screenshots/mobile-product-detail.png)

Lists the case contents and probabilities; a plan can be selected and ordered directly.

#### Login

![Mobile login](/screenshots/mobile-login.png)

Account and password login; unauthenticated users are guided to log in when ordering.

#### Opening Result

![Mobile opening result](/screenshots/mobile-opening.png)

The opening result shows the prize, wear and reference price immediately, and the prize is moved to the inventory automatically.

#### Orders

![Mobile orders](/screenshots/mobile-orders.png)

Orders are shown by status, with payment amount, time and channel.
