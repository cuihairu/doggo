import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
  lang: 'zh-CN',
  title: 'Doggo',
  description:
    '开源游戏资讯与社区平台 —— CS2 / Dota 2 资讯聚合、饰品行情、开箱模拟,基于 .NET 构建',
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }]],

  themeConfig: {
    logo: '/logo.svg',
    nav: [
      { text: '指南', link: '/guide/introduction', activeMatch: '/guide/' },
      { text: '设计', link: '/design/overview', activeMatch: '/design/' }
    ],
    sidebar: {
      '/guide/': [
        {
          text: '指南',
          items: [
            { text: '项目简介', link: '/guide/introduction' },
            { text: '功能总览', link: '/guide/features' },
            { text: '系统架构', link: '/guide/architecture' },
            { text: '快速开始', link: '/guide/getting-started' }
          ]
        }
      ],
      '/design/': [
        {
          text: '设计文档',
          items: [
            { text: '设计总览', link: '/design/overview' },
            { text: '原有平台功能分析', link: '/design/legacy-analysis' },
            { text: '开箱系统设计', link: '/design/case-opening' },
            { text: '数据模型', link: '/design/data-model' }
          ]
        }
      ]
    },
    socialLinks: [{ icon: 'github', link: 'https://github.com/cuihairu/doggo' }],
    outline: { level: [2, 3], label: '本页目录' },
    docFooter: { prev: '上一页', next: '下一页' },
    lastUpdated: {
      text: '最后更新',
      formatOptions: {
        dateStyle: 'short',
        timeStyle: 'medium'
      }
    },
    search: {
      provider: 'local',
      options: {
        translations: {
          button: { buttonText: '搜索文档', buttonAriaLabel: '搜索文档' },
          modal: {
            noResultsText: '未找到结果',
            resetButtonTitle: '清除查询条件',
            displayDetails: '显示详细列表',
            footer: { selectText: '选择', navigateText: '切换', closeText: '关闭' }
          }
        }
      }
    }
  }
})
