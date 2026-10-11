export default defineAppConfig({
  pages: [
    'pages/index/index',
    'pages/newsdetail/newsdetail',
    'pages/market/market',
    'pages/products/products',
    'pages/product/product',
    'pages/opening/opening',
    'pages/user/user',
    'pages/login/login'
  ],
  tabBar: {
    color: '#999999',
    selectedColor: '#2f66d0',
    backgroundColor: '#ffffff',
    borderStyle: 'black',
    list: [
      { pagePath: 'pages/index/index', text: '首页' },
      { pagePath: 'pages/products/products', text: '商品' },
      { pagePath: 'pages/user/user', text: '我的' }
    ]
  },
  window: {
    backgroundTextStyle: 'light',
    navigationBarBackgroundColor: '#2f66d0',
    navigationBarTitleText: 'Doggo',
    navigationBarTextStyle: 'white'
  }
})
