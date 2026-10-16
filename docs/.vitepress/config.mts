import { defineConfig } from 'vitepress'

// Served from a project page: https://cuihairu.github.io/doggo/
const base = '/doggo/'

// i18n shape: root = Chinese, /en/ = English (docs/en/**).
// Both locales declare a label, which is what makes the default theme's nav
// language switcher (VPNavBarTranslations) appear; it maps the current page to
// the same path under the other locale, so paths must mirror each other.
// Language switching is site-level only: the home pages are frontmatter-driven,
// so no switcher line is embedded in them.
export default defineConfig({
  lang: 'zh-CN',
  title: 'Doggo',
  base,
  description:
    '开源游戏资讯平台 —— CS2 / Dota 2 资讯聚合、饰品行情、开箱模拟,基于 .NET 构建',
  head: [['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }]],

  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      description:
        '开源游戏资讯平台 —— CS2 / Dota 2 资讯聚合、饰品行情、开箱模拟,基于 .NET 构建',
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
                { text: '快速开始', link: '/guide/getting-started' },
                { text: '部署', link: '/guide/deployment' },
                { text: '小程序提审合规自查', link: '/guide/miniapp-compliance' }
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
                { text: '数据模型', link: '/design/data-model' },
                { text: '价格爬虫模块设计', link: '/design/price-crawler-design' },
                { text: '漏斗数据分析设计', link: '/design/analytics-funnel-design' },
                { text: 'UI 体系', link: '/design/ui-system' },
                { text: '四端原型', link: '/design/prototypes/README' }
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
    },
    en: {
      label: 'English',
      lang: 'en-US',
      title: 'Doggo',
      description:
        'Open-source gaming news platform -- CS2 / Dota 2 news aggregation, item market data and case opening, built on .NET',
      themeConfig: {
        logo: '/logo.svg',
        nav: [
          { text: 'Guide', link: '/en/guide/introduction', activeMatch: '/en/guide/' },
          { text: 'Design', link: '/en/design/overview', activeMatch: '/en/design/' }
        ],
        sidebar: {
          '/en/guide/': [
            {
              text: 'Guide',
              items: [
                { text: 'Introduction', link: '/en/guide/introduction' },
                { text: 'Features', link: '/en/guide/features' },
                { text: 'System Architecture', link: '/en/guide/architecture' },
                { text: 'Getting Started', link: '/en/guide/getting-started' },
                { text: 'Deployment', link: '/en/guide/deployment' },
                { text: 'Miniapp Compliance', link: '/en/guide/miniapp-compliance' }
              ]
            }
          ],
          '/en/design/': [
            {
              text: 'Design docs',
              items: [
                { text: 'Design Overview', link: '/en/design/overview' },
                { text: 'Legacy Platform Analysis', link: '/en/design/legacy-analysis' },
                { text: 'Case-Opening System', link: '/en/design/case-opening' },
                { text: 'Data Model', link: '/en/design/data-model' },
                { text: 'Price Crawler', link: '/en/design/price-crawler-design' },
                { text: 'Funnel Analytics', link: '/en/design/analytics-funnel-design' },
                { text: 'UI System', link: '/en/design/ui-system' },
                { text: 'Four-Client Prototypes', link: '/en/design/prototypes/README' }
              ]
            }
          ]
        },
        socialLinks: [{ icon: 'github', link: 'https://github.com/cuihairu/doggo' }],
        outline: { level: [2, 3], label: 'On this page' },
        docFooter: { prev: 'Previous', next: 'Next' },
        lastUpdated: {
          text: 'Last updated',
          formatOptions: {
            dateStyle: 'short',
            timeStyle: 'medium'
          }
        },
        search: { provider: 'local' }
      }
    }
  }
})
