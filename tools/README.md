# 工具导航

| 用途 | 入口 |
| --- | --- |
| Windows 定制版构建（当前推荐） | `build-merged-windows.ps1` |
| 上游 Windows 安装包构建 | `build-windows.sh` |
| AI 钩子安装、卸载及诊断 | `integrate.py` |
| 批量导入角色图片 | `import.py` |
| 单张图片处理 | `prepare.py` |
| 修正眨眼帧 | `fix_blink.py` |
| 处理头发间白缝 | `hairgap.py` |
| README 配图与宣传图 | `readme_images.py`、`promo_images.py` |
| 特效截图与检查 | `fxcheck/` |

所有命令均从仓库根目录运行。Windows 定制版构建方式见[项目说明](../README.md#构建和运行)。macOS 构建入口是 `app/build.sh`；根目录的 `.sh` 脚本是上游安装、更新和卸载入口。

Python 工具必须使用适合当前任务的独立 conda 环境，不使用 base 环境。临时脚本、日志和验证产物放入工作空间中的专用 `.task-*` 子目录。
