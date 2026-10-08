# 前端来源与第三方许可

声间自有前端代码沿用仓库根目录的 GPL-3.0-only 许可。本目录独立构建；第五阶段使用单独候选包，未加入原有正式发布流程。

## 设计参考

参考 [Map1en/VRCX-0](https://github.com/Map1en/VRCX-0/tree/7c2cd64a4e6bafee299e58f5aa585d2602b7e8dc)，核对提交 `7c2cd64a4e6bafee299e58f5aa585d2602b7e8dc` 的 `components.json`、`src/styles/globals.css`、`AppShellLayout.tsx` 和 `AppSidebar.tsx`。采用相同的 React/TypeScript 技术方向及 neutral/base-nova 组件风格，参考细边框、紧凑侧栏和状态栏。声间的布局和业务代码独立编写，未复制其社交功能、品牌素材或 Rust 代码。根据首轮反馈，深浅主题颜色沿用本项目 `src/UiStyle.cs` 的蓝灰色 Palette；`public/mark.svg` 按本项目 `src/AppIcon.cs` 的 `DrawMark` 几何与颜色绘制。

## UI 组件

`src/components/ui/button.tsx`、`switch.tsx`、`sheet.tsx` 根据 [shadcn/ui](https://github.com/shadcn-ui/ui) 的官方 Base Nova registry 改写，保留 Base UI 的键盘、焦点和无障碍行为。

来源：https://ui.shadcn.com/r/styles/base-nova/button.json 、switch.json、sheet.json。原许可：https://github.com/shadcn-ui/ui/blob/main/LICENSE.md 。

MIT License

Copyright (c) 2023 shadcn

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## npm 运行依赖

第二阶段新增 `@tauri-apps/api`、构建用 `@tauri-apps/cli` 及 Rust Tauri 2 外壳。Tauri 使用 MIT / Apache-2.0 双许可，原文见 [Tauri 仓库](https://github.com/tauri-apps/tauri)。Rust 依赖及校验值锁定于 `src-tauri/Cargo.lock`。第五阶段的 `candidate-package.ps1` 从离线 Windows x64 Cargo 元数据归集依赖完整许可、NOTICE 和源码归档地址，包含构建依赖，写入 `THIRD-PARTY-RUST.txt`；缺失的上游许可原文及 Loader 许可见 [licenses/README.md](licenses/README.md)，未识别的缺失许可会阻止打包。前端许可脚本收集 API 包等 npm 运行依赖及 shadcn 的完整许可文字，候选包保留为 `THIRD-PARTY-FRONTEND.txt`。不生成安装器、不并入原有发布包；正式分发仍须提供与构建对应的完整项目源码。

React / React DOM、Base UI、class-variance-authority、clsx、tailwind-merge 使用其随包提供的 MIT 许可；Lucide 使用随包提供的 ISC 许可及其保留说明。完整版本及间接依赖固定于 `package-lock.json`。

`npm run build` 从实际安装的运行依赖收集原始 LICENSE 文本，写入 `dist/THIRD-PARTY-NOTICES.txt`；分发静态原型时应保留该文件。构建工具和测试工具不随静态界面运行。
