import { LoginFormPage, ProFormText } from '@ant-design/pro-components'
import { history, useModel } from '@umijs/max'
import { message } from 'antd'
import { clearToken } from '@/app'

/** Admin sign-in: the same account auth, gated on the role claim after login. */
export default function LoginPage() {
  const { initialState, setInitialState } = useModel('@@initialState')

  async function finish(values: { account: string; password: string }) {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Doggo-Client': 'admin',
      },
      body: JSON.stringify(values),
      credentials: 'same-origin',
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => ({ detail: '登录失败' }))
      message.error(problem.detail ?? '登录失败')
      return
    }

    const tokens = await response.json()
    // The access token is issued into an httpOnly cookie; the role claim is not
    // readable in JS, so confirm admin access via the /me profile before routing.
    const me = await fetch('/api/auth/me', { credentials: 'same-origin' })
    if (!me.ok) {
      clearToken()
      message.error('登录失败')
      return
    }
    const profile = await me.json()
    if (profile.isAdmin !== true) {
      clearToken()
      message.error('该账号没有后台权限')
      return
    }

    setInitialState({ isAdmin: true, nickname: profile.nickname })
    history.push('/dashboard')
  }

  void initialState

  return (
    <LoginFormPage title="Doggo 管理后台" subTitle="运营与管理" onFinish={finish as never}>
      <ProFormText name="account" label="账号" rules={[{ required: true, message: '请输入账号' }]} />
      <ProFormText.Password name="password" label="密码" rules={[{ required: true, message: '请输入密码' }]} />
    </LoginFormPage>
  )
}
