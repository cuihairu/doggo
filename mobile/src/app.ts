import { setApiBase } from '@doggo/shared'

// Emulator default: 10.0.2.2 is the host loopback from the Android emulator.
// Override at bundle time with TARO_APP_API_BASE.
setApiBase(process.env.TARO_APP_API_BASE ?? 'http://10.0.2.2:5077')
