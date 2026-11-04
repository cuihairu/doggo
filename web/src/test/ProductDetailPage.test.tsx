import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import ProductDetailPage from '../pages/ProductDetailPage'
import { ApiError } from '../api/client'
import { ProfileContext, type ProfileContextValue } from '../ProfileContext'

const profile = {
  id: 'u1', phone: '13800000000', email: '', nickname: 'doggo',
  avatarUrl: '', inviteCode: 'X', invitedByUserId: null, status: 'active', isAdmin: false,
}

const product = {
  id: 'p1', name: '测试箱子', iconUrl: '', finalPriceCents: 1000,
  regularPriceCents: 1000, status: 3, contents: [], plans: [], quantityTiers: [],
}

vi.mock('../api/client', async () => {
  const actual = await vi.importActual('../api/client')
  return { ...actual, api: vi.fn() }
})
vi.mock('../api/events', () => ({ trackEvent: vi.fn() }))
vi.mock('../api/auth', () => ({ fetchProfile: vi.fn() }))

import { api } from '../api/client'
import { fetchProfile } from '../api/auth'

function renderWith(status: 'loading' | 'ready' | 'anonymous' | 'unknown', ctx?: Partial<ProfileContextValue>) {
  const value: ProfileContextValue = {
    profile: status === 'ready' ? profile : null,
    profileStatus: status,
    setProfile: vi.fn(),
    setProfileStatus: vi.fn(),
    ...ctx,
  }
  return render(
    <ProfileContext.Provider value={value}>
      <MemoryRouter initialEntries={['/products/p1']}>
        <Routes>
          <Route path="/products/:id" element={<ProductDetailPage />} />
          <Route path="/login" element={<div>login-page</div>} />
          <Route path="/orders/:id" element={<div>order-page</div>} />
        </Routes>
      </MemoryRouter>
    </ProfileContext.Provider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(api).mockImplementation(async (path: string) => {
    if (path === '/api/products/p1') return product
    if (path === '/api/products/p1/skus') return { items: [] }
    if (path === '/api/orders') return { orderId: 'o1' }
    throw new ApiError(404, 'not found')
  })
})

describe('product detail place order profile gate (audit: unknown retry)', () => {
  it('navigates to login when anonymous', async () => {
    renderWith('anonymous')
    await screen.findByText('测试箱子')
    await userEvent.click(screen.getByRole('button', { name: /立即开箱/ }))
    await waitFor(() => expect(screen.getByText('login-page')).toBeInTheDocument())
  })

  it('blocks with a message while profile is still loading', async () => {
    renderWith('loading')
    await screen.findByText('测试箱子')
    await userEvent.click(screen.getByRole('button', { name: /立即开箱/ }))
    await waitFor(() => expect(screen.getByText('登录状态检查中，请稍后再试')).toBeInTheDocument())
  })

  it('retries /me on unknown and places order when it recovers', async () => {
    const setStatus = vi.fn()
    vi.mocked(fetchProfile).mockResolvedValue(profile)
    renderWith('unknown', { setProfileStatus: setStatus })
    await screen.findByText('测试箱子')
    await userEvent.click(screen.getByRole('button', { name: /立即开箱/ }))
    await waitFor(() => expect(setStatus).toHaveBeenCalledWith('ready'))
    await waitFor(() => expect(screen.getByText('order-page')).toBeInTheDocument())
  })

  it('navigates to login when retry returns 401 (true sign-out)', async () => {
    const setStatus = vi.fn()
    vi.mocked(fetchProfile).mockRejectedValue(new ApiError(401, 'unauthorized'))
    renderWith('unknown', { setProfileStatus: setStatus })
    await screen.findByText('测试箱子')
    await userEvent.click(screen.getByRole('button', { name: /立即开箱/ }))
    await waitFor(() => expect(screen.getByText('login-page')).toBeInTheDocument())
    expect(setStatus).toHaveBeenCalledWith('anonymous')
  })

  it('shows a transient error (not login redirect) on non-401 retry failure', async () => {
    vi.mocked(fetchProfile).mockRejectedValue(new Error('network down'))
    renderWith('unknown')
    await screen.findByText('测试箱子')
    await userEvent.click(screen.getByRole('button', { name: /立即开箱/ }))
    await waitFor(() => expect(screen.getByText('登录状态暂时不可用，请稍后再试')).toBeInTheDocument())
    expect(screen.queryByText('login-page')).not.toBeInTheDocument()
  })
})
