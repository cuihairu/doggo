import { useState } from 'react'
import { StyleSheet, Text, TextInput, View } from 'react-native'
import Taro from '@tarojs/taro'
import { accountLogin, accountRegister, ApiError } from '@doggo/shared'
import { SafeTop, Btn, Card, Muted } from '../../ui'
import { useTheme } from '../../theme'

/**
 * RN has no wx.login — the mobile app uses account credentials against
 * /api/auth/login and /api/auth/register (RN 差异处理).
 */
export default function LoginPage() {
  const t = useTheme()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [account, setAccount] = useState('')
  const [password, setPassword] = useState('')
  const [nickname, setNickname] = useState('')
  const [inviteCode, setInviteCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit() {
    if (!account.trim() || !password) {
      setError('请输入账号和密码')
      return
    }
    setBusy(true)
    setError('')
    try {
      const result = mode === 'login'
        ? await accountLogin(account.trim(), password)
        : await accountRegister(account.trim(), password, nickname.trim() || undefined, inviteCode.trim() || undefined)
      Taro.showToast({ title: `欢迎, ${result.nickname || account}`, icon: 'success' })
      Taro.navigateTo({ url: '/pages/user/user' })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : (mode === 'login' ? '登录失败，请稍后再试' : '注册失败，请稍后再试'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <View style={{ padding: 16 }}>
        <Card>
          <Text style={[styles.heading, { color: t.text }]}>{mode === 'login' ? '账号登录' : '注册账号'}</Text>
          <Muted>登录即同意《用户协议》与《隐私政策》</Muted>
          <TextInput
            placeholder="账号"
            placeholderTextColor={t.muted}
            value={account}
            onChangeText={setAccount}
            autoCapitalize="none"
            style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          <TextInput
            placeholder="密码"
            placeholderTextColor={t.muted}
            value={password}
            onChangeText={setPassword}
            secureTextEntry
            style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          {mode === 'register' && (
            <>
              <TextInput
                placeholder="昵称(可选)"
                placeholderTextColor={t.muted}
                value={nickname}
                onChangeText={setNickname}
                style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
              />
              <TextInput
                placeholder="邀请码(可选)"
                placeholderTextColor={t.muted}
                value={inviteCode}
                onChangeText={setInviteCode}
                autoCapitalize="none"
                style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
              />
            </>
          )}
          <Btn onPress={submit} busy={busy} style={{ marginTop: 8 }}>
            {mode === 'login' ? '登录' : '注册并登录'}
          </Btn>
          {error ? <Muted style={{ color: t.danger, marginTop: 8 }}>{error}</Muted> : null}
          <Btn
            tone="plain"
            onPress={() => { setMode(mode === 'login' ? 'register' : 'login'); setError('') }}
            style={{ marginTop: 10 }}
          >
            {mode === 'login' ? '没有账号？去注册' : '已有账号？去登录'}
          </Btn>
        </Card>
      </View>
    </View>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  heading: { fontSize: 20, fontWeight: '700' },
  input: {
    minHeight: 44,
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    marginTop: 12
  }
})
