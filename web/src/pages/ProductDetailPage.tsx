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
}

interface Sku {
  id: string
  planId: string
  quantity: number
  priceCents: number
  enabled: boolean
}

interface CreateOrderResult {
  orderId: string
  orderNo: string
  totalPriceCents: number
  totalCostCents: number
}

/** Product detail with SKU selection; placing an order goes to the pay page. */
export default function ProductDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [product, setProduct] = useState<ProductDetail | null>(null)
  const [skus, setSkus] = useState<Sku[]>([])
  const [selectedSkuId, setSelectedSkuId] = useState<string | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    api<ProductDetail>(`/api/products/${id}`).then(setProduct).catch((err) => setError(String(err.message ?? err)))
    api<{ items: Sku[] }>(`/api/products/${id}/skus`)
      .then((result) => setSkus(result.items.filter((sku) => sku.enabled)))
      .catch(() => setSkus([]))
  }, [id])

  async function placeOrder() {
    if (!id || !product) return
    if (!getAccessToken()) {
      navigate('/login')
      return
    }

    setError('')
    const sku = skus.find((item) => item.id === selectedSkuId)
    try {
      const result = await api<CreateOrderResult>('/api/orders', {
        body: {
          userId: (await api<{ id: string }>('/api/auth/me')).id,
          productId: id,
          planId: sku?.planId ?? null,
          optionId: sku?.id ?? null,
          entry: 1,
          serviceMode: 1,
          fulfillmentType: 0,
          payType: 0,
        },
      })
      navigate(`/orders/${result.orderId}`)
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  if (!product) return <p className="muted">{error || '加载中…'}</p>

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
          {skus.length > 0 && (
            <div style={{ margin: '14px 0' }}>
              <div className="muted" style={{ marginBottom: 6 }}>数量套餐</div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button
                  className={`btn ${selectedSkuId === null ? '' : 'secondary'}`}
                  onClick={() => setSelectedSkuId(null)}
                >
                  单个 ¥{(product.finalPriceCents / 100).toFixed(2)}
                </button>
                {skus.map((sku) => (
                  <button
                    key={sku.id}
                    className={`btn ${selectedSkuId === sku.id ? '' : 'secondary'}`}
                    onClick={() => setSelectedSkuId(sku.id)}
                  >
                    {sku.quantity} 个 ¥{(sku.priceCents / 100).toFixed(2)}
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
