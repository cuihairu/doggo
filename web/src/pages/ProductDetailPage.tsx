import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, getAccessToken } from '../api/client'

interface ProductDetail {
  id: string
  name: string
  iconUrl?: string
  finalPriceCents: number
  regularPriceCents: number
  status: number
  quantityTiers?: { quantity: number; priceCents: number }[]
}

interface Sku {
  planId: string | null
  optionId: string | null
  serviceMode: string
  quantity: number
  minQuantity: number
  unitPriceCents: number
  totalPriceCents: number
  cashbackBps: number
  canUseCoupon: boolean
  canUseCoin: boolean
  priceSource: string
}

interface CreateOrderResult {
  orderId: string
  orderNo: string
  totalPriceCents: number
  totalCostCents: number
}

/** Product detail with SKU / quantity-tier selection; placing an order goes to
 * the pay page. Plan options price crate bundles; the quantity ladder applies
 * to plain buys without one (the server prices the whole order by tier). */
export default function ProductDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [product, setProduct] = useState<ProductDetail | null>(null)
  const [skus, setSkus] = useState<Sku[]>([])
  const [selectedSkuId, setSelectedSkuId] = useState<string | null>(null)
  const [selectedTier, setSelectedTier] = useState(0)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    api<ProductDetail>(`/api/products/${id}`).then(setProduct).catch((err) => setError(String(err.message ?? err)))
    api<{ items: Sku[] }>(`/api/products/${id}/skus`)
      .then((result) => setSkus(result.items))
      .catch(() => setSkus([]))
  }, [id])

  async function placeOrder() {
    if (!id || !product) return
    if (!getAccessToken()) {
      navigate('/login')
      return
    }

    setError('')
    const sku = skus.find((item) => item.optionId != null && item.optionId === selectedSkuId)
    try {
      const result = await api<CreateOrderResult>('/api/orders', {
        body: {
          userId: (await api<{ id: string }>('/api/auth/me')).id,
          productId: id,
          planId: sku?.planId ?? null,
          optionId: sku?.optionId ?? null,
          entry: 1,
          serviceMode: 1,
          fulfillmentType: 0,
          payType: 0,
          quantity: selectedTier || 1,
        },
      })
      navigate(`/orders/${result.orderId}`)
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  if (!product) return <p className="muted">{error || '加载中…'}</p>

  const planSkus = skus.filter((sku) => sku.optionId != null)
  const tiers = product.quantityTiers ?? []

  return (
    <div>
      <h1 className="page-title">{product.name}</h1>
      <div className="card" style={{ display: 'flex', gap: 24, alignItems: 'flex-start' }}>
        {product.iconUrl && (
          <img src={product.iconUrl} alt="" style={{ width: 180, height: 180, objectFit: 'contain' }} />
        )}
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 22, color: 'var(--primary)', fontWeight: 700 }}>
            ¥{(product.finalPriceCents / 100).toFixed(2)}
          </div>
          {(planSkus.length > 0 || tiers.length > 0) && (
            <div style={{ margin: '14px 0' }}>
              <div className="muted" style={{ marginBottom: 6 }}>数量套餐</div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button
                  className={`btn ${selectedSkuId === null && selectedTier === 0 ? '' : 'secondary'}`}
                  onClick={() => { setSelectedSkuId(null); setSelectedTier(0) }}
                >
                  单个 ¥{(product.finalPriceCents / 100).toFixed(2)}
                </button>
                {planSkus.map((sku) => (
                  <button
                    key={sku.optionId}
                    className={`btn ${selectedSkuId === sku.optionId ? '' : 'secondary'}`}
                    onClick={() => { setSelectedSkuId(sku.optionId); setSelectedTier(0) }}
                  >
                    {sku.quantity} 个 ¥{(sku.totalPriceCents / 100).toFixed(2)}
                  </button>
                ))}
                {tiers.map((tier) => (
                  <button
                    key={tier.quantity}
                    className={`btn ${selectedTier === tier.quantity ? '' : 'secondary'}`}
                    onClick={() => { setSelectedSkuId(null); setSelectedTier(tier.quantity) }}
                  >
                    {tier.quantity} 个 ¥{((tier.priceCents * tier.quantity) / 100).toFixed(2)}
                  </button>
                ))}
              </div>
            </div>
          )}
          {error && <p className="error-text">{error}</p>}
          <button className="btn" onClick={placeOrder} disabled={product.status !== 3}>
            {product.status === 3 ? '立即购买' : '已下架'}
          </button>
        </div>
      </div>
    </div>
  )
}
