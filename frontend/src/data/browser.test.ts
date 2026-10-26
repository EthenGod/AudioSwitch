import { describe, expect, it, vi } from 'vitest'
import { browserToken, createBrowserConnection } from './browser'
import { backendFixture } from '../test/backendFixture'

describe('authenticated browser panel', () => {
  it('keeps the fragment token for reload and rejects remote or missing credentials', () => {
    sessionStorage.clear()
    const url = new URL(`http://127.0.0.1:12345/?browser#${'a'.repeat(64)}`) as unknown as Location
    expect(browserToken(url, sessionStorage)).toBe('a'.repeat(64))
    url.hash = ''
    expect(browserToken(url, sessionStorage)).toBe('a'.repeat(64))
    expect(() => browserToken(new URL('https://example.com/?browser') as unknown as Location, sessionStorage)).toThrow('本机')
    sessionStorage.clear()
    expect(() => browserToken(url, sessionStorage)).toThrow('失效')
  })
  it('uses the real gateway, authenticates every request, never retries writes, and detects stream closure', async () => {
    let stream!: ReadableStreamDefaultController<Uint8Array>
    const body = new ReadableStream<Uint8Array>({ start(controller) { stream = controller } })
    const reply = { ...backendFixture(), PanelApiVersion: 7, OperationError: null, PreferencesSaved: true }
    const request = vi.fn<typeof fetch>().mockResolvedValueOnce(new Response(body))
      .mockResolvedValueOnce(Response.json({ ok: true, value: reply }))
      .mockRejectedValueOnce(new TypeError('network disconnected'))
    const connection = createBrowserConnection('secret', request)
    const onEnd = vi.fn(); connection.subscribe(onEnd)
    expect(connection.gateway.mode).toBe('desktop')
    expect((await connection.gateway.read()).Devices).toHaveLength(4)
    await expect(connection.gateway.setPreference('GameMode', true)).rejects.toMatchObject({ requiresRefresh: true })
    expect(request).toHaveBeenCalledTimes(3)
    for (const [, init] of request.mock.calls) expect(init?.headers).toHaveProperty('X-AudioSwitch-Token', 'secret')
    expect(JSON.parse(request.mock.calls[2][1]!.body as string).command).toBe('panel_action')
    stream.close()
    await vi.waitFor(() => expect(onEnd).toHaveBeenCalledOnce())
    await expect(connection.gateway.read()).rejects.toThrow('连接已结束')
    expect(request).toHaveBeenCalledTimes(3)
  })
})
