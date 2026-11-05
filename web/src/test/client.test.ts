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

  it('treats a 4xx refresh failure as transient, not the original 401 sign-out', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockResolvedValueOnce(jsonResponse(429, { title: 'rate limited' }))
    vi.stubGlobal('fetch', fetchMock)

    const failure = await api('/api/me').catch((error: unknown) => error)
    expect(failure).toBeInstanceOf(ApiError)
    expect(failure).toMatchObject({ status: 429 })
  })

  it('treats a 500 refresh failure as transient', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockResolvedValueOnce(jsonResponse(500, { title: 'server error' }))
    vi.stubGlobal('fetch', fetchMock)

    const failure = await api('/api/me').catch((error: unknown) => error)
    expect(failure).toBeInstanceOf(ApiError)
    expect(failure).toMatchObject({ status: 500 })
  })

  it('propagates a refresh 401 as the original 401 (definitive sign-out)', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockResolvedValueOnce(jsonResponse(401, { title: 'refresh rejected' }))
    vi.stubGlobal('fetch', fetchMock)

    const failure = await api('/api/me').catch((error: unknown) => error)
    expect(failure).toBeInstanceOf(ApiError)
    expect(failure).toMatchObject({ status: 401 })
  })

  it('retries after a 204 refresh success (no body)', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(jsonResponse(200, { ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api<{ ok: boolean }>('/api/me')).resolves.toEqual({ ok: true })
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('propagates a network failure from the refresh request', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(jsonResponse(401, { title: 'expired' }))
      .mockRejectedValueOnce(new TypeError('fetch failed'))
    vi.stubGlobal('fetch', fetchMock)

    await expect(api('/api/me')).rejects.toThrow('fetch failed')
  })
})
