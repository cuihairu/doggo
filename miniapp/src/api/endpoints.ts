import Taro from '@tarojs/taro'
import { api, saveTokens } from '@doggo/shared'

// Endpoints shared with the mobile app live in the shared layer.
export * from '@doggo/shared'

interface TokenPair {
  userId: string
  nickname: string
  accessToken: string
  refreshToken: string
}

/** wx.login → POST /api/auth/wechat → JWT pair stored locally. Weapp-only entry. */
export async function wechatLogin(nickname?: string): Promise<TokenPair> {
  const { code } = await Taro.login()
  const tokens = await api<TokenPair>('/api/auth/wechat', {
    method: 'POST',
    data: { code, nickname },
    skipAuth: true
  })
  saveTokens(tokens.accessToken, tokens.refreshToken)
  return tokens
}
