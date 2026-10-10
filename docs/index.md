---
layout: home

hero:
  name: Doggo
  text: 开源游戏资讯与社区平台
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
