import { defineConfig } from 'vitepress'

const repository = 'https://github.com/osrbot/solidworks_urdf_exporter_pro'

export default defineConfig({
  lang: 'zh-CN',
  title: 'SW2URDF 文档',
  description: '从 SolidWorks 装配体导出 ROS、OpenUSD 和 MuJoCo 机器人资产',
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
  themeConfig: {
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
    socialLinks: [{ icon: 'github', link: repository }],
    editLink: {
      pattern: `${repository}/edit/master/docs/:path`,
      text: '在 GitHub 上修改本页'
    },
    footer: {
      message: 'SW2URDF 是社区维护项目，不是 Dassault Systemes 或 ROS 官方发行版。',
      copyright: 'Released under the MIT License.'
    }
  }
})
