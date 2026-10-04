# 自己提交代码、创建分支、推送和合并 PR

适用于 HOK BetaStudio，使用 Windows PowerShell、Git 和 GitHub CLI（`gh`）。命令应在项目根目录执行，每一步成功后再继续；不要整篇一次性粘贴执行。

推荐顺序是：**更新主分支 → 创建修补分支 → 修改文件 → add → commit → push → 创建 PR → 检查 → 合并 → 更新本地 main**。不是必须先 commit 才能建分支；先建分支更不容易把修改直接提交到 main。

## 1. 先理解：推送的是整个文件夹吗？

不是将磁盘上的整个项目目录重新打包上传。

| 操作 | 实际作用 |
| --- | --- |
| 修改文件 | 只改变本地工作区 |
| `git add` | 选择哪些变更进入下一次提交，包括明确选择的删除 |
| `git commit` | 在本地生成提交；逻辑上记录已跟踪项目的一个快照 |
| `git push` | 把该分支需要、而远端缺少的提交及对象发到远端；不是上传所有本地文件 |
| PR | 请求将修补分支的变化合并到 main |
| Merge PR | 将变化并入远端 main |
| Release | 给某个提交标记版本，并另外上传 ZIP、Setup 等发行附件 |

只修改 README 时，其他源码不会被删除，也不需要重新上传依赖包。`push` 会包含该分支尚未推送的全部相关提交，并不只包含最后一次 commit；如果之前提交过大文件或隐私文件，它们可能仍在历史里。

