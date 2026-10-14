import { defineConfig } from '@umijs/max'

const apiTarget = process.env.API_ORIGIN ?? 'http://localhost:5077'

export default defineConfig({
  antd: {},
  layout: {
    title: 'Doggo 管理后台',
    locale: false,
  },
  request: {},
  access: {},
  model: {},
  routes: [
    { path: '/login', layout: false, component: './Login' },
    {
      path: '/',
      redirect: '/dashboard',
    },
    { path: '/dashboard', component: './Dashboard', icon: 'DashboardOutlined', name: '数据面板' },
    {
      path: '/analytics',
      icon: 'FallOutlined',
      name: '数据分析',
      routes: [
        { path: '/analytics/funnel', component: './Funnel', name: '转化漏斗' },
        { path: '/analytics/price-crawl', component: './PriceCrawl', name: '价格爬虫' },
      ],
    },
    { path: '/articles', component: './Articles', icon: 'FileTextOutlined', name: '资讯管理' },
    { path: '/products', component: './Products', icon: 'AppstoreOutlined', name: '商品管理' },
    { path: '/orders', component: './Orders', icon: 'ShoppingCartOutlined', name: '订单管理' },
    { path: '/users', component: './Users', icon: 'TeamOutlined', name: '用户管理' },
    { path: '/rolls', component: './Rolls', icon: 'GiftOutlined', name: 'Roll 房管理' },
    { path: '/settings', component: './Settings', icon: 'SettingOutlined', name: '系统设置' },
  ],
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
  },
  npmClient: 'pnpm',
})
