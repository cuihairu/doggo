import { Platform, Pressable, StatusBar, StyleSheet, Text, View, TextStyle, ViewStyle } from 'react-native'
import type { ReactNode } from 'react'
import { useTheme } from './theme'

/** Top inset: RN SafeAreaView on iOS, StatusBar height on Android. */
export function SafeTop({ style }: { style?: ViewStyle }) {
  if (Platform.OS === 'ios') {
    const { SafeAreaView } = require('react-native')
    return <SafeAreaView style={[{ backgroundColor: style?.backgroundColor }, style]} />
  }
  const height = StatusBar.currentHeight ?? 0
  return <View style={{ height, backgroundColor: style?.backgroundColor }} />
}

export function Card({ children, style }: { children: ReactNode; style?: ViewStyle }) {
  const t = useTheme()
  return <View style={[styles.card, { backgroundColor: t.card, borderColor: t.border }, style]}>{children}</View>
}

/**
 * Minimum touch target 44px (mobile hard requirement). Touchable without an
 * onPress renders as a styled block (used for emphasis rows).
 */
export function Btn({
  children,
  onPress,
  disabled,
  busy,
  tone,
  style
}: {
  children: ReactNode
  onPress?: () => void
  disabled?: boolean
  busy?: boolean
  tone?: 'primary' | 'plain' | 'danger'
  style?: ViewStyle
}) {
  const t = useTheme()
  const filled = tone !== 'plain'
  const bg = tone === 'danger' ? t.danger : t.primary
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled || busy || !onPress}
      style={({ pressed }) => [
        styles.btn,
        filled ? { backgroundColor: bg } : { backgroundColor: t.card, borderWidth: 1, borderColor: t.primary },
        (disabled || busy) && { opacity: 0.5 },
        pressed && { opacity: 0.85 },
        style
      ]}
    >
      <Text style={[styles.btnText, { color: filled ? t.primaryText : t.primary }]}>
        {busy ? '处理中…' : children}
      </Text>
    </Pressable>
  )
}

export function Muted({ children, style }: { children: ReactNode; style?: TextStyle }) {
  const t = useTheme()
  return <Text style={[{ color: t.muted, fontSize: 13 }, style]}>{children}</Text>
}

export function Price({ children }: { children: ReactNode }) {
  const t = useTheme()
  return <Text style={{ color: t.primary, fontWeight: '700', fontSize: 15 }}>{children}</Text>
}

/** Error / empty message with optional retry action. */
export function Msg({ text, onRetry, hint }: { text: string; onRetry?: () => void; hint?: string }) {
  const t = useTheme()
  return (
    <Card>
      <Text style={{ color: t.muted }}>{text}</Text>
      {hint ? <Muted style={{ marginTop: 4 }}>{hint}</Muted> : null}
      {onRetry ? (
        <Btn tone="plain" onPress={onRetry} style={{ alignSelf: 'flex-start', marginTop: 10, paddingHorizontal: 14 }}>
          重试
        </Btn>
      ) : null}
    </Card>
  )
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    padding: 14,
    marginBottom: 12
  },
  btn: {
    minHeight: 44,
    borderRadius: 8,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 18
  },
  btnText: {
    fontSize: 15,
    fontWeight: '600'
  }
})
