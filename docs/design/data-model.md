# 数据模型

[English](/en/design/data-model) | [简体中文](/design/data-model)

开源版目标数据模型,按域分组。字段从原平台约 70 张表中提炼,结合 .NET 技术栈做了整理;机器人账号凭据等敏感数据**不落 Doggo 库**,由 vapor 管理。

## 用户域

| 实体 | 要点 |
| --- | --- |
| UserAccount | 手机/邮箱注册、昵称、头像、邀请码、状态;密码哈希 |
| UserSteamProfile | 用户 Steam 绑定:SteamId、昵称、头像 |
| UserTradeUrl | 用户交易链接(多条、可设默认、校验状态与错误) |
| UserCoin / UserCoinLog | 积分余额与流水;FIFO 消费、按批次过期 |
| UserCoupon / Coupon | 优惠券:类型(满减/折扣)、门槛、有效期、可用商品范围 |
| RedeemCode | 积分/优惠券兑换码,批量生成与核销 |
| UserLoginLog / UserVisit | 登录记录(IP/UA/结果)、访问统计(日 UV/PV) |
| InviteRewardLog | 邀请奖励流水 |
| SignRecord | 周期签到(自然周)与补签 |

## 商品域

| 实体 | 要点 |
| --- | --- |
| Product | 商品:类型(箱子/钥匙/组合)、classId、采购价、参考价、价格修正、最终价、上架状态 |
| CasePlan / CasePlanOption | 套餐与选项:起批数量、加价率(定价用)、可否用券/积分、返现倍率、数量档位 |
| PromotionPlan | 限量促销:折扣价、库存、每人限购、每日重置 |
| QuantityOption | 数量档位与库存告警阈值 |
| CaseContent | 箱子内容物展示:标题、品质(七档)、颜色、图片、参考价 |
| Recommend | 推荐位 |
| ExchangeRate | 多币种汇率(展示用) |

## 订单域

| 实体 | 要点 |
| --- | --- |
| UserOrder | 入口、商品类型、套餐、价格四件套(原价/售价/总价/成本)、积分抵扣、券抵扣、返现、支付渠道与回调、交易链接引用、状态、serviceMode(自开/代开) |
| PaymentRecord | 支付流水:渠道、金额、回调载荷、幂等键 |
| RefundRecord | 退款记录 |

## 开箱域

| 实体 | 要点 |
| --- | --- |
| GroupBuying | 批次:商品、批次号、座位数、已售、状态、乐观锁版本 |
| GroupBuyingSeat | 座位:序号、箱子 assetId、钥匙 assetId、奖品 assetId、状态(售卖中→…→完成) |
| UserPrize | 奖品:assetId/classId/instanceId、市场名、品质、外观、floatValue、稀有度、图标、检查链接、可交易时间、估价、订单关联、来源 |
| UserPrizeLog | 开箱日志(开箱时间、账号引用、结果) |
| RollRoom(+Config/Participants) | roll 房:模板、规则、状态、奖品配置(饰品/积分 + 权重)、参与者、开奖与领奖记录 |

## 资讯域(新增)

| 实体 | 要点 |
| --- | --- |
| NewsSource | 资讯源:名称、类型(官方/社区)、抓取配置、启停 |
| NewsItem | 资讯条目:标题、摘要、正文、原文链接、来源、游戏(CS2/Dota2)、分类、标签、发布时间、指纹去重键 |
| Article | 运营文章:富文本、Banner 图、分类、关键词、阅读数、上下架 |
| Notice / Banner / Partner | 公告、广告位、合作伙伴 |

## 行情域

| 实体 | 要点 |
| --- | --- |
| MarketItem | 饰品/箱子资料:classId、市场名、图标、类型 |
| MarketPriceSnapshot | 价格快照:vapor 同步的时间点价格,按日聚合供趋势 |
| SyncCursor | 同步游标(按数据源) |

## 系统域

| 实体 | 要点 |
| --- | --- |
| AdminAccount / Role / Permission | RBAC:后台账号、角色、权限、角色-权限关联 |
| AdminLoginLog / IpWhitelist | 后台登录日志、IP 白名单 |
| SiteSetting | 站点配置 key-value(功能开关) |
| SupportQrcode | 客服二维码(时段生效) |
| NotificationChannel / Event | 通知渠道配置与事件记录 |
| ShortUrl | 短链与点击计数 |

## 通用约定

- 主键:应用侧生成(如 ULID/雪花),避免自增 ID 外泄规模。
- 金额:`long`,最小货币单位(分);币种字段随行。
- 软删除:删除时间戳;查询默认过滤。
- 时间:UTC 存储,展示层转时区。
- 外部资产引用(Steam assetId 等)保存原始值 + 来源游标,便于对账。
