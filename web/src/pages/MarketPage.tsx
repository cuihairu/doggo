import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

interface MarketListResult {
  total: number
  items: {
    id: string
    marketHashName: string
    latestPriceCents: number
    currency: string
    priceUpdatedAtUtc?: string
  }[]
}

interface ExchangeRate {
  currencyCode: string
  rate: number
}

const GAME_FILTERS: Record<string, string> = { '': '全部游戏', '0': '通用', '1': 'CS2', '2': 'Dota2' }
const PAGE_SIZE = 50

/** Market catalog: latest snapshot price, with an approximate CNY figure when
 * the captured currency is foreign (rates are display-only conversion). */
export default function MarketPage() {
  const [q, setQ] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [game, setGame] = useState('')
  const [items, setItems] = useState<MarketListResult['items']>([])
  const [total, setTotal] = useState(0)
  const [rates, setRates] = useState<Record<string, number>>({})
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  function buildUrl(page: number) {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (q.trim()) params.set('q', q.trim())
    if (game) params.set('game', game)
    return `/api/market/items?${params}`
  }

  useEffect(() => {
    api<ExchangeRate[]>('/api/exchange-rates')
      .then((list) => setRates(Object.fromEntries(list.map((r) => [r.currencyCode, r.rate]))))
      .catch(() => {})
  }, [])

  // Any filter change restarts from page 1.
  useEffect(() => {
    setBusy(true)
    api<MarketListResult>(buildUrl(1))
      .then((result) => {
        setItems(result.items)
        setTotal(result.total)
        setError('')
      })
      .catch((err) => setError(String(err.message ?? err)))
      .finally(() => setBusy(false))
    // buildUrl closes over q/game; only re-run when they change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, game])

  function loadMore() {
    // Items always fill complete pages, so the next page is derived from the count.
    const next = Math.floor(items.length / PAGE_SIZE) + 1
    setBusy(true)
    api<MarketListResult>(buildUrl(next))
      .then((result) => {
        setItems((prev) => [...prev, ...result.items])
        setTotal(result.total)
      })
      .catch(() => {})
      .finally(() => setBusy(false))
  }

  function cnyApprox(cents: number, currency: string): string | null {
    const rate = rates[currency]
    if (currency === 'CNY' || !rate || !cents) return null
    return `≈ ¥${((cents / 100) * rate).toFixed(2)}`
  }

  return (
    <div>
      <h1 className="page-title">行情</h1>
      {error && <p className="error-text">{error}</p>}
      <form
        style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap', alignItems: 'flex-end' }}
        onSubmit={(event) => {
          event.preventDefault()
          setQ(searchInput.trim())
        }}
      >
        <div className="field" style={{ flex: 1, minWidth: 200, marginBottom: 0 }}>
          <input
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            placeholder="搜索饰品名称"
          />
        </div>
        <select
          aria-label="按游戏筛选"
          value={game}
          onChange={(event) => setGame(event.target.value)}
          style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius)', padding: '8px 10px', fontSize: 14 }}
        >
          {Object.entries(GAME_FILTERS).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
        <button className="btn" type="submit">搜索</button>
      </form>
      <div className="card">
        <table className="table">
          <thead>
            <tr>
              <th>饰品</th>
              <th>最新价</th>
              <th>更新时间</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td>
                  <Link to={`/market/${item.id}`}>{item.marketHashName}</Link>
                </td>
                <td>
                  {(item.latestPriceCents / 100).toFixed(2)} {item.currency}{' '}
                  {cnyApprox(item.latestPriceCents, item.currency) && (
                    <span className="muted">{cnyApprox(item.latestPriceCents, item.currency)}</span>
                  )}
                </td>
                <td className="muted">{item.priceUpdatedAtUtc?.slice(0, 19).replace('T', ' ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {items.length === 0 && !busy && <p className="muted">没有符合条件的饰品</p>}
      </div>
      {items.length < total && (
        <div style={{ marginTop: 12, textAlign: 'center' }}>
          <button className="btn secondary" disabled={busy} onClick={loadMore}>
            {busy ? '加载中…' : `加载更多（已显示 ${items.length} / ${total}）`}
          </button>
        </div>
      )}
    </div>
  )
}
