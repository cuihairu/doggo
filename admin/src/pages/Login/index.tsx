import { LoginFormPage, ProFormText } from '@ant-design/pro-components'
import { history, useModel } from '@umijs/max'
import { message } from 'antd'

/** Admin sign-in: the same account auth, gated on the role claim after login. */
export default function LoginPage() {
  const { initialState, setInitialState } = useModel('@@initialState')

  async function finish(values: { account: string; password: string }) {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(values),
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => ({ detail: '登录失败' }))
      message.error(problem.detail ?? '登录失败')
      return
    }

    const tokens = await response.json()
    localStorage.setItem('doggo.admin.accessToken', tokens.accessToken)
    const payload = JSON.parse(atob(tokens.accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    if (payload.role !== 'admin') {
      localStorage.removeItem('doggo.admin.accessToken')
      message.error('该账号没有后台权限')
      return
    }

    setInitialState({ isAdmin: true, nickname: tokens.nickname })
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
