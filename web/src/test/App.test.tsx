import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import App from '../App'
import { ApiError } from '../api/client'

const profile = {
  id: 'u1', phone: '13800000000', email: '', nickname: 'doggo',
  avatarUrl: '', inviteCode: 'X', invitedByUserId: null, status: 'active', isAdmin: false,
}

vi.mock('../api/auth', () => ({
  fetchProfile: vi.fn(),
  logout: vi.fn(),
}))

vi.mock('../api/events', () => ({ trackEvent: vi.fn() }))

import { fetchProfile, logout } from '../api/auth'

function renderApp() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route element={<App />}>
          <Route path="/" element={<div>home</div>} />
          <Route path="/login" element={<div>login-page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
})

describe('profile status machine (audit: /me failure distinction)', () => {
  it('sets anonymous when /me returns 401 (true sign-out)', async () => {
    vi.mocked(fetchProfile).mockRejectedValue(new ApiError(401, 'unauthorized'))
    renderApp()
    await waitFor(() => expect(screen.getByRole('link', { name: '登录' })).toBeInTheDocument())
    expect(screen.queryByText('会话检查失败，重试')).not.toBeInTheDocument()
  })

  it('sets unknown on transient /me failure and offers retry instead of a login link', async () => {
    vi.mocked(fetchProfile).mockRejectedValue(new Error('network down'))
    renderApp()
    await waitFor(() => expect(screen.getByRole('link', { name: '会话检查失败，重试' })).toBeInTheDocument())
    expect(screen.queryByRole('link', { name: '登录' })).not.toBeInTheDocument()
  })

  it('retry recovers to signed-in when /me succeeds', async () => {
    vi.mocked(fetchProfile)
      .mockRejectedValueOnce(new Error('network down'))
      .mockResolvedValueOnce(profile)
    renderApp()
    const retry = await screen.findByRole('link', { name: '会话检查失败，重试' })
    await userEvent.click(retry)
    await waitFor(() => expect(screen.getByText('doggo')).toBeInTheDocument())
  })

  it('retry on 401 moves to signed-out login link', async () => {
    vi.mocked(fetchProfile)
      .mockRejectedValueOnce(new Error('network down'))
      .mockRejectedValueOnce(new ApiError(401, 'unauthorized'))
    renderApp()
    const retry = await screen.findByRole('link', { name: '会话检查失败，重试' })
    await userEvent.click(retry)
    await waitFor(() => expect(screen.getByRole('link', { name: '登录' })).toBeInTheDocument())
  })

  it('signout keeps the session visible and shows an error when logout fails', async () => {
    vi.mocked(fetchProfile).mockResolvedValue(profile)
    vi.mocked(logout).mockRejectedValue(new Error('offline'))
    renderApp()
    await waitFor(() => expect(screen.getByText('doggo')).toBeInTheDocument())
    await userEvent.click(screen.getByText('退出'))
    await waitFor(() => expect(screen.getByText('退出失败，请重试')).toBeInTheDocument())
    expect(screen.getByText('doggo')).toBeInTheDocument()
  })
})
