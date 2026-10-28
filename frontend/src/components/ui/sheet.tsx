// Adapted from shadcn/ui base-nova (MIT). See THIRD-PARTY.md.
import { Dialog } from '@base-ui/react/dialog'
import { X } from 'lucide-react'
import { Button } from './button'
import { cn } from '@/lib/utils'

export const Sheet = Dialog.Root
export const SheetTitle = Dialog.Title
export const SheetDescription = Dialog.Description
export function SheetContent({ children, className, nested = false, closeDisabled = false, closeLabel = '关闭设备设置', ...props }: Dialog.Popup.Props & { nested?: boolean; closeDisabled?: boolean; closeLabel?: string }) {
  return <Dialog.Portal>
    <Dialog.Backdrop forceRender={nested} className={cn('sheet-backdrop', nested && 'sheet-backdrop-nested')} />
    <Dialog.Popup className={cn('sheet-content', nested && 'sheet-content-nested', className)} {...props}>
      {children}
      <Dialog.Close disabled={closeDisabled} render={<Button variant="ghost" size="icon" className="sheet-close" aria-label={closeLabel} />}><X /></Dialog.Close>
    </Dialog.Popup>
  </Dialog.Portal>
}
