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
