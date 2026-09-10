# GitHub Actions 启用说明

仓库中的工作流示例保存在 `docs/ci/`：

- `docs/ci/ci.yml.example`
- `docs/ci/pages.yml.example`

这是为了避免使用只有 Contents 权限的 GitHub Token 时无法写入
`.github/workflows/`。拥有 `workflow` 权限的维护者只需复制文件并去掉
`.example` 后缀：

```text
docs/ci/ci.yml.example    -> .github/workflows/ci.yml
docs/ci/pages.yml.example -> .github/workflows/pages.yml
```

启用后：

- CI 会构建 WPF 项目和检查 Python 引擎语法。
- Pages 工作流会把 `site/` 部署到 GitHub Pages。

如果不使用 Actions，也可以在仓库 Settings -> Pages 中选择
`Deploy from a branch`，分支 `main`，目录 `/ (root)`。根目录
`index.html` 会自动跳转到 `site/` 展示页。
