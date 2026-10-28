// Adapted from shadcn/ui base-nova (MIT). See THIRD-PARTY.md.
import { Button as ButtonPrimitive } from '@base-ui/react/button'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

const buttonVariants = cva(
  'inline-flex shrink-0 items-center justify-center gap-2 rounded-md border text-[14px] font-medium transition-colors disabled:pointer-events-none disabled:opacity-40 [&_svg]:size-4 [&_svg]:shrink-0',
  { variants: {
    variant: {
      default: 'border-transparent bg-primary text-primary-foreground',
      outline: 'border-border bg-card text-foreground hover:bg-muted',
      ghost: 'border-transparent text-muted-foreground hover:bg-muted hover:text-foreground',
    },
    size: { default: 'h-9 px-3', sm: 'h-8 px-2.5 text-xs', icon: 'size-8 p-0' },
  }, defaultVariants: { variant: 'default', size: 'default' } },
)

export function Button({ className, variant, size, pending = false, disabled, ...props }: ButtonPrimitive.Props & VariantProps<typeof buttonVariants> & { pending?: boolean }) {
  return <ButtonPrimitive data-slot="button" data-variant={variant ?? 'default'} className={cn(buttonVariants({ variant, size }), className)} {...props} disabled={disabled || pending} focusableWhenDisabled={pending && !disabled} data-pending={pending && !disabled || undefined} aria-busy={pending || undefined} />
}
