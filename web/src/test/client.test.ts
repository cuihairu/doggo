import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError } from '../api/client'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('api transparent refresh (audit: refresh 5xx vs sign-out)', () => {
  it('retries the original request after a successful refresh', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockResolvedValueOnce(jsonResponse(200, {}))
      .mockResolvedValueOnce(jsonResponse(200, { ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api<{ ok: boolean }>('/api/me')).resolves.toEqual({ ok: true })
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('surfaces a 5xx refresh failure instead of the original 401 sign-out', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockResolvedValueOnce(jsonResponse(503, { title: 'refresh unavailable' }))
    vi.stubGlobal('fetch', fetchMock)

    const failure = await api('/api/me').catch((error: unknown) => error)
    expect(failure).toBeInstanceOf(ApiError)
    expect(failure).toMatchObject({ status: 503 })
  })
})
