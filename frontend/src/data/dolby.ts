export interface DolbyProfile {
  MainProfile: 0 | 4 | null; SubProfile: 4 | 5 | null
  Enabled: boolean | null; Surround: boolean | null; Dialog: boolean | null; Leveler: boolean | null; AutoSwitch: boolean | null
  SurroundStrength: number | null; DialogStrength: number | null; LevelerStrength: number | null
  Ieq: 0 | 2 | null; Eq: number[] | null
}
export interface DolbyRead { Token: string; Status: 'running' | 'ready' | 'cancelled' | 'error'; Message: string; Profile?: DolbyProfile }
export const emptyDolby = (): DolbyProfile => ({ MainProfile:null, SubProfile:null, Enabled:null, Surround:null, Dialog:null, Leveler:null, AutoSwitch:null, SurroundStrength:null, DialogStrength:null, LevelerStrength:null, Ieq:null, Eq:null })
export function dolbyProfile(value: unknown): DolbyProfile | null {
  if (value == null) return null
  if (typeof value !== 'object' || Array.isArray(value)) throw new Error('Dolby 方案格式无效。')
  const p = value as Record<string, unknown>, result = emptyDolby()
  if (Object.keys(p).some(key => !(key in result))) throw new Error('存在未识别的 Dolby 字段，已保留原方案。')
  for (const key of ['Enabled','Surround','Dialog','Leveler','AutoSwitch'] as const) {
    if (p[key] != null && typeof p[key] !== 'boolean') throw new Error('Dolby 开关格式无效。')
    result[key] = p[key] as boolean | null ?? null
  }
  for (const key of ['SurroundStrength','DialogStrength','LevelerStrength'] as const) {
    if (p[key] != null && (typeof p[key] !== 'number' || !Number.isFinite(p[key]) || p[key] < 0 || p[key] > 1)) throw new Error('Dolby 强度需要在 0–100% 之间。')
    result[key] = p[key] as number | null ?? null
  }
  if (![null,undefined,0,4].includes(p.MainProfile as number | null) || ![null,undefined,4,5].includes(p.SubProfile as number | null) || (p.MainProfile === 4 ? p.SubProfile !== 4 && p.SubProfile !== 5 : p.SubProfile != null)) throw new Error('请选择支持的 Dolby 预设。')
  result.MainProfile = p.MainProfile as DolbyProfile['MainProfile'] ?? null; result.SubProfile = p.SubProfile as DolbyProfile['SubProfile'] ?? null
  if (p.Ieq != null && (p.MainProfile !== 0 || ![0,2].includes(p.Ieq as number))) throw new Error('智能均衡器仅支持电影预设。')
  result.Ieq = p.Ieq as DolbyProfile['Ieq'] ?? null
  if (p.Eq != null) { if (p.MainProfile !== 4) throw new Error('均衡器需要自定义预设。'); result.Eq = rawEq(p.Eq) }
  return result
}
export function rawEq(value: unknown): number[] {
  if (!Array.isArray(value) || value.length !== 20 || value.some(v => !Number.isInteger(v) || v < -192 || v > 192)) throw new Error('均衡器需要 20 个 −192 到 192 的整数。')
  return [...value] as number[]
}
export const frequencies = ['32 Hz','64 Hz','125 Hz','250 Hz','500 Hz','1 kHz','2 kHz','4 kHz','8 kHz','16 kHz']
const anchors = [0,1,2,3,4,7,10,13,16,18], positions = [47,141,234,328,469,656,844,1031,1313,1688,2250,3000,3750,4688,5813,7125,9000,11250,13875,19688]
export function toTen(raw: number[]): number[] { const valid = rawEq(raw); return anchors.map(i => valid[i] / 16) }
const roundAway = (value: number) => Math.sign(value) * Math.floor(Math.abs(value) + 0.5)
/** Same calibrated positions and rounding as src/DolbyEqualizer.cs; see DOLBY-EQ.md. */
export function toTwenty(db: number[]): number[] {
  if (db.length !== 10 || db.some(v => !Number.isFinite(v) || v < -12 || v > 12)) throw new Error('均衡器需要 10 个 −12 到 12 dB 的数值。')
  const gains = db.map(v => roundAway(v * 16))
  return positions.map((position, i) => {
    let segment = 0; while (segment < 8 && i > anchors[segment + 1]) segment++
    const left = positions[anchors[segment]], span = positions[anchors[segment + 1]] - left
    return Math.max(-192, Math.min(192, roundAway((gains[segment] * span + (gains[segment + 1] - gains[segment]) * (position - left)) / span)))
  })
}
export function mapDolbyRead(value: unknown): DolbyRead {
  const p = value as DolbyRead
  if (!p || typeof p.Token !== 'string' || !p.Token || !['running','ready','cancelled','error'].includes(p.Status) || typeof p.Message !== 'string') throw new Error('Dolby 读取结果不完整，请重试或取消。')
  const profile = p.Status === 'ready' ? dolbyProfile(p.Profile) : null
  if (p.Status === 'ready' && !profile) throw new Error('Dolby 未返回有效方案，原草稿已保留。')
  return { Token:p.Token, Status:p.Status, Message:p.Message, ...(profile ? { Profile:profile } : {}) }
}
