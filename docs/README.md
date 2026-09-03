# 文档维护

`docs/` 同时保存普通用户手册和项目维护资料：

- `index.md`、`guide/`、`features/`、`exports/`、`support/`：VitePress 用户站点；
- `wiki/`：GitHub Wiki 的版本化内容，不参与 VitePress 构建；
- `architecture/`、`development/`：架构、兼容性和维护资料，不进入用户站点；
- `planning/`、`reviews/`：历史设计记录，不进入用户站点。

## 本地预览

需要 Node.js 和 pnpm。当前文档构建使用 Node.js 24 与 pnpm 11。

```powershell
pnpm install --frozen-lockfile
pnpm docs:dev
```

发布前执行完整构建和死链检查：

```powershell
pnpm docs:build
pnpm docs:preview
```

VitePress 输出目录是 `docs/.vitepress/dist/`，该目录是生成物，不提交到 Git。

`.github/workflows/docs.yml` 提供手动 GitHub Pages 发布。先完成本地预览与内容审核，再手动触发；
文档提交本身不会自动公开站点。

## 内容规则

- 先说明为什么使用、页面怎么操作、最终得到什么。
- 普通用户页面不介绍内部数据结构、临时目录或维护流程。
- 只有在影响用户判断时，才说明自动检查与目标应用验收的区别。
- 截图必须来自当前 UI，不包含本机绝对路径、个人信息或失败弹窗。
- 修改用户可见功能时同步 README、Wiki 首页、对应功能页和 Changelog。
