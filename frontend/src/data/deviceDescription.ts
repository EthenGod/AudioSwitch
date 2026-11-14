import { t } from '../i18n'
import type { Device } from './types'

/** Translate at render time, so existing snapshots follow language changes too. */
export function deviceDescription(device: Device): string {
  switch (device.ConnectionType) {
    case 'output': return t('声音输出')
    case 'input': return t('麦克风输入')
    case 'usb-headphones': return t('USB Audio / 耳机')
    case 'display': return t('DisplayPort / 显示器')
    case 'saved-bluetooth': return t('Bluetooth / 已保留预设')
    case 'usb-microphone': return t('USB Audio / 麦克风')
    case 'built-in': return t('Realtek Audio / 内置')
    default: return device.Connection
  }
}
