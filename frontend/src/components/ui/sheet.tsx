// Adapted from shadcn/ui base-nova (MIT). See THIRD-PARTY.md.
import { Dialog } from '@base-ui/react/dialog'
import { X } from 'lucide-react'
import { Button } from './button'
import { cn } from '@/lib/utils'

export const Sheet = Dialog.Root
export const SheetTitle = Dialog.Title
export const SheetDescription = Dialog.Description
export function SheetContent({ children, className, closeDisabled = false, ...props }: Dialog.Popup.Props & { closeDisabled?: boolean }) {
  return <Dialog.Portal>
    <Dialog.Backdrop className="sheet-backdrop" />
    <Dialog.Popup className={cn('sheet-content', className)} {...props}>
      {children}
      <Dialog.Close disabled={closeDisabled} render={<Button variant="ghost" size="icon" className="sheet-close" aria-label="关闭设备设置" />}><X /></Dialog.Close>
    </Dialog.Popup>
  </Dialog.Portal>
}
