# 梦幻西游缓存清理工具

一键清理梦幻西游客户端缓存（`res3d_*`、`V3d_cache*` 文件）。双击运行，选择游戏目录即可；支持启动时自动清理、开机自启动、自动检查更新。

## 功能

- 递归清理 `res3d_*` 和 `V3d_cache*` 缓存文件，自动去掉只读属性，统计释放空间
- 启动时自动清理（可关）
- 开机自启动（HKCU 注册表，无需管理员权限）
- 自动检查更新：启动后后台检查 GitHub 最新 Release，有新版本时询问是否跳转下载
- 国内加速：更新检查和下载优先走 ghproxy 系镜像（mirror.ghproxy.com / ghproxy.net / ghfast.top / gh-proxy.com），全部失效时直连 GitHub 兜底

## 版本号

格式 `年月日.自增号`，如 `20260815.3`。年月日为构建日期，自增号由 GitHub Actions `run_number` 自动递增，每次构建产生新版本。

## 手动构建

仓库不提交 exe，由 GitHub Actions 自动构建。本地构建（需 .NET Framework 4.x，Windows 自带）：

```
csc /nologo /target:winexe /codepage:65001 /optimize+ /debug- /out:梦幻清理缓存.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  梦幻清理缓存.cs AssemblyInfo.cs
```

AssemblyInfo.cs 由构建流程生成：`AssemblyVersion` 固定 `1.0.0.0`（AssemblyVersion 段上限 65535，年月日会溢出）；显示版本（`Application.ProductVersion`）取 `AssemblyFileVersion` = `年月日.自增号`。

## 自动构建（GitHub Actions）

`.github/workflows/build.yml`：push 到 main 或手动触发 `workflow_dispatch` 时——

1. 计算版本号 `年月日.run_number`
2. 生成 AssemblyInfo.cs，用 .NET Framework 自带 csc 编译
3. 生成自签名代码签名证书并签名 exe
4. 发布 GitHub Release（tag `v<版本号>`），附带 `MHXYTempCleaner.exe`，供程序自动更新

## 签名说明

此项目使用自签名证书，仅用于标识构建来源，无公信力：SmartScreen 和杀毒软件可能仍会提示"未知发布者"。正式分发建议改用商业代码签名证书（把证书导入 Actions 的 Secrets 并在 workflow 中替换签名步骤即可）。

自检：`梦幻清理缓存.exe --selftest` 会验证清理逻辑和版本号解析逻辑并弹窗报告。

## 许可

仅供个人使用。