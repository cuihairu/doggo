# Miniapp Review Compliance Checklist

[English](/en/guide/miniapp-compliance) | [简体中文](/guide/miniapp-compliance)

This page records the compliance self-checks done before submitting the WeChat miniapp for review. It concerns `miniapp/` (Taro + React, compiled to weapp).

## Categories and qualifications

| Item | Status | Notes |
| --- | --- | --- |
| Service category | pending | The platform displays virtual goods and takes orders; common categories are "IT tech > basic telecom / software service" or "e-commerce platform"; cash payments require confirming an individual/company entity and the matching qualifications |
| Registered entity | pending | A personal-entity miniapp cannot enable WeChat Pay; the order-and-pay flow needs a company entity before review |
| AppID | `touristappid` (placeholder) | Replace with the real AppID before review, see `project.config.json` |

## Privacy and agreements

| Item | Status | Notes |
| --- | --- | --- |
| Privacy policy | must be hosted by the site | The admin console / official site must host a privacy policy page URL, filled into the review form; the miniapp user center can link to it |
| User agreement | must be hosted by the site | Same as above |
| `getUserProfile` / phone number | not used | The miniapp only exchanges `wx.login` for an openid; it does not read avatar or nickname (the nickname is typed by hand and may stay empty), does not request phone-number authorisation, and triggers no privacy-API warnings |
| Location / album and other sensitive permissions | not used | No `getLocation`, `chooseImage`, or similar calls |
| User data storage | server-side | The openid is stored server-side in `user_accounts.wechat_open_id` (unique index); locally only the JWT accessToken/refreshToken is kept as login state, plus local session and channel keys (`Taro.setStorage`) |

## Payments and virtual goods

| Item | Status | Notes |
| --- | --- | --- |
| WeChat Pay | not integrated | `payOrder` in `miniapp/src/api/endpoints.ts` calls `POST /api/orders/{id}/pay` (balance / points deduction semantics) without invoking `wx.requestPayment`; before integrating `wx.requestPayment`, the virtual-goods category and payment qualifications must be approved first |
| iOS virtual payment restrictions | no iOS-side payment invoked | If `wx.requestPayment` is integrated later, virtual goods on iOS need a compliant route or the review will be rejected |
| Case-opening / lottery gameplay | server-generated | Opening results come from server-side order fulfilment (`GET /api/users/{userId}/prizes`); the frontend runs no probability demo. If review classifies it as lottery gameplay, a probability disclosure page must be added per platform requirements |

## Content and technical self-checks

- [x] Every page is declared in `app.config.ts`; no navigation to undeclared page paths
- [x] The three tabBar items (home / products / profile) are plain text with no infringing icons
- [x] All requests go to the ICP-registered domain pointed to by `TARO_APP_API_BASE` (configure the request allowlist domain in the public platform and enable HTTPS before launch)
- [x] No incentivised sharing, no forced official-account follows, no hot code updates
- [x] The test `project.config.json` sets `urlCheck: false` for local debugging only; the review build must set it back to `true` and confirm the domain configuration
- [x] No leftover console debug output printing sensitive information (login state lives only in storage keys, never logged)

## Pre-review to-dos (external dependencies)

1. Register the real miniapp entity, obtain the AppID/AppSecret, and configure the server env vars `WeChat__AppId` / `WeChat__AppSecret`.
2. Configure the request allowlist domain in the public platform (pointing at the live API domain, HTTPS).
3. Host the privacy policy and user agreement on the site and get their URLs.
4. Decide the service category; if the review environment must demo a complete order, integrate WeChat Pay first or provide a demo explanation as the review requires.
5. Open `dist/` in WeChat DevTools with the real AppID and finish device preview plus a staging regression.

Items 1-4 above depend on external accounts and entity qualifications; they cannot be done from inside the repository.
