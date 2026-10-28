// Adapted from shadcn/ui base-nova (MIT). See THIRD-PARTY.md.
import { Switch as Primitive } from '@base-ui/react/switch'
import { cn } from '@/lib/utils'

export function Switch({ className, pending = false, disabled, ...props }: Primitive.Root.Props & { pending?: boolean }) {
  return <Primitive.Root data-slot="switch" className={cn('ui-switch', className)} {...props} disabled={disabled || pending} data-pending={pending && !disabled || undefined} aria-busy={pending || undefined}>
    <Primitive.Thumb className="ui-switch-thumb" />
  </Primitive.Root>
}
