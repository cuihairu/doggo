# 小程序提审合规自查


本文记录微信小程序提审前的合规自查项。对应 `miniapp/`(Taro + React,编译目标 weapp)。

## 类目与资质

| 项 | 现状 | 说明 |
| --- | --- | --- |
| 服务类目 | 待定 | 平台含虚拟商品展示与订单,常见类目为「IT 科技 > 基础电信/软件服务」或「电商平台」;含现金支付需确认个体/企业主体与相应资质 |
| 主体 | 待定 | 个人主体小程序不能开通微信支付;下单支付流程提审前需企业主体 |
| AppID | `touristappid`(占位) | 提审前替换为正式 AppID,见 `project.config.json` |

## 隐私与协议

| 项 | 现状 | 说明 |
| --- | --- | --- |
| 隐私政策 | 需站点提供 | 后台/官网需托管《隐私政策》页面 URL,提审表单中填写;小程序内用户中心可加入口 |
| 用户协议 | 需站点提供 | 同上 |
| `getUserProfile` / 手机号 | 未使用 | 小程序仅用 `wx.login` 换 openid,不取头像昵称(昵称为用户手填,可空),不申请手机号授权,不触发隐私接口告警 |
| 位置/相册等敏感权限 | 未使用 | 无 `getLocation`、`chooseImage` 等调用 |
| 用户数据存储 | 服务端 | openid 存服务端 `user_accounts.wechat_open_id`(唯一索引),本地登录态仅存 JWT accessToken/refreshToken,另存本地会话与渠道键(`Taro.setStorage`) |

## 支付与虚拟物品

| 项 | 现状 | 说明 |
| --- | --- | --- |
| 微信支付 | 未接入 | `miniapp/src/api/endpoints.ts` 的 `payOrder` 调 `POST /api/orders/{id}/pay`(余额/积分扣减语义),未拉起 `wx.requestPayment`;接入 `wx.requestPayment` 前,虚拟商品类目与支付资质必须先过 |
| iOS 虚拟支付限制 | 不涉及 iOS 端拉起支付 | 若后续接入 `wx.requestPayment`,iOS 端虚拟商品需走合规方案,否则提审会被拒 |
| 开箱/抽奖玩法 | 服务端生成 | 开箱结果由服务端订单履约产生(`GET /api/users/{userId}/prizes`),前端不做概率演示;如审核认定为抽奖类玩法,需按平台要求补充概率公示页 |

## 内容与技术自查

- [x] 页面均在 `app.config.ts` 声明,无未声明页面路径跳转
- [x] tabBar 三项(首页/商品/我的)为纯文本,无侵权图标
- [x] 所有请求走 `TARO_APP_API_BASE` 指向的备案域名(上线前在公众平台配置 request 合法域名,并开启 HTTPS)
- [x] 无诱导分享、无强制关注公众号、无热更新代码
- [x] 测试环境 `project.config.json` 设置 `urlCheck: false` 仅供本地调试;提审包需改回 `true` 并确认域名配置
- [x] 无残留 console 调试输出导致的敏感信息打印(登录态只存 storage key,不打印)

## 提审前待办(外部依赖)

1. 注册正式小程序主体,取得 AppID/AppSecret,配置到服务端环境变量 `WeChat__AppId` / `WeChat__AppSecret`。
2. 公众平台配置 request 合法域名(指向线上 API 域名,HTTPS)。
3. 站点托管《隐私政策》《用户协议》并取得 URL。
4. 确定服务类目;如需在审核环境演示完整下单,先接入微信支付或按审核要求提供演示说明。
5. 用微信开发者工具以正式 AppID 打开 `dist/`,完成真机预览与体验版回归。

以上 1–4 依赖外部账号与主体资质,仓库内无法代为完成。
