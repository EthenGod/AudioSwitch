import { CircleAlert, RefreshCw } from 'lucide-react'
import { Button } from './ui/button'

export function ConnectionError({ title, message, retry, busy }: { title: string; message: string; retry?: () => void; busy?: boolean }) {
  return <section className="connection-error">
    <div role="alert">
      <CircleAlert size={20} aria-hidden="true" />
      <h1>{title}</h1>
      <p>{message}</p>
    </div>
    {retry && <Button variant="outline" pending={busy} onClick={retry}><RefreshCw className={busy ? 'spin' : ''} />重试</Button>}
  </section>
}
