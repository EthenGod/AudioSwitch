import { Select as Primitive } from '@base-ui/react/select'
import { Check, ChevronDown } from 'lucide-react'

export type SelectOption = { value: string; label: string }

export function Select({ id, label, value, options, disabled = false, pending = false, onValueChange }: {
  id?: string; label: string; value: string; options: SelectOption[]; disabled?: boolean; pending?: boolean
  onValueChange: (value: string) => void
}) {
  return <Primitive.Root value={value} items={options} disabled={disabled || pending} onValueChange={next => { if (next !== null) onValueChange(next) }}>
    <Primitive.Trigger id={id} aria-label={label} className="ui-select" data-pending={pending && !disabled || undefined} aria-busy={pending || undefined}>
      <Primitive.Value className="ui-select-value" />
      <Primitive.Icon className="ui-select-icon"><ChevronDown size={16} /></Primitive.Icon>
    </Primitive.Trigger>
    <Primitive.Portal>
      <Primitive.Positioner className="ui-select-positioner" sideOffset={6} alignItemWithTrigger={false}>
        <Primitive.Popup className="ui-select-popup">
          <Primitive.List className="ui-select-list">
            {options.map(option => <Primitive.Item key={option.value} value={option.value} className="ui-select-item">
              <Primitive.ItemText>{option.label}</Primitive.ItemText>
              <Primitive.ItemIndicator className="ui-select-check"><Check size={14} /></Primitive.ItemIndicator>
            </Primitive.Item>)}
          </Primitive.List>
        </Primitive.Popup>
      </Primitive.Positioner>
    </Primitive.Portal>
  </Primitive.Root>
}
