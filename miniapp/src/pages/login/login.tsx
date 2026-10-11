import { useEffect, useState } from 'react'
import { View, Text, Button, Input } from '@tarojs/components'
import Taro from '@tarojs/taro'
import { wechatLogin } from '../../api/endpoints'
import { ApiError, getAccessToken } from '../../api/client'

/**
 * Login = wx.login only. The miniapp never sees credentials; the code goes to
 * /api/auth/wechat and the server returns the token pair.
 */
export default function LoginPage() {
  const [nickname, setNickname] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (getAccessToken()) {
      Taro.switchTab({ url: '/pages/user/user' })
    }
  }, [])

  async function login() {
    setBusy(true)
    setError('')
    try {
      const result = await wechatLogin(nickname.trim() || undefined)
      Taro.showToast({ title: `欢迎, ${result.nickname}`, icon: 'success' })
      Taro.switchTab({ url: '/pages/user/user' })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '微信登录失败，请稍后再试')
    } finally {
      setBusy(false)
    }
  }

  return (
    <View className="card">
      <Text style={{ fontWeight: 700, fontSize: '32rpx' }}>微信一键登录</Text>
      <Text className="muted">登录即同意《用户协议》与《隐私政策》</Text>
      <Input
        placeholder="昵称(可选)"
        value={nickname}
        onInput={(e) => setNickname(e.detail.value)}
        style={{ border: '1rpx solid #eee', borderRadius: '8rpx', padding: '12rpx', margin: '20rpx 0' }}
      />
      <Button className="btn-primary" disabled={busy} onClick={login}>
        {busy ? '登录中…' : '微信登录'}
      </Button>
      {error && <Text className="muted" style={{ color: '#d0453a' }}>{error}</Text>}
    </View>
  )
}
