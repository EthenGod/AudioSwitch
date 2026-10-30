import type { UpgradeJob, UpgradeKind } from './types'
import { demoNotes } from './demoNotes'
export const upgradeActive = (job: UpgradeJob | null) => !!job && ['checking', 'available', 'repairable', 'downloading', 'ready', 'installing'].includes(job.Status)
export function mapUpgrade(value: unknown, token?: string, kind?: UpgradeKind): UpgradeJob {
  const raw = value as UpgradeJob | null
  if (!raw || typeof raw.Token !== 'string' || !raw.Token || (token !== undefined && raw.Token !== token) || (kind !== undefined && raw.Kind !== kind)
    || !['update', 'repair'].includes(raw.Kind) || !['checking', 'available', 'repairable', 'current', 'passed', 'unavailable', 'downloading', 'ready', 'installing', 'handoff', 'cancelled', 'error'].includes(raw.Status)
    || typeof raw.Message !== 'string' || !raw.Message || typeof raw.CanDownload !== 'boolean'
    || !Number.isSafeInteger(raw.Bytes) || raw.Bytes < 0 || !Number.isSafeInteger(raw.Total) || raw.Total < 0
    || (raw.Version != null && typeof raw.Version !== 'string') || (raw.Notes != null && typeof raw.Notes !== 'string')
    || (raw.Directory != null && typeof raw.Directory !== 'string') || (raw.Entries != null && (!Array.isArray(raw.Entries) || raw.Entries.some(v => typeof v !== 'string')))
    || (raw.CanDownload && !['available', 'repairable'].includes(raw.Status))) throw new Error('维护结果不完整，请重新读取状态，勿重复提交。')
  return raw
}
/** In-memory demonstration only: never fetches, writes files or launches a process. */
export function mockUpgrade(includePrerelease: () => boolean = () => false) {
  let job: UpgradeJob | null = null, sequence = 0, finish = 0, committed = false
  function current(token: string) { if (!job || token !== job.Token) throw new Error('示例任务已失效。'); return job }
  return {
    async startUpgrade(kind: UpgradeKind) {
      if (upgradeActive(job) || committed) throw new Error('请先结束当前任务；模拟安装交接后请刷新预览。')
      job = { Token:String(++sequence), Kind:kind, Status:'checking', Message:'正在模拟检查…', CanDownload:false, Bytes:0, Total:8 * 1024 * 1024 }
      finish = Date.now() + 700; return structuredClone(job)
    },
    async readUpgrade(token: string) {
      const j = current(token)
      if (Date.now() >= finish) {
        if (j.Status === 'checking') Object.assign(j, { Status:j.Kind === 'update' ? 'available' : 'repairable', Message:j.Kind === 'update' ? '示例：发现可安装版本。' : '示例：发现文件缺损，可下载完整包修复。', Version: j.Kind === 'repair' ? 'v0.12.1（示例）' : includePrerelease() ? 'v0.13.0（测试版示例）' : 'v0.12.2（正式版示例）', Notes: j.Kind === 'update' ? demoNotes : undefined, Directory: j.Kind === 'repair' ? '示例程序目录' : undefined, Entries: j.Kind === 'repair' ? ['通过：AudioSwitch.exe', '通过：AudioSwitch.exe.config', '缺失：audio-switch-panel.exe', '无法读取：vendor/svcl/svcl.exe'] : undefined, CanDownload:true })
        if (j.Status === 'downloading') Object.assign(j, { Status:'ready', Message:'模拟下载与校验完成。', Bytes:j.Total })
        if (j.Status === 'installing') Object.assign(j, { Status:'handoff', Message:'模拟交接结束。没有安装、退出或重启任何程序。' })
      } else if (j.Status === 'downloading') j.Bytes = Math.min(j.Total, j.Bytes + 1024 * 1024)
      return structuredClone(j)
    },
    async upgradeAction(token: string, action: 'download' | 'install') {
      const j = current(token)
      if (action === 'download' && j.CanDownload) Object.assign(j, { Status:'downloading', CanDownload:false, Message:'正在模拟下载并校验…' })
      else if (action === 'install' && j.Status === 'ready' && !committed) { committed = true; Object.assign(j, { Status:'installing', Message:'正在模拟交接…' }) }
      else throw new Error('当前步骤不能执行此操作。')
      finish = Date.now() + 1600; return structuredClone(j)
    },
    async cancelUpgrade(token: string) {
      const j = current(token)
      if (committed) throw new Error('已提交安装，不能取消。')
      Object.assign(j, { Status:'cancelled', CanDownload:false, Message:'模拟任务已取消，没有修改文件或系统设置。' })
      return structuredClone(j)
    },
  }
}
