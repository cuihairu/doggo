import { Animated, Easing, Platform, Pressable, StatusBar, StyleSheet, Text, View, TextStyle, ViewStyle } from 'react-native'
import { useEffect, useRef, type ReactNode } from 'react'
import Svg, { Path } from 'react-native-svg'
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

const ICON_PATHS: Record<'box' | 'warn' | 'check', string[]> = {
  box: ['M4 8l8-4 8 4v8l-8 4-8-4V8z', 'M4 8l8 4 8-4', 'M12 12v8'],
  warn: ['M12 3L2 20h20L12 3z', 'M12 10v4', 'M12 17.2v.3'],
  check: ['M4 12.5l5 5L20 6.5'],
}

/** Shared state icon (box/warn/check); stroke follows `color` (ui-system.md §6). */
export function StateIco({ name, color, size = 34 }: { name: 'box' | 'warn' | 'check'; color: string; size?: number }) {
  return (
    <Svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke={color}
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {ICON_PATHS[name].map((d) => (
        <Path key={d} d={d} />
      ))}
    </Svg>
  )
}

/** Loading skeleton: pulsing placeholder cards instead of "加载中…" text. */
export function Skeleton({ cards = 3 }: { cards?: number }) {
  const t = useTheme()
  const pulse = useRef(new Animated.Value(0)).current
  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(pulse, { toValue: 1, duration: 700, easing: Easing.inOut(Easing.quad), useNativeDriver: true }),
        Animated.timing(pulse, { toValue: 0, duration: 700, easing: Easing.inOut(Easing.quad), useNativeDriver: true })
      ])
    )
    loop.start()
    return () => loop.stop()
  }, [pulse])
  const opacity = pulse.interpolate({ inputRange: [0, 1], outputRange: [0.45, 1] })
  return (
    <View>
      {Array.from({ length: cards }, (_, index) => (
        <Animated.View key={index} style={[styles.skelCard, { borderColor: t.border, opacity }]}>
          <View style={[styles.skelLine, { backgroundColor: t.border, width: '60%' }]} />
          <View style={[styles.skelLine, { backgroundColor: t.border, width: '35%' }]} />
        </Animated.View>
      ))}
    </View>
  )
}

export function Price({ children }: { children: ReactNode }) {
  const t = useTheme()
  return <Text style={{ color: t.primary, fontWeight: '700', fontSize: 15 }}>{children}</Text>
}

/** Error state: warn icon + reason + retry; `children` renders an alternative path. */
export function Msg({ text, onRetry, hint, children }: { text: string; onRetry?: () => void; hint?: string; children?: ReactNode }) {
  const t = useTheme()
  return (
    <Card style={{ alignItems: 'center' }}>
      <StateIco name="warn" color={t.danger} />
      <Text style={{ color: t.danger, fontWeight: '700', marginTop: 8, textAlign: 'center' }}>{text}</Text>
      {hint ? <Muted style={{ marginTop: 2, textAlign: 'center' }}>{hint}</Muted> : null}
      {onRetry ? (
        <Btn tone="plain" onPress={onRetry} style={{ marginTop: 10, paddingHorizontal: 14 }}>
          重试
        </Btn>
      ) : null}
      {children}
    </Card>
  )
}

/** Empty state: icon + why it is empty + the next action (prototype .empty + ghost btn). */
export function EmptyState({
  title,
  hint,
  actionLabel,
  onAction
}: {
  title: string
  hint?: string
  actionLabel?: string
  onAction?: () => void
}) {
  const t = useTheme()
  return (
    <Card style={{ alignItems: 'center', paddingVertical: 22 }}>
      <StateIco name="box" color={t.muted} />
      <Text style={{ color: t.text, fontWeight: '700', marginTop: 8 }}>{title}</Text>
      {hint ? <Muted style={{ marginTop: 2, textAlign: 'center' }}>{hint}</Muted> : null}
      {actionLabel && onAction ? (
        <Btn tone="plain" onPress={onAction} style={{ marginTop: 10, paddingHorizontal: 16 }}>
          {actionLabel}
        </Btn>
      ) : null}
    </Card>
  )
}

/** Success state: what happened and where to go next. */
export function SuccessBox({
  title,
  detail,
  actionLabel,
  onAction
}: {
  title: string
  detail?: string
  actionLabel?: string
  onAction?: () => void
}) {
  const t = useTheme()
  return (
    <Card style={{ alignItems: 'center' }}>
      <StateIco name="check" color={t.success} />
      <Text style={{ color: t.success, fontWeight: '700', marginTop: 8 }}>{title}</Text>
      {detail ? <Muted style={{ marginTop: 2, textAlign: 'center' }}>{detail}</Muted> : null}
      {actionLabel && onAction ? (
        <Btn onPress={onAction} style={{ marginTop: 10, paddingHorizontal: 16 }}>
          {actionLabel}
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
  },
  skelCard: {
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    padding: 14,
    marginBottom: 12
  },
  skelLine: {
    height: 14,
    borderRadius: 4,
    marginTop: 8
  }
})
