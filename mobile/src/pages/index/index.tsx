import { ScrollView, StyleSheet, Text, View } from 'react-native'
import { useEffect, useState } from 'react'
import { fetchNews, NewsItemView } from '@doggo/shared'

export default function Index() {
  const [items, setItems] = useState<NewsItemView[]>([])
  const [error, setError] = useState('')

  useEffect(() => {
    fetchNews()
      .then((res) => setItems(res.items))
      .catch((e: Error) => setError(e.message))
  }, [])

  return (
    <ScrollView style={styles.page} contentContainerStyle={styles.content}>
      <Text style={styles.title}>Doggo</Text>
      {error ? <Text style={styles.error}>{error}</Text> : null}
      {items.map((item) => (
        <View key={item.id} style={styles.row}>
          <Text style={styles.rowTitle}>{item.title}</Text>
        </View>
      ))}
      {!items.length && !error ? <Text style={styles.muted}>加载中…</Text> : null}
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  page: { flex: 1, backgroundColor: '#f6f7f9' },
  content: { padding: 16, paddingTop: 48 },
  title: { fontSize: 24, fontWeight: '700' },
  error: { marginTop: 12, color: '#c0392b' },
  row: { paddingVertical: 10, borderBottomWidth: StyleSheet.hairlineWidth, borderColor: '#e5e5e5' },
  rowTitle: { fontSize: 16 },
  muted: { color: '#888' }
})
