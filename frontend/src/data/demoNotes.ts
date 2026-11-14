import { t } from '../i18n'

// This is app-owned preview copy. Real release notes remain the publisher's text.
export function demoNotes() { return `## :sparkles: ${t('更新内容')}

- ${t('**浏览器面板**：支持自定义端口。')}
- :headphones: ${t('设备优先级支持拖动排序。')}

## :wrench: ${t('修复内容')}

- ${t('修复首次保存 Dolby 后的基础预设冲突。')}
- ${t('~~旧版说明~~ 现在支持 GitHub Markdown。')}

| ${t('项目')} | ${t('状态')} |
| --- | --- |
| ${t('主面板')} | ✅ ${t('可用')} |
| ${t('浏览器面板')} | ✅ ${t('可用')} |

> ${t('此处为界面演示，不代表实际发布信息。')}

- [x] ${t('深浅色预览')}
- [ ] ${t('实机验收')}

\`AudioSwitch.exe\`

[${t('查看项目')}](https://github.com/EthenGod/AudioSwitch)

<details><summary>${t('更多说明')}</summary>

${t('支持 **粗体**、*斜体*、列表、表格和代码。')}

</details>` }
