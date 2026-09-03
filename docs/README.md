# 文档维护

`docs/` 同时保存在线手册和版本化 Wiki 内容：

- `index.md`、`guide/`、`exports/`、`reference/`：面向用户的简明入口；
- `wiki/`：GitHub Wiki 的版本化事实源，也直接参与在线站点构建；
- `architecture/`、`development/`：架构、兼容性和维护资料；
- `planning/`、`reviews/`：历史设计记录，不参与站点构建。

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

- 先说明用户能得到什么文件，再解释内部实现。
- 把生成、自动检查和目标应用验收分开描述。
- 截图必须来自当前 UI，不包含本机绝对路径、个人信息或失败弹窗。
- 修改产品边界时同步 README、Wiki 首页、目标专项页、兼容性矩阵和 Changelog。
