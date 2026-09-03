import { defineConfig } from 'vitepress'

const repository = 'https://github.com/osrbot/solidworks_urdf_exporter_pro'

export default defineConfig({
  lang: 'zh-CN',
  title: 'SW2URDF 文档',
  description: '从 SolidWorks 装配体导出 ROS、OpenUSD 和 MuJoCo 机器人资产',
  base: process.env.DOCS_BASE || '/',
  cleanUrls: true,
  lastUpdated: true,
  srcExclude: ['planning/**', 'reviews/**'],
  themeConfig: {
    siteTitle: 'SW2URDF 文档',
    nav: [
      { text: '开始使用', link: '/guide/getting-started' },
      { text: '模型配置', link: '/guide/model-setup' },
      { text: '导出目标', link: '/exports/' },
      { text: '版本与验证', link: '/reference/versions' },
      { text: 'GitHub', link: repository }
    ],
    sidebar: [
      {
        text: '开始',
        items: [
          { text: '文档首页', link: '/' },
          { text: '快速开始', link: '/guide/getting-started' },
          { text: '本次主要变化', link: '/guide/whats-new' }
        ]
      },
      {
        text: 'SolidWorks 配置',
        items: [
          { text: 'Link、Joint 与几何', link: '/guide/model-setup' },
          { text: 'Link Tree 细节', link: '/wiki/Link-Tree' },
          { text: '惯性', link: '/wiki/Inertia' },
          { text: '碰撞', link: '/wiki/Collision-zh-CN' }
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
        text: '参考',
        items: [
          { text: '版本怎么理解', link: '/reference/versions' },
          { text: '验证范围', link: '/reference/validation' },
          { text: '常见问题', link: '/wiki/Troubleshooting-zh-CN' },
          { text: '兼容性矩阵', link: '/development/compatibility-matrix' }
        ]
      },
      {
        text: '维护',
        collapsed: true,
        items: [
          { text: '参与开发', link: '/wiki/Contributing-zh-CN' },
          { text: '发布流程', link: '/wiki/Release-Process-zh-CN' },
          { text: '内部 Robot Bundle', link: '/architecture/robot-bundle-v2' }
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
