import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

interface ProductListResult {
  total: number
  items: {
    id: string
    name: string
    iconUrl?: string
    status: number
    finalPriceCents: number
    regularPriceCents: number
  }[]
}

interface RecommendView {
  id: string
  productId: string
  productName: string
  productIconUrl: string
  title: string
  description: string
}

const TYPE_FILTERS: Record<string, string> = { '': '全部类型', '0': '钥匙', '1': '箱子', '2': '箱子组件' }
const PAGE_SIZE = 24

/** Store front: operator recommends on top, every on-shelf product links to its detail page. */
export default function ProductListPage() {
  const [q, setQ] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [type, setType] = useState('')
  const [items, setItems] = useState<ProductListResult['items']>([])
  const [total, setTotal] = useState(0)
  const [recommends, setRecommends] = useState<RecommendView[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  function buildUrl(page: number) {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (q.trim()) params.set('q', q.trim())
    if (type) params.set('type', type)
    return `/api/products?${params}`
  }

  useEffect(() => {
    api<RecommendView[]>('/api/recommends').then(setRecommends).catch(() => {})
  }, [])

  // Any filter change restarts from page 1.
  useEffect(() => {
    setBusy(true)
    api<ProductListResult>(buildUrl(1))
      .then((result) => {
        setItems(result.items)
        setTotal(result.total)
        setError('')
      })
      .catch((err) => setError(String(err.message ?? err)))
      .finally(() => setBusy(false))
    // buildUrl closes over q/type; only re-run when they change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [q, type])

  function loadMore() {
    // Items always fill complete pages, so the next page is derived from the count.
    const next = Math.floor(items.length / PAGE_SIZE) + 1
    setBusy(true)
    api<ProductListResult>(buildUrl(next))
      .then((result) => {
        setItems((prev) => [...prev, ...result.items])
        setTotal(result.total)
      })
      .catch(() => {})
      .finally(() => setBusy(false))
  }

  const onShelf = items.filter((product) => product.status === 3)

  return (
    <div>
      <h1 className="page-title">商店</h1>
      {error && <p className="error-text">{error}</p>}
      <form
        style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap', alignItems: 'flex-end' }}
        onSubmit={(event) => {
          event.preventDefault()
          setQ(searchInput.trim())
        }}
      >
        <div className="field" style={{ flex: 1, minWidth: 200, marginBottom: 0 }}>
          <input
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            placeholder="搜索商品名称"
          />
        </div>
        <select
          aria-label="按类型筛选"
          value={type}
          onChange={(event) => setType(event.target.value)}
          style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius)', padding: '8px 10px', fontSize: 14 }}
        >
          {Object.entries(TYPE_FILTERS).map(([value, label]) => (
            <option key={value} value={value}>{label}</option>
          ))}
        </select>
        <button className="btn" type="submit">搜索</button>
      </form>
      {recommends.length > 0 && (
        <div style={{ display: 'flex', gap: 12, overflowX: 'auto', marginBottom: 16 }}>
          {recommends.map((recommend) => (
            <Link
              key={recommend.id}
              to={`/products/${recommend.productId}`}
              className="card"
              style={{ flex: '0 0 auto', width: 240, color: 'inherit', display: 'flex', gap: 10, alignItems: 'center' }}
            >
              {recommend.productIconUrl && (
                <img src={recommend.productIconUrl} alt="" style={{ width: 48, height: 48, objectFit: 'contain' }} />
              )}
              <span>
                <span style={{ fontSize: 14, display: 'block' }}>{recommend.title}</span>
                {recommend.description && (
                  <span className="muted" style={{ fontSize: 12, display: 'block' }}>{recommend.description}</span>
                )}
              </span>
            </Link>
          ))}
        </div>
      )}
      <div className="grid cols-4">
        {onShelf.map((product) => (
          <Link
            key={product.id}
            to={`/products/${product.id}`}
            className="card"
            style={{ color: 'inherit', textAlign: 'center' }}
          >
            {product.iconUrl && (
              <img src={product.iconUrl} alt="" style={{ width: 110, height: 110, objectFit: 'contain' }} />
            )}
            <div style={{ fontSize: 14, margin: '8px 0 4px' }}>{product.name}</div>
            <div style={{ color: 'var(--primary)', fontWeight: 600 }}>
              ¥{(product.finalPriceCents / 100).toFixed(2)}
            </div>
            {product.regularPriceCents > product.finalPriceCents && (
              <div className="muted" style={{ textDecoration: 'line-through' }}>
                ¥{(product.regularPriceCents / 100).toFixed(2)}
              </div>
            )}
          </Link>
        ))}
      </div>
      {onShelf.length === 0 && !busy && <p className="muted">没有符合条件的商品</p>}
      {/* Gate on rows fetched, not the on-shelf subset: total also covers
          self-open/group-open products, so the subset would disagree with it
          and "load more" would refetch the last page. */}
      {items.length < total && (
        <div style={{ marginTop: 12, textAlign: 'center' }}>
          <button className="btn secondary" disabled={busy} onClick={loadMore}>
            {busy ? '加载中…' : `加载更多（已加载 ${items.length} / ${total}）`}
          </button>
        </div>
      )}
    </div>
  )
}
