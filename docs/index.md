---
layout: home

hero:
  name: Doggo
  text: 开源游戏资讯平台
  tagline: CS2 / Dota 2 资讯聚合、饰品行情、开箱模拟,基于 C# / .NET 构建,支持 Linux 与 Docker 部署
  image:
    src: /logo.svg
    alt: Doggo
  actions:
    - theme: brand
      text: 快速开始
      link: /guide/getting-started
    - theme: alt
      text: 功能总览
      link: /guide/features
    - theme: alt
      text: GitHub
      link: https://github.com/cuihairu/doggo

features:
  - icon: 📰
    title: 资讯聚合
    details: 抓取 CS2 与 Dota 2 的官方公告、更新日志、赛事动态,经过清洗与去重后统一展示。
  - icon: 📈
    title: 饰品行情
    details: 同步武器箱与饰品资料,跟踪 Steam 市场价格,提供行情查询与趋势展示。
  - icon: 🎁
    title: 开箱模拟
    details: 按武器箱掉落概率与磨损区间实现的开箱玩法,包含库存管理与开箱记录。
  - icon: 🤖
    title: Steam 机器人
    details: Steam 侧的行情同步、库存与交易能力基于 vapor 实现,Doggo 通过其 API 集成。
  - icon: 🛠️
    title: 管理后台
    details: 用户、饰品、箱子、资讯、订单与统计数据的运营管理界面。
  - icon: ⚙️
    title: 统一技术栈
    details: 服务端、爬虫、机器人统一使用 C# / .NET,原生支持 Linux。
---

## 界面预览

以下截图取自本地运行的真实界面(种子数据),按功能逐项说明。

### 玩家端 Web

#### 首页

![玩家端首页](/screenshots/web-home.png)

聚合最新资讯、行情入口与商店推荐,顶部导航覆盖资讯、行情、商店与 Roll 房。

#### 资讯详情

![资讯详情](/screenshots/web-news-detail.png)

文章正文在抓取后经过清洗与去重,可查看来源与发布时间。

#### 饰品行情列表

![饰品行情列表](/screenshots/web-market-list.png)

按游戏与类型筛选饰品,展示最新价与更新时间。

#### 价格趋势

![价格趋势](/screenshots/web-market-trend.png)

单个饰品提供日线走势图与开、高、低、收历史价格表。

#### 商店列表

![商店列表](/screenshots/web-product-list.png)

在售武器箱按上架顺序排列,展示价格与分类。

#### 箱子详情

![箱子详情](/screenshots/web-product-detail.png)

展示箱子内物品、掉落概率、磨损区间与可选购买方案。

#### 订单支付

![订单支付](/screenshots/web-order-pay.png)

下单后生成支付倒计时,按步骤完成渠道选择与支付确认。

#### 团队开箱

![团队开箱](/screenshots/web-case-opening.png)

多人拼团开箱按座位分配奖品,领奖后自动存入库存。

#### 我的奖品

![我的奖品](/screenshots/web-my-prizes.png)

开箱所得奖品统一入库存,可查看磨损与参考价。

#### 登录

![登录页](/screenshots/web-login.png)

手机号加密码登录,登录后可使用开箱、订单与库存功能。

### 管理后台

#### 后台登录

![后台登录](/screenshots/admin-login.png)

管理员账号登录入口,非管理员访问自动跳回登录页。

#### 数据面板

![数据面板](/screenshots/admin-dashboard.png)

汇总用户、订单与营收数据,提供近 14 天走势与商品营收排行。

#### 订单管理

![订单管理](/screenshots/admin-orders.png)

按状态筛选订单,可查看奖品明细并处理退款。

#### 商品管理

![商品管理](/screenshots/admin-products.png)

维护武器箱商品、促销与汇率,控制上架状态。

#### 资讯管理

![资讯管理](/screenshots/admin-articles.png)

管理资讯条目、资讯源、公告与首页 Banner。

#### 用户管理

![用户管理](/screenshots/admin-users.png)

查看用户资料、角色与状态,可查询单个用户的订单流水。

#### 转化漏斗

![转化漏斗](/screenshots/admin-funnel.png)

按浏览、加购、下单、支付四步统计转化率与流失率,按日聚合。

#### 价格爬虫

![价格爬虫](/screenshots/admin-price-crawl.png)

审阅第三方站点的价格映射,并与站内价差对照。

#### 系统设置

![系统设置](/screenshots/admin-settings.png)

在线维护站点参数与爬虫站点总览,部署级配置仍在服务端环境变量。

### 移动端 App

#### 首页

![移动端首页](/screenshots/mobile-home.png)

底部导航覆盖商品开箱、行情与订单入口。

#### 商品开箱列表

![移动端商品列表](/screenshots/mobile-products.png)

展示在售武器箱与价格,点击进入详情。

#### 开箱详情与下单

![移动端开箱详情](/screenshots/mobile-product-detail.png)

列出箱内物品与概率,可选套餐直接下单。

#### 登录

![移动端登录](/screenshots/mobile-login.png)

账号密码登录,未登录用户下单时引导登录。

#### 开箱结果

![移动端开箱结果](/screenshots/mobile-opening.png)

开箱即时展示奖品、磨损与参考价,奖品自动存入库存。

#### 订单

![移动端订单](/screenshots/mobile-orders.png)

订单按状态展示,支付金额、时间与渠道可查。

