import { useCallback, useEffect, useState } from 'react'
import { ScrollView, StyleSheet, Text, TextInput, View } from 'react-native'
import Taro from '@tarojs/taro'
import { fetchOrder, OrderView, ApiError, kvGet, kvSet, yuan } from '@doggo/shared'
import { SafeTop, Btn, Card, Msg, Muted } from '../../ui'
import { useTheme } from '../../theme'

const STATUS_TEXT: Record<number, string> = {
  0: '待支付',
  1: '待结算',
  2: '待取回',
  3: '取回中',
  4: '已完成',
  5: '已取消',
  6: '已退款'
}

/**
 * Orders are looked up by id (no list endpoint): the app remembers the last
 * 10 order ids locally after a purchase, and an id can be pasted in manually.
 */
export default function OrderPage() {
  const t = useTheme()
  const [ids, setIds] = useState<string[]>([])
  const [orders, setOrders] = useState<Record<string, OrderView>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [manualId, setManualId] = useState('')

  const loadIds = useCallback(() => {
    setIds(kvGet<string[]>('doggo.orderIds') ?? [])
  }, [])

  const refresh = useCallback((id: string) => {
    fetchOrder(id)
      .then((order) => setOrders((prev) => ({ ...prev, [id]: order })))
      .catch((e) => setErrors((prev) => ({ ...prev, [id]: e instanceof ApiError ? e.message : '查询失败' })))
  }, [])

  useEffect(() => {
    loadIds()
  }, [loadIds])

  useEffect(() => {
    ids.forEach((id) => {
      if (!orders[id] && !errors[id]) refresh(id)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ids])

  function addManual() {
    const id = manualId.trim()
    if (!id) return
    const list = kvGet<string[]>('doggo.orderIds') ?? []
    kvSet('doggo.orderIds', [id, ...list.filter((x) => x !== id)].slice(0, 10))
    setManualId('')
    loadIds()
  }

  return (
    <View style={[styles.page, { backgroundColor: t.bg }]}>
      <SafeTop style={{ backgroundColor: t.bg }} />
      <ScrollView contentContainerStyle={{ padding: 16 }}>
        <Card>
          <Text style={[styles.section, { color: t.text }]}>查询订单</Text>
          <TextInput
            placeholder="输入订单 ID (guid)"
            placeholderTextColor={t.muted}
            value={manualId}
            onChangeText={setManualId}
            autoCapitalize="none"
            style={[styles.input, { backgroundColor: t.bg, borderColor: t.border, color: t.text }]}
          />
          <Btn onPress={addManual} disabled={!manualId.trim()}>查询</Btn>
          <Muted style={{ marginTop: 8 }}>下单后的订单会自动记录在这里</Muted>
        </Card>

        {ids.length === 0 ? <Msg text="还没有订单记录" /> : null}

        {ids.map((id) => {
          const order = orders[id]
          const err = errors[id]
          return (
            <Card key={id}>
              <View style={styles.row}>
                <Muted style={{ flex: 1 }} >{id.slice(0, 8)}…</Muted>
                <Btn tone="plain" onPress={() => refresh(id)} style={{ paddingHorizontal: 12 }}>刷新</Btn>
              </View>
              {order ? (
                <>
                  <View style={styles.row}>
                    <Text style={{ color: t.text }}>{order.orderNo}</Text>
                    <Text style={{ color: STATUS_COLOR[order.status] ?? t.text }}>{STATUS_TEXT[order.status] ?? `状态 ${order.status}`}</Text>
                  </View>
                  <View style={styles.row}>
                    <Muted>实付 ¥{yuan(order.totalCostCents)}</Muted>
                    <Muted>{String(order.createdAtUtc ?? '').slice(0, 19).replace('T', ' ')}</Muted>
                  </View>
                  {order.error ? <Muted style={{ color: t.danger }}>{order.error}</Muted> : null}
                </>
              ) : null}
              {err ? <Muted style={{ color: t.danger }}>{err}</Muted> : null}
            </Card>
          )
        })}
      </ScrollView>
    </View>
  )
}

const STATUS_COLOR: Record<number, string> = {
  4: '#2e9e5b',
  5: '#8a8f99',
  6: '#8a8f99'
}

const styles = StyleSheet.create({
  page: { flex: 1 },
  section: { fontSize: 16, fontWeight: '700', marginBottom: 8 },
  input: {
    minHeight: 44,
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    marginBottom: 10
  },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginTop: 8 }
})