1.3 的 [PR #1](https://github.com/Alanshown/HOK-BetaStudio/pull/1) 包含 **32 个文件变更**，包括源码、索引、文档、校验文件和工作流删除。没有上传依赖缓存、构建产物、DB、提取素材或演示页代码。由于当时终端没有 Git 登录凭据，实际远端提交使用了已连接的 GitHub 插件；本地与远端提交 ID 可以不同，但已核对完整代码树一致。下面的流程让你直接通过自己的终端完成，不依赖该插件。

更多原理见 [Git push 官方说明](https://git-scm.com/docs/git-push)。

## 2. 第一次使用：准备工具和登录

先安装 [Git](https://git-scm.com/downloads) 和 [GitHub CLI](https://cli.github.com/)，然后重新打开 PowerShell：

```powershell
git --version
gh --version
git rev-parse --show-toplevel
git remote -v
```

本项目的 `origin` 应为 `https://github.com/Alanshown/HOK-BetaStudio.git`。如果不是，先确认你是否进入了正确的仓库，不要直接覆盖远程配置。

```powershell
gh auth login --hostname github.com --git-protocol https --web
gh auth status --hostname github.com
gh auth setup-git --hostname github.com
```

按终端提示，在浏览器完成你自己的 GitHub 登录。最后一条为 Git 配置 GitHub CLI 凭据助手；**Codex 插件已连接不等于终端 Git 已登录**。不要把令牌写进 remote URL、源码或聊天记录。参见 [登录](https://cli.github.com/manual/gh_auth_login)和[凭据助手](https://cli.github.com/manual/gh_auth_setup-git)。

检查本仓库的提交署名：

```powershell
git config user.name
git config user.email
```

如果缺失或不正确，再设置。邮箱建议使用 GitHub Settings → Emails 页面提供的 noreply 地址，不要随意填不存在的邮箱：

```powershell
git config user.name 'Alanshown'
$commitEmail = Read-Host '输入你的 GitHub 提交邮箱或 noreply 地址'
git config user.email $commitEmail
```

这里未使用 `--global`，只修改当前仓库的署名配置。

## 3. 每次开始：检查工作区，再建分支

```powershell
git status --short
git branch --show-current
```

`M` 是修改，`A` 是新增，`D` 是删除，`??` 是未跟踪。项目可能还有本地设计稿和素材：不打算上传就保留在本地，不要为了让输出“干净”而删除它们。若有未提交的工作修改，先保存到合适分支，不要盲目切换、拉取或重置。

以下命令适用于没有待处理工作修改、准备开始新任务的情况：

```powershell
git switch main
git pull --ff-only origin main
$branch = 'docs/release-links-guide'
git switch -c $branch
```

每次新任务换一个新分支名，例如 `fix/audio-preview`、`feat/catalog-update`。如果名称已存在，先查看它的用途，不要强制覆盖。`--ff-only` 在主线分叉时会停止，而不是偷偷生成一次合并。建分支行为见 [git switch](https://git-scm.com/docs/git-switch)。

**已经改完但还没 commit？** 在当前工作区直接 `git switch -c 新分支名` 通常会保留修改；随后按下一节选择文件。此时不要先强行切回 main 再拉取。

## 4. 选择文件并 commit

先完成修改，然后审查差异：

```powershell
git diff --stat
git diff
git status --short
```

新文件的内容不会出现在普通 `git diff` 中，需另外打开检查。下面以本次 README 和说明文档修正为例；以后按实际变更调整文件列表，不是每次都提交这些文件：

```powershell
git add -- README.md README.zh.md README.vi.md docs/GIT-PR-GUIDE.zh.md docs/INSTALL.md tooling/check-public-docs.cjs
git diff --cached --name-status
git diff --cached --stat
git diff --cached
git diff --cached --check
```

`--cached` 显示这次 commit 真正包含的内容。确认没有 `.db`、密钥、日志、私人路径、依赖、打包文件或演示页。不要习惯性使用 `git add .`，尤其本项目有较多仅供本地使用的文件。

若误暂存某个文件，用 `git restore --staged -- 文件路径` 取消暂存；这不会删除工作区里的修改。`.gitignore` 不能自动移除已经跟踪的文件，也不能清除历史提交里的敏感内容。

按修改范围运行检查：

```powershell
node tooling/check-public-docs.cjs
node tooling/validate-publication.cjs
```

如果改了程序源码，再运行：

```powershell
npm run build --prefix frontend
dotnet build backend/Hok.Desktop/Hok.Desktop.csproj -c Release
dotnet build backend/Hok.Worker/Hok.Worker.csproj -c Release
dotnet run --project backend/Hok.Contracts.Tests -c Release -- .
dotnet run --project backend/Hok.Catalog.Tests -c Release -- .
```

需要先按 README 安装依赖和 .NET SDK。项目的自动 Source checks 工作流已移除，以上检查仍可本地运行；没有远程检查不代表代码经过自动验证。

```powershell
git commit -m 'docs: update release links and add Git workflow guide'
git log -1 --oneline
git status --short
```

如果显示 `nothing to commit`，检查是否选错文件、忘记保存或已经提交。不要为了完成命令而创建无意义的提交。

## 5. 推送修补分支

```powershell
$branch = git branch --show-current
git push -u origin $branch
```

确认 `$branch` 不是 `main`。第一次的 `-u` 建立本地分支与远端分支的关联；之后在该分支可直接 `git push`。推送分支成功后，main 尚未改变。

## 6. 创建 PR

```powershell
gh pr create --repo Alanshown/HOK-BetaStudio --base main --head $branch --title 'docs: correct release links and add Git guide' --body 'Update the three README variants, installation links and the PowerShell Git/PR guide. Local documentation checks passed.'
$prNumber = gh pr view $branch --repo Alanshown/HOK-BetaStudio --json number --jq '.number'
gh pr view $prNumber --repo Alanshown/HOK-BetaStudio --web
```

标题、说明应写本次实际内容和真实测试结果。已有该分支的 PR 就直接继续用，不必重复创建。参数说明见 [gh pr create](https://cli.github.com/manual/gh_pr_create)。

PR 尚未合并时，如果还要修改：在同一个修补分支继续修改、`git add`、`git commit`、`git push`，PR 会更新。

## 7. 检查并合并 PR

```powershell
gh pr diff $prNumber --repo Alanshown/HOK-BetaStudio --name-only
gh pr diff $prNumber --repo Alanshown/HOK-BetaStudio
gh pr view $prNumber --repo Alanshown/HOK-BetaStudio --json state,mergeable,reviewDecision,statusCheckRollup
$headSha = gh pr view $prNumber --repo Alanshown/HOK-BetaStudio --json headRefOid --jq '.headRefOid'
```

确认变更文件、冲突、审查意见和所需检查。若 GitHub 尚在计算可合并状态，稍后再查；有冲突或检查失败，先解决，不使用 `--admin` 绕过要求。确认无误后：

```powershell
gh pr merge $prNumber --repo Alanshown/HOK-BetaStudio --merge --match-head-commit $headSha
gh pr view $prNumber --repo Alanshown/HOK-BetaStudio --json state,mergedAt,url
```

`--merge` 保留分支提交历史；`--match-head-commit` 避免在你检查后 PR 又被追加提交时误合并。这里不自动删除远端或本地分支，方便回查。参见 [gh pr merge](https://cli.github.com/manual/gh_pr_merge)。

## 8. 合并后更新本地 main

```powershell
git switch main
git pull --ff-only origin main
git log -3 --oneline
git status --short
```

合并是在 GitHub 发生的，本地 main 不会自动更新。旧修补分支可先保留；下一次从更新后的 main 建一个新分支，不要在已合并分支上无限追加新任务。

## 9. 如果不小心先在 main 上 commit 了

若尚未推送，不必删除提交或使用 `reset --hard`。先从当前提交创建分支来保留工作，再推送该分支：

```powershell
git switch -c fix/move-local-work
git push -u origin fix/move-local-work
```

之后仍按上述步骤创建并合并 PR。本地 main 暂时也指向原先的提交，不需要急着“清零”；使用保留历史的 merge 合并后，尝试 `git switch main` 和 `git pull --ff-only origin main`。若因 squash、rebase 或额外提交导致分叉，停止并核对历史，不要强制推送主分支。

## 10. 常见问题

| 现象 | 处理 |
| --- | --- |
| `could not read Username` / 无法弹出登录 | 先完成 `gh auth login` 和 `gh auth setup-git`，再重试 push |
| `gh` 或 `git` 不是命令 | 安装相应工具后重新打开终端 |
| `Permission denied` / 403 | 核对 `gh auth status` 的账户、仓库地址和写权限，不修改源码来掩盖认证问题 |
| `non-fast-forward` | 先 `git fetch origin`，用 `git log --oneline --graph --all -20` 检查分叉；不要 `push --force` |
| 修改不在 PR 中 | 检查是否保存、add、commit、push 到了正确的 head 分支 |
| 文件被 `.gitignore` 忽略 | 用 `git check-ignore -v -- 文件路径` 查原因，不要用 `git add -f` 强行加入程序包或密钥 |
| PR 合并了但下载仍是旧版 | PR 更新源码，不会自动上传安装包或建立 Release |

## 11. Release 与 README 版本要分开处理

正确顺序：源码合并并核对构建来源 → 打包与测试 → 完成分发检查 → 在 GitHub 创建 Release 草稿并上传 ZIP、Setup、SHA256SUMS → 公开发行 → 核对实际附件链接。

本项目已经移除自动工作流，`git push` / 合并 PR 不会自动发布安装包。发行附件应上传到 Release，不要 `git add` 到源码仓库。README 的源码版本可以先更新；下载入口使用 [Release 列表](https://github.com/Alanshown/HOK-BetaStudio/releases)，只在对应版本与附件确实公开后才写固定下载直链。

你可以在网页操作，也可以使用 [gh release create](https://cli.github.com/manual/gh_release_create)。先确认目标版本未存在，别重复创建或覆盖：

```powershell
gh release list --repo Alanshown/HOK-BetaStudio
```

上传前阅读 [构建说明](BUILD.md)和[发行检查清单](RELEASE-CHECKLIST.md)。本指南中的创建/合并命令是操作说明，不会因阅读文档而自动执行。
