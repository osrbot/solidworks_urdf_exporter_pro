import { defineConfig, type DefaultTheme } from 'vitepress'

const repository = 'https://github.com/osrbot/solidworks_urdf_exporter_pro'

const zhTheme: DefaultTheme.Config = {
  siteTitle: 'SW2URDF 文档',
  nav: [
    { text: '为什么使用', link: '/guide/why-use' },
    { text: '功能页面', link: '/features/link-tree' },
    { text: '导出目标', link: '/exports/' },
    { text: '问题与贡献', link: '/support/help-and-contribute' },
    { text: 'GitHub', link: repository }
  ],
  sidebar: [
    {
      text: '开始',
      items: [
        { text: '文档首页', link: '/' },
        { text: '为什么使用', link: '/guide/why-use' },
        { text: '安装', link: '/guide/installation' },
        { text: '快速开始', link: '/guide/getting-started' },
        { text: '相比社区原版', link: '/guide/whats-new' }
      ]
    },
    {
      text: '功能页面',
      items: [
        { text: 'Link 树', link: '/features/link-tree' },
        { text: 'Joint 属性', link: '/features/joint' },
        { text: '惯性', link: '/features/inertia' },
        { text: '可视与碰撞', link: '/features/collision' },
        { text: '外观', link: '/features/appearance' },
        { text: '模型与导出', link: '/features/export-page' }
      ]
    },
    {
      text: '导出目标',
      items: [
        { text: '先选对输出', link: '/exports/' },
        { text: 'ROS 1 / ROS 2', link: '/exports/ros' },
        { text: 'OpenUSD', link: '/exports/openusd' },
        { text: 'MuJoCo MJCF', link: '/exports/mujoco' }
      ]
    },
    {
      text: '帮助',
      items: [
        { text: '常见问题', link: '/support/troubleshooting' },
        { text: '提问与贡献代码', link: '/support/help-and-contribute' }
      ]
    }
  ],
  outline: { level: [2, 3], label: '本页内容' },
  docFooter: { prev: '上一页', next: '下一页' },
  lastUpdated: { text: '最后更新' },
  langMenuLabel: '切换语言',
  returnToTopLabel: '返回顶部',
  sidebarMenuLabel: '菜单',
  darkModeSwitchLabel: '外观',
  lightModeSwitchTitle: '切换到浅色主题',
  darkModeSwitchTitle: '切换到深色主题',
  skipToContentLabel: '跳转到正文',
  search: {
    provider: 'local',
    options: {
      translations: {
        button: { buttonText: '搜索文档', buttonAriaLabel: '搜索文档' },
        modal: {
          noResultsText: '没有找到相关内容',
          resetButtonTitle: '清除查询',
          footer: { selectText: '选择', navigateText: '切换', closeText: '关闭' }
        }
      }
    }
  },
  editLink: {
    pattern: `${repository}/edit/master/docs/:path`,
    text: '在 GitHub 上修改本页'
  },
  footer: {
    message: 'SW2URDF 是社区维护项目，不是 Dassault Systemes 或 ROS 官方发行版。',
    copyright: 'Released under the MIT License.'
  }
}

const enTheme: DefaultTheme.Config = {
  siteTitle: 'SW2URDF Docs',
  nav: [
    { text: 'Why', link: '/en/guide/why-use' },
    { text: 'Features', link: '/en/features/link-tree' },
    { text: 'Exports', link: '/en/exports/' },
    { text: 'Help', link: '/en/support/help-and-contribute' },
    { text: 'GitHub', link: repository }
  ],
  sidebar: [
    {
      text: 'Start',
      items: [
        { text: 'Documentation Home', link: '/en/' },
        { text: 'Why SW2URDF', link: '/en/guide/why-use' },
        { text: 'Installation', link: '/en/guide/installation' },
        { text: 'Quick Start', link: '/en/guide/getting-started' },
        { text: 'Compared with Upstream', link: '/en/guide/whats-new' }
      ]
    },
    {
      text: 'Feature Pages',
      items: [
        { text: 'Link Tree', link: '/en/features/link-tree' },
        { text: 'Joint Properties', link: '/en/features/joint' },
        { text: 'Inertia', link: '/en/features/inertia' },
        { text: 'Visual and Collision', link: '/en/features/collision' },
        { text: 'Appearance', link: '/en/features/appearance' },
        { text: 'Model and Export', link: '/en/features/export-page' }
      ]
    },
    {
      text: 'Export Targets',
      items: [
        { text: 'Choose an Output', link: '/en/exports/' },
        { text: 'ROS 1 / ROS 2', link: '/en/exports/ros' },
        { text: 'OpenUSD', link: '/en/exports/openusd' },
        { text: 'MuJoCo MJCF', link: '/en/exports/mujoco' }
      ]
    },
    {
      text: 'Help',
      items: [
        { text: 'Troubleshooting', link: '/en/support/troubleshooting' },
        { text: 'Questions and Contributions', link: '/en/support/help-and-contribute' }
      ]
    }
  ],
  outline: { level: [2, 3], label: 'On this page' },
  docFooter: { prev: 'Previous page', next: 'Next page' },
  lastUpdated: { text: 'Last updated' },
  search: {
    provider: 'local',
    options: {
      translations: {
        button: { buttonText: 'Search docs', buttonAriaLabel: 'Search documentation' },
        modal: {
          noResultsText: 'No results found',
          resetButtonTitle: 'Clear search',
          footer: { selectText: 'Select', navigateText: 'Navigate', closeText: 'Close' }
        }
      }
    }
  },
  editLink: {
    pattern: `${repository}/edit/master/docs/:path`,
    text: 'Edit this page on GitHub'
  },
  footer: {
    message: 'SW2URDF is community maintained and is not an official Dassault Systemes or ROS release.',
    copyright: 'Released under the MIT License.'
  }
}

export default defineConfig({
  base: process.env.DOCS_BASE || '/',
  cleanUrls: true,
  lastUpdated: true,
  srcExclude: [
    'README.md',
    'architecture/**',
    'development/**',
    'isaac/**',
    'planning/**',
    'reviews/**',
    'wiki/**'
  ],
  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      title: 'SW2URDF 文档',
      description: '从 SolidWorks 装配体导出 ROS、OpenUSD 和 MuJoCo 机器人资产',
      themeConfig: zhTheme,
      markdown: {
        container: {
          tipLabel: '提示',
          warningLabel: '注意',
          dangerLabel: '警告',
          infoLabel: '信息',
          detailsLabel: '详情'
        },
        codeCopyButton: {
          tooltipText: '复制代码',
          copiedText: '已复制'
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      link: '/en/',
      title: 'SW2URDF Documentation',
      description: 'Export ROS, OpenUSD, and MuJoCo robot assets from SolidWorks assemblies',
      themeConfig: enTheme
    }
  },
  themeConfig: {
    i18nRouting: true,
    socialLinks: [{ icon: 'github', link: repository }]
  }
})
