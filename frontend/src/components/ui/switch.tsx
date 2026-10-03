// Adapted from shadcn/ui base-nova (MIT). See THIRD-PARTY.md.
import { Switch as Primitive } from '@base-ui/react/switch'
import { cn } from '@/lib/utils'

export function Switch({ className, ...props }: Primitive.Root.Props) {
  return <Primitive.Root data-slot="switch" className={cn('ui-switch', className)} {...props}>
    <Primitive.Thumb className="ui-switch-thumb" />
  </Primitive.Root>
}
