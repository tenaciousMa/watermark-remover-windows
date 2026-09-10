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
